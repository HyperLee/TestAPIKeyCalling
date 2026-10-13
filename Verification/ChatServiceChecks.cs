using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using TestAPIKeyCalling.Services;

using ApplicationProgram = TestAPIKeyCalling.Program;

namespace Verification;

internal static partial class Program
{
    private static async Task ChatContractTests()
    {
        await Check("Chat_ServiceContract_IsRegisteredInApplicationAssembly", () =>
        {
            var contract = typeof(ApplicationProgram).Assembly.GetType("TestAPIKeyCalling.Services.IGeminiChatService");
            Require(contract is not null && contract.IsInterface);
            return Task.CompletedTask;
        });

        await Check("Chat_MultiTurn_UsesOrderedHistoryAndKeyHeader", async () =>
        {
            using var fixture = new ChatFixture();
            var result = await fixture.GenerateAsync("synthetic-chat-key", [
                ("user", "第一則"),
                ("model", "第一則回覆"),
                ("user", "第二則")
            ]);

            Require(result.Outcome == GeminiOutcome.Success);
            Require(fixture.Handler.Url == "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent");
            Require(fixture.Handler.Header == "synthetic-chat-key");
            using var body = JsonDocument.Parse(fixture.Handler.Body);
            var contents = body.RootElement.GetProperty("contents");
            Require(contents.GetArrayLength() == 3);
            Require(contents[0].GetProperty("role").GetString() == "user");
            Require(contents[0].GetProperty("parts")[0].GetProperty("text").GetString() == "第一則");
            Require(contents[1].GetProperty("role").GetString() == "model");
            Require(contents[1].GetProperty("parts")[0].GetProperty("text").GetString() == "第一則回覆");
            Require(contents[2].GetProperty("role").GetString() == "user");
            Require(contents[2].GetProperty("parts")[0].GetProperty("text").GetString() == "第二則");
            Require(!fixture.Handler.Body.Contains("synthetic-chat-key", StringComparison.Ordinal));
        });

        await Check("Chat_ContextLengthError_HasSafeClassification", async () =>
        {
            using var fixture = new ChatFixture();
            fixture.Handler.Status = System.Net.HttpStatusCode.BadRequest;
            fixture.Handler.Response = "{\"error\":{\"message\":\"The input exceeds the context window limit\"}}";
            var result = await fixture.GenerateAsync("synthetic-chat-key", [("user", "訊息")]);
            var contextLength = Enum.Parse<GeminiError>("ContextLength");
            Require(result.Error == contextLength);
            Require(!result.Message.Contains("context window limit", StringComparison.OrdinalIgnoreCase));
        });
    }

    private sealed class ChatFixture : IDisposable
    {
        private readonly ServiceProvider _services;
        public FakeHandler Handler { get; } = new();

        public ChatFixture()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            ApplicationProgram.ConfigureServices(services, new ConfigurationBuilder().Build());
            services.AddHttpClient("Gemini")
                .ConfigurePrimaryHttpMessageHandler(() => Handler)
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
            _services = services.BuildServiceProvider();
        }

        public async Task<GeminiResult> GenerateAsync(string key, (string Role, string Text)[] messages)
        {
            var assembly = typeof(ApplicationProgram).Assembly;
            var messageType = assembly.GetType("TestAPIKeyCalling.Services.GeminiChatMessage");
            Require(messageType is not null);
            var typedMessages = Array.CreateInstance(messageType!, messages.Length);
            for (var index = 0; index < messages.Length; index++)
            {
                typedMessages.SetValue(Activator.CreateInstance(messageType!, messages[index].Role, messages[index].Text), index);
            }

            var contract = assembly.GetType("TestAPIKeyCalling.Services.IGeminiChatService");
            Require(contract is not null);
            var service = _services.GetRequiredService(contract!);
            var method = contract!.GetMethod("GenerateAsync");
            Require(method is not null);
            var task = method!.Invoke(service, [key, typedMessages, CancellationToken.None]) as Task<GeminiResult>;
            Require(task is not null);
            return await task!;
        }

        public void Dispose() => _services.Dispose();
    }
}
