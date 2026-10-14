using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using TestAPIKeyCalling.Services;

namespace TestAPIKeyCalling.Pages;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ApiKeyChatModel(IGeminiChatService gemini, IChatSessionStore sessions) : PageModel
{
    private const string SessionCookieName = "ApiKeyChat.Session";
    private const int MaximumMessageLength = 4000;

    [BindProperty]
    public string ApiKey { get; set; } = "";

    [BindProperty]
    public string SelectedModelId { get; set; } = GeminiChatModels.DefaultModelId;

    public IReadOnlyList<GeminiChatModelOption> ModelOptions => GeminiChatModels.All;
    public bool HasSession { get; private set; }
    public string CurrentModelId { get; private set; } = GeminiChatModels.DefaultModelId;
    public IReadOnlyList<GeminiChatMessage> Messages { get; private set; } = [];
    public int CompletedRounds { get; private set; }
    public bool LastResponseTruncated { get; private set; }
    public string StatusMessage { get; private set; } = "";
    public string StatusStyle { get; private set; } = "info";

    public void OnGet()
    {
        PreventCaching();
        LoadSession();
    }

    public async Task<IActionResult> OnPostBeginAsync()
    {
        PreventCaching();
        var modelId = HasPostedModelId() ? SelectedModelId : GeminiChatModels.DefaultModelId;
        if (!GeminiChatModels.IsAllowed(modelId))
        {
            ApiKey = "";
            ModelState.Remove(nameof(ApiKey));
            return BadRequest();
        }

        var oldSessionIndex = GetSessionIndex();
        if (oldSessionIndex is not null)
        {
            await sessions.RemoveAsync(oldSessionIndex, HttpContext.RequestAborted);
            DeleteSessionCookie();
        }

        var error = ApiKeyValidation.GetError(ApiKey, out var normalizedKey);
        ApiKey = "";
        ModelState.Remove(nameof(ApiKey));
        if (error is not null)
        {
            StatusMessage = error;
            StatusStyle = "danger";
            return Page();
        }

        var sessionIndex = sessions.Create(normalizedKey!, modelId);
        Response.Cookies.Append(SessionCookieName, sessionIndex, SessionCookieOptions());
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostChangeModelAsync()
    {
        PreventCaching();
        if (!HasPostedModelId() || !GeminiChatModels.IsAllowed(SelectedModelId))
        {
            return BadRequest();
        }

        if (GetSessionIndex() is not { } sessionIndex)
        {
            DeleteSessionCookie();
            return RedirectToPage();
        }

        var status = await sessions.ChangeModelAsync(sessionIndex, SelectedModelId, HttpContext.RequestAborted);
        if (status == ChatModelChangeStatus.InvalidModel)
        {
            return BadRequest();
        }

        if (status == ChatModelChangeStatus.SessionExpired)
        {
            DeleteSessionCookie();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostNewChatAsync()
    {
        PreventCaching();
        if (GetSessionIndex() is { } sessionIndex)
        {
            await sessions.ResetConversationAsync(sessionIndex, HttpContext.RequestAborted);
        }
        else
        {
            DeleteSessionCookie();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostClearKeyAsync()
    {
        PreventCaching();
        if (GetSessionIndex() is { } sessionIndex)
        {
            await sessions.RemoveAsync(sessionIndex, HttpContext.RequestAborted);
        }
        DeleteSessionCookie();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSendAsync()
    {
        PreventCaching();
        var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
        var message = form["Message"].ToString();
        var requestIdText = form["RequestId"].ToString();
        if (string.IsNullOrWhiteSpace(message))
        {
            return ChatJson(StatusCodes.Status400BadRequest, "invalid", "請輸入要傳送的文字。");
        }
        if (message.Length > MaximumMessageLength)
        {
            return ChatJson(StatusCodes.Status400BadRequest, "invalid", "訊息不可超過 4,000 個 UTF-16 單位。");
        }
        if (!Guid.TryParseExact(requestIdText, "D", out var requestId))
        {
            return ChatJson(StatusCodes.Status400BadRequest, "invalid", "送出識別碼格式不正確，請重新送出。");
        }
        if (GetSessionIndex() is not { } sessionIndex)
        {
            DeleteSessionCookie();
            return ChatJson(StatusCodes.Status410Gone, "sessionExpired", "工作階段已失效，請重新輸入 Key。");
        }

        var start = sessions.TryBeginSend(sessionIndex, requestId, message);
        switch (start.Status)
        {
            case ChatSendStatus.Busy:
                return ChatJson(StatusCodes.Status409Conflict, "busy", "上一則訊息仍在處理中，請稍候。");
            case ChatSendStatus.SessionExpired:
                DeleteSessionCookie();
                return ChatJson(StatusCodes.Status410Gone, "sessionExpired", "工作階段已失效，請重新輸入 Key。");
            case ChatSendStatus.RequestIdConflict:
                return ChatJson(StatusCodes.Status400BadRequest, "requestIdConflict", "送出識別碼已用於其他訊息，請重新送出。");
            case ChatSendStatus.RoundLimit:
                return ChatJson(StatusCodes.Status409Conflict, "roundLimit", "此對話已達 20 組上限，請開始新對話。");
            case ChatSendStatus.Replay:
                return start.Result is { } saved
                    ? ResultJson(saved)
                    : ChatJson(StatusCodes.Status500InternalServerError, "error", "請求失敗，請重新整理後再試。");
        }

        using var lease = start.Lease!;
        try
        {
            var requestMessages = lease.History.Append(new GeminiChatMessage("user", message)).ToArray();
            var result = await gemini.GenerateAsync(lease.ModelId, lease.ApiKey, requestMessages, HttpContext.RequestAborted);
            if (result.Outcome is GeminiOutcome.Success or GeminiOutcome.Truncated)
            {
                if (string.IsNullOrWhiteSpace(result.Text) || !lease.Complete(result))
                {
                    return ChatJson(StatusCodes.Status500InternalServerError, "error", "API 未回傳可安全保存的文字，請稍後再試。");
                }
            }

            return ResultJson(result);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception)
        {
            return ChatJson(StatusCodes.Status200OK, "error", "請求失敗，請稍後再試。");
        }
    }

    private IActionResult ResultJson(GeminiResult result)
    {
        var status = result.Outcome switch
        {
            GeminiOutcome.Success => "success",
            GeminiOutcome.Truncated => "truncated",
            _ => "error"
        };
        return ChatJson(StatusCodes.Status200OK, status, result.Message, result.Text, result.Error.ToString());
    }

    private JsonResult ChatJson(int statusCode, string status, string message, string text = "", string error = "")
    {
        PreventCaching();
        Response.StatusCode = statusCode;
        return new JsonResult(new { status, message, text, error });
    }

    private void LoadSession()
    {
        var sessionIndex = GetSessionIndex();
        var snapshot = sessionIndex is null ? null : sessions.Get(sessionIndex);
        if (snapshot is null)
        {
            if (sessionIndex is not null)
            {
                DeleteSessionCookie();
                StatusMessage = "工作階段已失效，請重新輸入 Key。";
            }
            return;
        }

        HasSession = true;
        CurrentModelId = snapshot.ModelId;
        SelectedModelId = snapshot.ModelId;
        Messages = snapshot.Messages;
        CompletedRounds = snapshot.CompletedRounds;
        LastResponseTruncated = snapshot.LastResponseTruncated;
    }

    private string? GetSessionIndex()
    {
        return Request.Cookies.TryGetValue(SessionCookieName, out var sessionIndex) ? sessionIndex : null;
    }

    private bool HasPostedModelId()
    {
        return Request.HasFormContentType && Request.Form.ContainsKey(nameof(SelectedModelId));
    }

    private CookieOptions SessionCookieOptions()
    {
        return new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = Request.IsHttps,
            Path = "/ApiKeyChat",
            IsEssential = true
        };
    }

    private void DeleteSessionCookie()
    {
        Response.Cookies.Delete(SessionCookieName, SessionCookieOptions());
    }

    private void PreventCaching()
    {
        Response.Headers.CacheControl = "no-store, no-cache";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
    }
}