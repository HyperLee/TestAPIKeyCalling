using System.Security.Cryptography;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace TestAPIKeyCalling.Services;

public enum ChatSendStatus
{
    Started,
    Replay,
    Busy,
    SessionExpired,
    RequestIdConflict,
    RoundLimit
}

public sealed record ChatSessionSnapshot(IReadOnlyList<GeminiChatMessage> Messages, int CompletedRounds, bool LastResponseTruncated);

public sealed record ChatSendStart(ChatSendStatus Status, ChatSendLease? Lease = null, GeminiResult? Result = null);

public interface IChatSessionStore
{
    string Create(string apiKey);
    ChatSessionSnapshot? Get(string sessionIndex);
    ChatSendStart TryBeginSend(string sessionIndex, Guid requestId, string input);
    Task ResetConversationAsync(string sessionIndex, CancellationToken cancellationToken);
    Task RemoveAsync(string sessionIndex, CancellationToken cancellationToken);
}

public sealed class ChatSendLease : IDisposable
{
    private ChatSessionState? _state;

    internal ChatSendLease(ChatSessionState state, Guid requestId, string input, string apiKey, IReadOnlyList<GeminiChatMessage> history)
    {
        _state = state;
        RequestId = requestId;
        Input = input;
        ApiKey = apiKey;
        History = history;
    }

    internal Guid RequestId { get; }
    internal string Input { get; }
    public string ApiKey { get; }
    public IReadOnlyList<GeminiChatMessage> History { get; }

    public bool Complete(GeminiResult result)
    {
        var state = _state;
        if (state is null || result.Outcome is not (GeminiOutcome.Success or GeminiOutcome.Truncated)
            || string.IsNullOrWhiteSpace(result.Text))
        {
            return false;
        }

        lock (state.Sync)
        {
            if (state.CompletedRequests.ContainsKey(RequestId))
            {
                return false;
            }

            state.Messages.Add(new GeminiChatMessage("user", Input));
            state.Messages.Add(new GeminiChatMessage("model", result.Text));
            state.CompletedRequests.Add(RequestId, new ChatCompletedRequest(Input, result));
            state.LastResponseTruncated = result.Outcome == GeminiOutcome.Truncated;
            return true;
        }
    }

    public void Dispose()
    {
        var state = Interlocked.Exchange(ref _state, null);
        state?.OperationGate.Release();
    }
}

public sealed class MemoryChatSessionStore(IMemoryCache memoryCache, TimeSpan? idleTimeout = null) : IChatSessionStore
{
    public const int MaximumRounds = 20;
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(30);

    private readonly TimeSpan _idleTimeout = idleTimeout ?? DefaultIdleTimeout;

    public string Create(string apiKey)
    {
        if (ApiKeyValidation.GetError(apiKey, out var normalizedKey) is not null)
        {
            throw new ArgumentException("Key format is invalid.", nameof(apiKey));
        }

        string sessionIndex;
        ChatSessionState state;
        do
        {
            sessionIndex = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            state = new ChatSessionState(normalizedKey!);
        }
        while (memoryCache.TryGetValue(sessionIndex, out object? _));

        memoryCache.Set(sessionIndex, state, new MemoryCacheEntryOptions().SetSlidingExpiration(_idleTimeout));
        return sessionIndex;
    }

    public ChatSessionSnapshot? Get(string sessionIndex)
    {
        var state = GetState(sessionIndex);
        if (state is null)
        {
            return null;
        }

        lock (state.Sync)
        {
            return new ChatSessionSnapshot(state.Messages.ToArray(), state.Messages.Count / 2, state.LastResponseTruncated);
        }
    }

    public ChatSendStart TryBeginSend(string sessionIndex, Guid requestId, string input)
    {
        var state = GetState(sessionIndex);
        if (state is null)
        {
            return new ChatSendStart(ChatSendStatus.SessionExpired);
        }

        lock (state.Sync)
        {
            if (state.PendingControls > 0 || !state.OperationGate.Wait(0))
            {
                return new ChatSendStart(ChatSendStatus.Busy);
            }

            if (state.CompletedRequests.TryGetValue(requestId, out var completed))
            {
                state.OperationGate.Release();
                return completed.Input == input
                    ? new ChatSendStart(ChatSendStatus.Replay, Result: completed.Result)
                    : new ChatSendStart(ChatSendStatus.RequestIdConflict);
            }

            if (state.Messages.Count / 2 >= MaximumRounds)
            {
                state.OperationGate.Release();
                return new ChatSendStart(ChatSendStatus.RoundLimit);
            }

            var lease = new ChatSendLease(state, requestId, input, state.ApiKey, state.Messages.ToArray());
            return new ChatSendStart(ChatSendStatus.Started, lease);
        }
    }

    public Task ResetConversationAsync(string sessionIndex, CancellationToken cancellationToken)
    {
        return RunControlAsync(sessionIndex, clearKey: false, cancellationToken);
    }

    public Task RemoveAsync(string sessionIndex, CancellationToken cancellationToken)
    {
        return RunControlAsync(sessionIndex, clearKey: true, cancellationToken);
    }

    private async Task RunControlAsync(string sessionIndex, bool clearKey, CancellationToken cancellationToken)
    {
        var state = GetState(sessionIndex);
        if (state is null)
        {
            return;
        }

        lock (state.Sync)
        {
            state.PendingControls++;
        }

        var acquired = false;
        try
        {
            await state.OperationGate.WaitAsync(cancellationToken);
            acquired = true;
            lock (state.Sync)
            {
                state.Messages.Clear();
                state.CompletedRequests.Clear();
                state.LastResponseTruncated = false;
                if (clearKey)
                {
                    state.ApiKey = "";
                    memoryCache.Remove(sessionIndex);
                }
            }
        }
        finally
        {
            lock (state.Sync)
            {
                state.PendingControls--;
            }
            if (acquired)
            {
                state.OperationGate.Release();
            }
        }
    }

    private ChatSessionState? GetState(string sessionIndex)
    {
        if (string.IsNullOrEmpty(sessionIndex) || sessionIndex.Length != 43
            || sessionIndex.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-')))
        {
            return null;
        }

        return memoryCache.TryGetValue(sessionIndex, out ChatSessionState? state) ? state : null;
    }
}

public static class ApiKeyValidation
{
    public static string? GetError(string? value, out string? normalized)
    {
        normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return "請輸入 API Key。";
        }

        return normalized.Any(character => character < '!' || character > '~')
            ? "Key 格式不正確，請確認沒有內部空白或特殊字元。"
            : null;
    }
}

internal sealed class ChatSessionState(string apiKey)
{
    public object Sync { get; } = new();
    public SemaphoreSlim OperationGate { get; } = new(1, 1);
    public string ApiKey { get; set; } = apiKey;
    public List<GeminiChatMessage> Messages { get; } = [];
    public Dictionary<Guid, ChatCompletedRequest> CompletedRequests { get; } = [];
    public bool LastResponseTruncated { get; set; }
    public int PendingControls { get; set; }
}

internal sealed record ChatCompletedRequest(string Input, GeminiResult Result);
