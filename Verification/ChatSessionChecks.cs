using System.Collections;

using Microsoft.Extensions.Caching.Memory;

using TestAPIKeyCalling.Services;

using ApplicationProgram = TestAPIKeyCalling.Program;

namespace Verification;

internal static partial class Program
{
    private static async Task ChatSessionTests()
    {
        await Check("Session_Index_IsRandomBase64Url256BitValue", () =>
        {
            using var fixture = new SessionFixture();
            var index = fixture.Create("synthetic-key-A");
            Require(index.Length == 43 && index.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'));
            Require(!index.Contains("synthetic-key-A", StringComparison.Ordinal));
            return Task.CompletedTask;
        });

        await Check("Session_IsolatesKeysAndHistories", () =>
        {
            using var fixture = new SessionFixture();
            var first = fixture.Create("synthetic-key-A");
            var second = fixture.Create("synthetic-key-B");
            var firstSend = fixture.Begin(first, Guid.NewGuid(), "第一則");
            var secondSend = fixture.Begin(second, Guid.NewGuid(), "另一則");
            Require(fixture.Status(firstSend) == "Started" && fixture.Status(secondSend) == "Started");
            Require(fixture.ApiKey(firstSend) == "synthetic-key-A" && fixture.ApiKey(secondSend) == "synthetic-key-B");
            fixture.Complete(firstSend, new GeminiResult(GeminiOutcome.Success, "回覆 A", "success"));
            fixture.DisposeLease(firstSend);
            Require(fixture.MessageCount(fixture.Get(first)) == 2);
            Require(fixture.MessageCount(fixture.Get(second)) == 0);
            fixture.DisposeLease(secondSend);
            return Task.CompletedTask;
        });

        await Check("Session_CompletedRequest_ReplaysAndRejectsChangedMessage", () =>
        {
            using var fixture = new SessionFixture();
            var index = fixture.Create("synthetic-key");
            var requestId = Guid.NewGuid();
            var started = fixture.Begin(index, requestId, "相同文字");
            fixture.Complete(started, new GeminiResult(GeminiOutcome.Success, "已保存回覆", "success"));
            fixture.DisposeLease(started);
            var replay = fixture.Begin(index, requestId, "相同文字");
            var conflict = fixture.Begin(index, requestId, "不同文字");
            Require(fixture.Status(replay) == "Replay");
            Require(fixture.ResultText(replay) == "已保存回覆");
            Require(fixture.Status(conflict) == "RequestIdConflict");
            return Task.CompletedTask;
        });

        await Check("Session_ConcurrentSendIsBusyAndResetWaitsThenClearsHistory", async () =>
        {
            using var fixture = new SessionFixture();
            var index = fixture.Create("synthetic-key");
            var active = fixture.Begin(index, Guid.NewGuid(), "進行中");
            var reset = fixture.ResetAsync(index);
            var next = fixture.Begin(index, Guid.NewGuid(), "下一則");
            Require(fixture.Status(next) == "Busy");
            fixture.DisposeLease(active);
            await reset;
            var afterReset = fixture.Begin(index, Guid.NewGuid(), "新對話");
            Require(fixture.Status(afterReset) == "Started");
            Require(fixture.MessageCount(fixture.History(afterReset)) == 0);
            Require(fixture.ApiKey(afterReset) == "synthetic-key");
            fixture.DisposeLease(afterReset);
        });

        await Check("Session_TwentySuccessfulRoundsLimitAndResetClearsReplay", () =>
        {
            using var fixture = new SessionFixture();
            var index = fixture.Create("synthetic-key");
            var oldRequest = Guid.NewGuid();
            for (var round = 0; round < 20; round++)
            {
                var started = fixture.Begin(index, round == 0 ? oldRequest : Guid.NewGuid(), $"訊息 {round}");
                Require(fixture.Status(started) == "Started");
                fixture.Complete(started, new GeminiResult(GeminiOutcome.Success, $"回覆 {round}", "success"));
                fixture.DisposeLease(started);
            }

            var limit = fixture.Begin(index, Guid.NewGuid(), "第 21 則");
            Require(fixture.Status(limit) == "RoundLimit");
            fixture.ResetAsync(index).GetAwaiter().GetResult();
            var reusedId = fixture.Begin(index, oldRequest, "新對話重用 ID");
            Require(fixture.Status(reusedId) == "Started");
            Require(fixture.MessageCount(fixture.History(reusedId)) == 0);
            fixture.DisposeLease(reusedId);
            return Task.CompletedTask;
        });

        await Check("Session_ExpiresAfterConfiguredSlidingIdlePeriod", async () =>
        {
            using var fixture = new SessionFixture(TimeSpan.FromMilliseconds(100));
            var index = fixture.Create("synthetic-key");
            await Task.Delay(250);
            Require(fixture.Get(index) is null);
        });

        await Check("Session_DeleteWaitsForActiveSendAndRemovesKeyAndHistory", async () =>
        {
            using var fixture = new SessionFixture();
            var index = fixture.Create("synthetic-key");
            var active = fixture.Begin(index, Guid.NewGuid(), "進行中");
            var delete = fixture.DeleteAsync(index);
            Require(!delete.IsCompleted);
            fixture.DisposeLease(active);
            await delete;
            Require(fixture.Get(index) is null);
        });
    }

    private sealed class SessionFixture : IDisposable
    {
        private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
        private readonly object _store;
        private readonly Type _storeType;
        private readonly Type _contract;

        public SessionFixture(TimeSpan? idleTimeout = null)
        {
            var assembly = typeof(ApplicationProgram).Assembly;
            _contract = assembly.GetType("TestAPIKeyCalling.Services.IChatSessionStore")
                ?? throw new InvalidOperationException("Missing chat session store contract");
            _storeType = assembly.GetType("TestAPIKeyCalling.Services.MemoryChatSessionStore")
                ?? throw new InvalidOperationException("Missing chat session store implementation");
            var constructor = _storeType.GetConstructors().Single();
            _store = constructor.Invoke([_cache, idleTimeout ?? TimeSpan.FromMinutes(30)]);
        }

        public string Create(string key) => (string)Invoke("Create", key)!;
        public object? Get(string index) => Invoke("Get", index);
        public object Begin(string index, Guid requestId, string input) => Invoke("TryBeginSend", index, requestId, input)!;
        public Task ResetAsync(string index) => (Task)Invoke("ResetConversationAsync", index, CancellationToken.None)!;
        public Task DeleteAsync(string index) => (Task)Invoke("RemoveAsync", index, CancellationToken.None)!;
        public string Status(object start) => start.GetType().GetProperty("Status")!.GetValue(start)!.ToString()!;
        public object? Result(object start) => start.GetType().GetProperty("Result")!.GetValue(start);
        public string? ResultText(object start) => Result(start)?.GetType().GetProperty("Text")?.GetValue(Result(start)) as string;
        public string ApiKey(object start) => (string)Lease(start).GetType().GetProperty("ApiKey")!.GetValue(Lease(start))!;
        public object History(object start) => Lease(start).GetType().GetProperty("History")!.GetValue(Lease(start))!;
        public void Complete(object start, GeminiResult result) => Lease(start).GetType().GetMethod("Complete")!.Invoke(Lease(start), [result]);
        public void DisposeLease(object start) => (Lease(start) as IDisposable)?.Dispose();
        public int MessageCount(object? source)
        {
            if (source is null)
            {
                return -1;
            }

            var messages = source.GetType().GetProperty("Messages")?.GetValue(source) ?? source;
            return ((IEnumerable)messages).Cast<object>().Count();
        }

        private object Lease(object start) => start.GetType().GetProperty("Lease")!.GetValue(start)!;

        private object? Invoke(string name, params object[] arguments)
        {
            var method = _contract.GetMethod(name);
            Require(method is not null);
            return method!.Invoke(_store, arguments);
        }

        public void Dispose() => _cache.Dispose();
    }
}
