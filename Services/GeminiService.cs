using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TestAPIKeyCalling.Services;

public sealed class GeminiService(HttpClient client, IApiKeyReader keyReader, GeminiSettings settings, ILogger<GeminiService> logger) : IGeminiService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GeminiResult> GenerateAsync(string input, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        int? status = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (InputValidation.GetError(input) is { } inputError)
            {
                return Failure(GeminiError.Input, inputError);
            }

            if (!IsValidModel(settings.Model))
            {
                return Failure(GeminiError.Configuration, "Gemini:Model 設定錯誤，請填入單一模型識別字，不可包含 URL 或 models/ 前綴，修改後重新啟動。");
            }

            var key = await keyReader.ReadAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/{settings.Model}:generateContent");
            request.Headers.Add("x-goog-api-key", key);
            request.Content = JsonContent.Create(new
            {
                contents = new[]
                {
                    new { role = "user", parts = new[] { new { text = input } } }
                }
            });

            // Default ResponseContentRead keeps body reading inside HttpClient's timeout.
            using var response = await client.SendAsync(request, cancellationToken);
            status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                var authenticationError = response.StatusCode == HttpStatusCode.BadRequest
                    && await IsKeyErrorAsync(response, cancellationToken);
                return ClassifyHttpError(status.Value, authenticationError);
            }

            GenerateResponse? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<GenerateResponse>(JsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
                return Failure(GeminiError.InvalidResponse, "API 回應格式異常，請稍後再試。");
            }

            if (body is null)
            {
                return Failure(GeminiError.InvalidResponse, "API 回應格式異常，請稍後再試。");
            }

            var result = ParseResponse(body);
            // Defence in depth if an upstream response unexpectedly echoes credentials.
            return result.Text.Contains(key, StringComparison.Ordinal)
                ? Failure(GeminiError.InvalidResponse, "API 回應異常，無法安全顯示結果。")
                : result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A disconnected browser cancels work without creating a timeout diagnostic.
            throw;
        }
        catch (OperationCanceledException)
        {
            return Failure(GeminiError.Timeout, "請求逾時（60 秒），可再次送出測試。");
        }
        catch (KeyConfigurationException exception)
        {
            return Failure(GeminiError.Configuration, exception.Message);
        }
        catch (HttpRequestException)
        {
            return Failure(GeminiError.Network, "無法連線至 Gemini，請檢查網路連線。");
        }
        catch (Exception)
        {
            // Do not attach the exception: messages/stacks may contain credentials or bodies.
            return Failure(GeminiError.Unexpected, "測試失敗，請檢查設定後再試。");
        }

        GeminiResult Failure(GeminiError error, string message)
        {
            logger.LogWarning("Gemini error {Category}; HTTP {Status}; elapsed {ElapsedMilliseconds}ms",
                error, status, timer.ElapsedMilliseconds);
            return new GeminiResult(GeminiOutcome.Error, "", message, error);
        }

        GeminiResult ClassifyHttpError(int code, bool keyError)
        {
            return code switch
            {
                400 when keyError => Failure(GeminiError.Authentication, "認證失敗，請檢查 Key 與 API 存取限制。"),
                400 => Failure(GeminiError.Unexpected, "請求或服務設定不符，請檢查 Gemini 設定與服務可用性。"),
                401 or 403 => Failure(GeminiError.Authentication, "認證失敗或權限不足，請檢查 Key 與 API 存取限制。"),
                404 => Failure(GeminiError.ModelNotFound, "模型或 API 資源不存在，請檢查 Gemini:Model，修改後重新啟動。"),
                429 => Failure(GeminiError.Quota, "已達頻率或配額限制，請稍後重試或檢查配額。"),
                >= 500 and <= 599 => Failure(GeminiError.RemoteService, "Gemini 服務暫時異常，請稍後再試。"),
                _ => Failure(GeminiError.Unexpected, "Gemini 請求失敗，請稍後再試。")
            };
        }

        GeminiResult ParseResponse(GenerateResponse body)
        {
            if (body.PromptFeedback?.BlockReason is { Length: > 0 } blockReason
                && blockReason != "BLOCK_REASON_UNSPECIFIED")
            {
                return Failure(GeminiError.Blocked, "內容遭阻擋，請調整輸入再試。");
            }

            if (body.Candidates is not { Count: > 0 })
            {
                return Failure(GeminiError.NoText, "API 未回傳可顯示的文字。");
            }

            var candidate = body.Candidates[0];
            if (candidate is null)
            {
                return Failure(GeminiError.InvalidResponse, "API 回應格式異常，請稍後再試。");
            }

            if (candidate.FinishReason is "SAFETY" or "RECITATION" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "SPII"
                or "IMAGE_SAFETY" or "IMAGE_PROHIBITED_CONTENT" or "IMAGE_RECITATION")
            {
                return Failure(GeminiError.Blocked, "內容遭阻擋，請調整輸入再試。");
            }

            if (candidate.FinishReason is not ("STOP" or "MAX_TOKENS"))
            {
                return Failure(GeminiError.InvalidResponse, "API 回覆非正常結束，請調整輸入或稍後再試。");
            }

            var text = string.Concat(candidate.Content?.Parts?
                .Where(part => part is not null && !part.Thought)
                .Select(part => part!.Text) ?? []);
            if (string.IsNullOrWhiteSpace(text))
            {
                return Failure(GeminiError.NoText, candidate.FinishReason == "MAX_TOKENS"
                    ? "API 未回傳可顯示的文字；回覆已達長度限制。"
                    : "API 未回傳可顯示的文字。");
            }

            return candidate.FinishReason == "MAX_TOKENS"
                ? new GeminiResult(GeminiOutcome.Truncated, text, "回覆已達長度限制，內容可能不完整")
                : new GeminiResult(GeminiOutcome.Success, text, "測試成功");
        }
    }

    private static bool IsValidModel(string model)
    {
        return model.Length > 0 && char.IsAsciiLetterOrDigit(model[0])
            && model.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
    }

    private static async Task<bool> IsKeyErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object
                || !error.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            return details.EnumerateArray().Any(detail => detail.ValueKind == JsonValueKind.Object
                && detail.TryGetProperty("@type", out var type) && type.ValueKind == JsonValueKind.String
                && type.GetString() == "type.googleapis.com/google.rpc.ErrorInfo"
                && detail.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String
                && reason.GetString() is "API_KEY_INVALID" or "API_KEY_EXPIRED" or "API_KEY_NOT_FOUND" or "API_KEY_SERVICE_BLOCKED");
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed class GenerateResponse
    {
        public PromptFeedback? PromptFeedback { get; set; }
        public List<Candidate?>? Candidates { get; set; }
    }

    private sealed class PromptFeedback
    {
        public string? BlockReason { get; set; }
    }

    private sealed class Candidate
    {
        public string? FinishReason { get; set; }
        public Content? Content { get; set; }
    }

    private sealed class Content
    {
        public List<Part?>? Parts { get; set; }
    }

    private sealed class Part
    {
        public string? Text { get; set; }
        public bool Thought { get; set; }
    }
}