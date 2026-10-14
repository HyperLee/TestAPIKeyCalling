using System.Collections;

using Microsoft.Extensions.Caching.Memory;

using TestAPIKeyCalling.Services;

using ApplicationProgram = TestAPIKeyCalling.Program;

namespace Verification;

internal static partial class Program
{
    private static async Task ChatSessionTests()
    {
        await Check("ChatModels_ExposeThreeAllowedModelsAndRequiredHints", () =>
        {
            var catalog = typeof(ApplicationProgram).Assembly.GetType("TestAPIKeyCalling.Services.GeminiChatModels");
            Require(catalog is not null);
            Require((string?)catalog!.GetField("DefaultModelId")?.GetValue(null) == "gemini-3.8-flash");
            var models = ((IEnumerable)catalog.GetProperty("All")!.GetValue(null)!).Cast<object>().ToArray();
            Require(models.Length == 3);
            Require(models.Select(model => model.GetType().GetProperty("Id")!.GetValue(model)).Cast<string>()
                .SequenceEqual(["gemini-3.8-flash", "gemini-3.5-flash-lite", "gemini-2.5-flash"]));
            Require(models.Select(model => (string)model.GetType().GetProperty("Hint")!.GetValue(model)!).Any(hint => hint.Contains("建議", StringComparison.Ordinal)));
            Require(models.Select(model => (string)model.GetType().GetProperty("Hint")!.GetValue(model)!).Any(hint => hint.Contains("成本", StringComparison.Ordinal)));
            Require(models.Select(model => (string)model.GetType().GetProperty("Hint")!.GetValue(model)!).Any(hint => hint.Contains("受限", StringComparison.Ordinal)));
            var isAllowed = catalog.GetMethod("IsAllowed")!;
            Require(models.All(model => (bool)isAllowed.Invoke(null, [model.GetType().GetProperty("Id")!.GetValue(model)])!));
            Require(!(bool)isAllowed.Invoke(null, ["gemini-3.8-flash:generateContent"])!);
            return Task.CompletedTask;
        });

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

        await Check("Session_StoresSelectedModelInSnapshotAndSendLease", () =>
        {
            using var fixture = new SessionFixture();
            var index = fixture.Create("synthetic-key", "gemini-3.5-flash-lite");
            Require(fixture.ModelId(fixture.Get(index)) == "gemini-3.5-flash-lite");
            var started = fixture.Begin(index, Guid.NewGuid(), "使用選定模型");
            Require(fixture.Status(started) == "Started" && fixture.ModelId(started) == "gemini-3.5-flash-lite");
            fixture.DisposeLease(started);
            return Task.CompletedTask;
        });

        await Check("Session_CreateWithoutModelUsesDefault", () =>
        {
            using var fixture = new SessionFixture();
            var index = fixture.Create("synthetic-key");
            Require(fixture.ModelId(fixture.Get(index)) == "gemini-3.8-flash");
            return Task.CompletedTask;
        });

        await Check("Session_ChangingModelClearsConversationAndDedupButRetainsKey", async () =>
        {
            using var fixture = new SessionFixture();
            var index = fixture.Create("synthetic-key", "gemini-3.8-flash");
            var requestId = Guid.NewGuid();
            var started = fixture.Begin(index, requestId, "舊對話");
            fixture.Complete(started, new GeminiResult(GeminiOutcome.Truncated, "舊回覆", "截斷"));
            fixture.DisposeLease(started);
            Require(fixture.LastResponseTruncated(fixture.Get(index)));

            Require(await fixture.ChangeModelAsync(index, "gemini-2.5-flash") == "Changed");
            var snapshot = fixture.Get(index);
            Require(fixture.ModelId(snapshot) == "gemini-2.5-flash");
            Require(fixture.MessageCount(snapshot) == 0 && fixture.LastResponseTruncated(snapshot) == false);
            var reusedId = fixture.Begin(index, requestId, "新對話");
            Require(fixture.Status(reusedId) == "Started" && fixture.ApiKey(reusedId) == "synthetic-key");
            Require(fixture.ModelId(reusedId) == "gemini-2.5-flash" && fixture.MessageCount(fixture.History(reusedId)) == 0);
            fixture.DisposeLease(reusedId);
        });

        await Check("Session_ChangingToCurrentModelPreservesHistoryAndReplay", async () =>
        {
            using var fixture = new SessionFixture();
            var index = fixture.Create("synthetic-key", "gemini-3.8-flash");
            var requestId = Guid.NewGuid();
            var started = fixture.Begin(index, requestId, "保留的對話");
            fixture.Complete(started, new GeminiResult(GeminiOutcome.Success, "保留的回覆", "成功"));
            fixture.DisposeLease(started);

            Require(await fixture.ChangeModelAsync(index, "gemini-3.8-flash") == "Unchanged");
            Require(fixture.MessageCount(fixture.Get(index)) == 2);
            var replay = fixture.Begin(index, requestId, "保留的對話");
            Require(fixture.Status(replay) == "Replay" && fixture.ResultText(replay) == "保留的回覆");
        });

        await Check("Session_ModelChangeWaitsForSendAndBlocksInterleaving", async () =>
        {
            using var fixture = new SessionFixture();
            var index = fixture.Create("synthetic-key", "gemini-3.8-flash");
            var active = fixture.Begin(index, Guid.NewGuid(), "切換前的訊息");
            var change = fixture.ChangeModelAsync(index, "gemini-3.5-flash-lite");
            var overlapping = fixture.Begin(index, Guid.NewGuid(), "不得交錯");
            Require(!change.IsCompleted && fixture.Status(overlapping) == "Busy");
            fixture.Complete(active, new GeminiResult(GeminiOutcome.Success, "切換前的回覆", "成功"));
            fixture.DisposeLease(active);
            Require(await change == "Changed");
            var next = fixture.Begin(index, Guid.NewGuid(), "切換後的訊息");
            Require(fixture.Status(next) == "Started" && fixture.ModelId(next) == "gemini-3.5-flash-lite");
            fixture.DisposeLease(next);
        });

        await Check("Session_InvalidModelChangeLeavesStateUnchanged", async () =>
        {
            using var fixture = new SessionFixture();
            var index = fixture.Create("synthetic-key", "gemini-3.8-flash");
            var started = fixture.Begin(index, Guid.NewGuid(), "原有對話");
            fixture.Complete(started, new GeminiResult(GeminiOutcome.Success, "原有回覆", "成功"));
            fixture.DisposeLease(started);
            Require(await fixture.ChangeModelAsync(index, "other-model") == "InvalidModel");
            Require(fixture.ModelId(fixture.Get(index)) == "gemini-3.8-flash" && fixture.MessageCount(fixture.Get(index)) == 2);
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
        public string Create(string key, string model) => (string)Invoke("Create", key, model)!;
        public object? Get(string index) => Invoke("Get", index);
        public object Begin(string index, Guid requestId, string input) => Invoke("TryBeginSend", index, requestId, input)!;
        public Task ResetAsync(string index) => (Task)Invoke("ResetConversationAsync", index, CancellationToken.None)!;
        public Task DeleteAsync(string index) => (Task)Invoke("RemoveAsync", index, CancellationToken.None)!;
        public string Status(object start) => start.GetType().GetProperty("Status")!.GetValue(start)!.ToString()!;
        public object? Result(object start) => start.GetType().GetProperty("Result")!.GetValue(start);
        public string? ResultText(object start) => Result(start)?.GetType().GetProperty("Text")?.GetValue(Result(start)) as string;
        public string ApiKey(object start) => (string)Lease(start).GetType().GetProperty("ApiKey")!.GetValue(Lease(start))!;
        public string? ModelId(object? source)
        {
            var lease = source?.GetType().GetProperty("Lease")?.GetValue(source);
            return (string?)(lease ?? source)?.GetType().GetProperty("ModelId")?.GetValue(lease ?? source);
        }
        public bool LastResponseTruncated(object? snapshot) => (bool?)snapshot?.GetType().GetProperty("LastResponseTruncated")?.GetValue(snapshot) == true;
        public object History(object start) => Lease(start).GetType().GetProperty("History")!.GetValue(Lease(start))!;
        public async Task<string> ChangeModelAsync(string index, string model)
        {
            var task = (Task)Invoke("ChangeModelAsync", index, model, CancellationToken.None)!;
            await task;
            return task.GetType().GetProperty("Result")?.GetValue(task)?.ToString() ?? "";
        }
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
            var method = _contract.GetMethods().SingleOrDefault(candidate => candidate.Name == name
                && candidate.GetParameters().Length == arguments.Length);
            Require(method is not null);
            return method!.Invoke(_store, arguments);
        }

        public void Dispose() => _cache.Dispose();
    }
}