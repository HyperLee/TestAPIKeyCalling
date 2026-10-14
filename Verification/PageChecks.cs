using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using TestAPIKeyCalling.Services;

using ApplicationProgram = TestAPIKeyCalling.Program;

namespace Verification;

internal static partial class Program
{
    private static WebApplication CreateTestApp(FakeHandler handler, FakeReader reader, CapturingProvider logs, string address)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(ApplicationProgram).Assembly.GetName().Name,
            ContentRootPath = root,
            EnvironmentName = "Development"
        });
        builder.WebHost.UseUrls(address);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        ApplicationProgram.ConfigureServices(builder.Services, builder.Configuration);
        builder.Services.AddSingleton<IApiKeyReader>(reader);
        builder.Services.AddHttpClient("Gemini")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        var app = builder.Build();
        ApplicationProgram.ConfigurePipeline(app);
        return app;
    }

    private static string Textarea(string html, string id)
    {
        var match = Regex.Match(html, $"<textarea[^>]*id=\"{id}\"[^>]*>([\\s\\S]*?)</textarea>");
        Require(match.Success);
        // HTML normalizes CRLF and ignores exactly one initial LF in a textarea.
        var value = WebUtility.HtmlDecode(match.Groups[1].Value).Replace("\r\n", "\n");
        return value.StartsWith('\n') ? value[1..] : value;
    }

    private static async Task PageTests()
    {
        var handler = new FakeHandler();
        var reader = new FakeReader();
        var logs = new CapturingProvider();
        await using var app = CreateTestApp(handler, reader, logs, "http://127.0.0.1:0");
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };

        async Task<string> Post(string input, Dictionary<string, string>? extras = null)
        {
            var page = await client.GetStringAsync("/ApiKeyTest");
            var match = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            Require(match.Success);
            var fields = extras ?? [];
            fields["Input"] = input;
            fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value);
            using var response = await client.PostAsync("/ApiKeyTest", new FormUrlEncodedContent(fields));
            Require(response.StatusCode == HttpStatusCode.OK);
            return await response.Content.ReadAsStringAsync();
        }

        await Check("Page_HomeNavigationAndExistingLinks", async () =>
        {
            var html = await client.GetStringAsync("/");
            Require(html.Contains("href=\"/ApiKeyTest\"") && WebUtility.HtmlDecode(html).Contains("API Key 測試頁面"));
            Require(html.Contains("href=\"/Privacy\""));
        });
        await Check("Page_Get_EmptyReadonlyNoRemoteCall", async () =>
        {
            var html = await client.GetStringAsync("/ApiKeyTest");
            Require(Textarea(html, "Input") == "" && Textarea(html, "Result") == "");
            Require(Regex.IsMatch(html, "<textarea[^>]*readonly[^>]*id=\"Result\"|<textarea[^>]*id=\"Result\"[^>]*readonly"));
            Require(handler.Calls == 0 && !html.Contains(FakeReader.Key));
            Require(html.Contains("name=\"__RequestVerificationToken\""));
        });
        foreach (var input in new[] { "", " \n ", new string('a', 4001), string.Concat(Enumerable.Repeat("😀", 2001)) })
        {
            await Check("Page_InvalidInput_RejectBeforeKeyRead", async () =>
            {
                var calls = handler.Calls;
                var reads = reader.Calls;
                var html = await Post(input);
                Require(handler.Calls == calls && reader.Calls == reads && Textarea(html, "Result") == "");
                Require(WebUtility.HtmlDecode(html).Contains(InputValidation.GetError(input)!));
            });
        }
        await Check("Page_Valid4000EmojiUnits_Accepted", async () =>
        {
            var input = string.Concat(Enumerable.Repeat("😀", 2000));
            var html = await Post(input);
            Require(Textarea(html, "Input") == input && Textarea(html, "Result") == "模擬回覆");
        });
        await Check("Page_ScriptResponse_EncodedAndReadonly", async () =>
        {
            const string output = "</textarea><script>window.secretExecuted=true</script>\n<div>純文字</div>";
            handler.Response = Reply("STOP", new { text = output });
            var html = await Post("  繁體中文\n\"原樣\"  ");
            Require(Textarea(html, "Result") == output && !html.Contains("<script>window.secretExecuted"));
            Require(Textarea(html, "Input") == "  繁體中文\n\"原樣\"  ");
            Require(!html.Contains(FakeReader.Key));
        });
        await Check("Page_ResponseLeadingNewlines_Preserved", async () =>
        {
            const string output = "\n\n第一行\n第二行";
            handler.Response = Reply("STOP", new { text = output });
            var html = await Post("test");
            Require(Textarea(html, "Result") == output);
            handler.Response = Reply("STOP", new { text = "模擬回覆" });
        });
        await Check("Page_SuccessThenFailure_NoStaleResult", async () =>
        {
            handler.Status = HttpStatusCode.TooManyRequests;
            handler.Response = "raw upstream diagnostic";
            var html = await Post("new input");
            Require(Textarea(html, "Result") == "" && Textarea(html, "Input") == "new input");
            Require(WebUtility.HtmlDecode(html).Contains("配額限制") && !html.Contains("raw upstream"));
            handler.Status = HttpStatusCode.OK;
            handler.Response = Reply("STOP", new { text = "模擬回覆" });
        });
        await Check("Page_Truncation_TextAndWarning", async () =>
        {
            handler.Response = Reply("MAX_TOKENS", new { text = "partial" });
            var html = await Post("test");
            Require(Textarea(html, "Result") == "partial" && WebUtility.HtmlDecode(html).Contains("內容可能不完整"));
            handler.Response = Reply("STOP", new { text = "模擬回覆" });
        });
        await Check("Page_KeyConfiguration_NoRequestSafeMessage", async () =>
        {
            reader.Fail = true;
            var calls = handler.Calls;
            var html = await Post("test");
            Require(handler.Calls == calls && Textarea(html, "Result") == "" && WebUtility.HtmlDecode(html).Contains("Key 設定錯誤"));
            reader.Fail = false;
        });
        foreach (var token in new[] { "", "invalid" })
        {
            await Check("Page_MissingOrInvalidAntiforgery_Rejected", async () =>
            {
                var calls = handler.Calls;
                using var response = await client.PostAsync("/ApiKeyTest", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Input"] = "test",
                    ["__RequestVerificationToken"] = token
                }));
                Require(response.StatusCode == HttpStatusCode.BadRequest && handler.Calls == calls);
            });
        }
        await Check("Page_ForgedClientKeyModel_Ignored", async () =>
        {
            await Post("only current input", new Dictionary<string, string> { ["Key"] = "forged", ["Model"] = "forged" });
            Require(handler.Header == FakeReader.Key && handler.Url.EndsWith("/gemini-2.5-flash:generateContent"));
            using var body = JsonDocument.Parse(handler.Body);
            Require(body.RootElement.GetProperty("contents").GetArrayLength() == 1);
        });
        await Check("Page_KeyFileAndVerification_NotServed", async () =>
        {
            foreach (var path in new[] { "/gemini-api-key.txt", "/Verification/Program.cs" })
            {
                using var response = await client.GetAsync(path);
                Require(response.StatusCode == HttpStatusCode.NotFound);
            }
        });
        await Check("Host_HttpClientTimeoutAndSafeTraceLogging", () =>
        {
            var factory = app.Services.GetRequiredService<IHttpClientFactory>();
            using var http = factory.CreateClient("Gemini");
            Require(http.Timeout == TimeSpan.FromSeconds(60));
            Require(!logs.Messages.Any(message => message.Contains(FakeReader.Key) || message.Contains("raw upstream diagnostic")));
            return Task.CompletedTask;
        });
        await app.StopAsync();
    }

    private static async Task ServeAsync()
    {
        var handler = new FakeHandler { Delay = TimeSpan.FromSeconds(2) };
        handler.RespondByInput = true;
        await using var app = CreateTestApp(handler, new FakeReader(), new CapturingProvider(), "http://127.0.0.1:5288");
        Console.WriteLine("Browser verification host: http://127.0.0.1:5288 (synthetic credentials; no external requests).");
        await app.RunAsync();
    }

    private sealed class CapturingProvider : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();
        public IEnumerable<string> Messages => _messages;
        public ILogger CreateLogger(string categoryName) => new Capture(_messages);
        public void Dispose() { }

        private sealed class Capture(System.Collections.Concurrent.ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                messages.Enqueue(formatter(state, exception));
            }
        }
    }
}