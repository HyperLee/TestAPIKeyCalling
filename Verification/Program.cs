using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

using TestAPIKeyCalling.Services;

namespace Verification;

internal static partial class Program
{
    private static int _passed;
    private static int _failed;

    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--serve"))
        {
            await ServeAsync();
            return 0;
        }

        await KeyTests();
        await ServiceTests();
        await PageTests();
        Console.WriteLine($"Summary: {_passed}/{_passed + _failed} checks passed.");
        return _failed == 0 ? 0 : 1;
    }

    private static async Task Check(string name, Func<Task> test)
    {
        try
        {
            await test();
            _passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception)
        {
            _failed++;
            // Never print exception messages: an assertion may involve a secret.
            Console.WriteLine($"FAIL {name}");
        }
    }

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Assertion failed");
        }
    }

    private static async Task KeyTests()
    {
        var directory = Directory.CreateTempSubdirectory("gemini-key-check-");
        var path = Path.Combine(directory.FullName, FileApiKeyReader.FileName);
        var reader = new FileApiKeyReader(new TestEnvironment(directory.FullName));
        await Check("Key_Missing_SafeConfigurationError", async () =>
        {
            try
            {
                await reader.ReadAsync(default);
                Require(false);
            }
            catch (KeyConfigurationException e)
            {
                Require(e.Message.Contains(FileApiKeyReader.FileName) && !e.Message.Contains(directory.FullName));
                Require(!File.Exists(path));
            }
        });
        foreach (var invalid in new[] { "", "\n# comment\n", "YOUR_GEMINI_API_KEY", "fake-one\nfake-two", "fake\tkey", "fake\0key", "fake\u007fkey", "fake中文" })
        {
            await Check("Key_InvalidContent_Rejected", async () =>
            {
                await File.WriteAllTextAsync(path, invalid);
                try
                {
                    await reader.ReadAsync(default);
                    Require(false);
                }
                catch (KeyConfigurationException e)
                {
                    Require(!e.Message.Contains(directory.FullName));
                    Require(invalid.Length == 0 || invalid == "YOUR_GEMINI_API_KEY" || !e.Message.Contains(invalid));
                }
            });
        }
        foreach (var newline in new[] { "\n", "\r\n" })
        {
            foreach (var bom in new[] { false, true })
            {
                await Check("Key_BomNewlinesComments_OneValue", async () =>
                {
                    await File.WriteAllTextAsync(path, $"# comment{newline}{newline}  synthetic-key-A  {newline}", new UTF8Encoding(bom));
                    Require(await reader.ReadAsync(default) == "synthetic-key-A");
                });
            }
        }
        await Check("Key_Changed_RereadWithoutRestart", async () =>
        {
            await File.WriteAllTextAsync(path, "synthetic-key-A");
            Require(await reader.ReadAsync(default) == "synthetic-key-A");
            await File.WriteAllTextAsync(path, "synthetic-key-B");
            Require(await reader.ReadAsync(default) == "synthetic-key-B");
        });
        await Check("Key_InvalidUtf8_Rejected", async () =>
        {
            await File.WriteAllBytesAsync(path, [0xff, 0xfe, 0x41]);
            try
            {
                await reader.ReadAsync(default);
                Require(false);
            }
            catch (KeyConfigurationException)
            {
            }
        });
        await Check("Key_Unreadable_SafeError", async () =>
        {
            using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            try
            {
                await reader.ReadAsync(default);
                Require(false);
            }
            catch (KeyConfigurationException e)
            {
                Require(!e.Message.Contains(directory.FullName));
            }
        });
        await Check("Key_Cancelled_PropagatesCancellation", async () =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                await reader.ReadAsync(cancellation.Token);
                Require(false);
            }
            catch (OperationCanceledException)
            {
            }
        });
        File.Delete(path);
        // Only the specifically named test file is deleted; no recursive cleanup.
    }

    private static string Reply(string reason = "STOP", params object[] parts)
    {
        return JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = reason, content = new { parts } } } });
    }

    private static async Task ServiceTests()
    {
        foreach (var input in new[] { "", " \r\n", new string('a', 4001), string.Concat(Enumerable.Repeat("😀", 2001)) })
        {
            await Check("Input_Invalid_NoHttpOrKeyRead", async () =>
            {
                using var fixture = new Fixture();
                var result = await fixture.Service.GenerateAsync(input, default);
                Require(result.Error == GeminiError.Input && fixture.Handler.Calls == 0 && fixture.Reader.Calls == 0);
            });
        }
        foreach (var input in new[] { new string('a', 4000), string.Concat(Enumerable.Repeat("😀", 2000)), "  繁體中文\n\"引號\"\\  " })
        {
            await Check("Input_ValidBoundaryUnicode_SentUnchanged", async () =>
            {
                using var fixture = new Fixture();
                var result = await fixture.Service.GenerateAsync(input, default);
                Require(result.Outcome == GeminiOutcome.Success && fixture.Handler.Calls == 1);
                using var document = JsonDocument.Parse(fixture.Handler.Body);
                var root = document.RootElement;
                Require(root.EnumerateObject().Count() == 1);
                var contents = root.GetProperty("contents");
                Require(contents.GetArrayLength() == 1 && contents[0].GetProperty("role").GetString() == "user");
                Require(contents[0].GetProperty("parts")[0].GetProperty("text").GetString() == input);
                Require(fixture.Handler.Url == "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent");
                Require(fixture.Handler.Header == FakeReader.Key && fixture.Handler.Method == HttpMethod.Post);
                Require(fixture.Handler.ContentType == "application/json");
                Require(!fixture.Handler.Body.Contains(FakeReader.Key));
            });
        }
        foreach (var model in new[] { "", " ", "models/gemini-2.5-flash", "https://example.test", "bad/name", "bad?key=x", "bad\nname", "中文", ".", ".." })
        {
            await Check("Model_Invalid_NoHttp", async () =>
            {
                using var fixture = new Fixture(model);
                var result = await fixture.Service.GenerateAsync("test", default);
                Require(result.Error == GeminiError.Configuration && fixture.Handler.Calls == 0);
            });
        }
        await Check("Model_CustomIdentifier_UsedWithoutFallback", async () =>
        {
            using var fixture = new Fixture("custom-model-1.0");
            await fixture.Service.GenerateAsync("test", default);
            Require(fixture.Handler.Url.EndsWith("/custom-model-1.0:generateContent"));
        });
        await Check("Key_ConfigurationFailure_NoHttp", async () =>
        {
            using var fixture = new Fixture();
            fixture.Reader.Fail = true;
            var result = await fixture.Service.GenerateAsync("test", default);
            Require(result.Error == GeminiError.Configuration && fixture.Handler.Calls == 0);
        });
        foreach (var pair in new[]
        {
            (400, GeminiError.Unexpected), (401, GeminiError.Authentication), (403, GeminiError.Authentication),
            (404, GeminiError.ModelNotFound), (429, GeminiError.Quota), (500, GeminiError.RemoteService),
            (503, GeminiError.RemoteService), (418, GeminiError.Unexpected), (302, GeminiError.Unexpected)
        })
        {
            await Check($"Http_{pair.Item1}_SafeClassificationNoRetry", async () =>
            {
                using var fixture = new Fixture();
                fixture.Handler.Status = (HttpStatusCode)pair.Item1;
                fixture.Handler.Response = FakeReader.Key + " raw body";
                var result = await fixture.Service.GenerateAsync("test", default);
                Require(result.Error == pair.Item2 && result.Text == "" && fixture.Handler.Calls == 1);
                Require(!result.Message.Contains(FakeReader.Key));
                Require(!fixture.Logger.Messages.Any(x => x.Contains(FakeReader.Key) || x.Contains("raw body")));
            });
        }
        await Check("Http_400StructuredInvalidKey_Authentication", async () =>
        {
            using var fixture = new Fixture();
            fixture.Handler.Status = HttpStatusCode.BadRequest;
            fixture.Handler.Response = "{\"error\":{\"details\":[{\"@type\":\"type.googleapis.com/google.rpc.ErrorInfo\",\"reason\":\"API_KEY_INVALID\"}]}}";
            Require((await fixture.Service.GenerateAsync("test", default)).Error == GeminiError.Authentication);
        });
        foreach (var pair in new[]
        {
            ("{", GeminiError.InvalidResponse), ("[]", GeminiError.InvalidResponse), ("null", GeminiError.InvalidResponse),
            ("{}", GeminiError.NoText), ("{\"candidates\":[]}", GeminiError.NoText),
            (Reply("STOP", new { text = "secret thought", thought = true }), GeminiError.NoText),
            (Reply("STOP", new { inlineData = new { mimeType = "image/png", data = "" } }), GeminiError.NoText),
            (Reply("STOP", new { text = " \r\n" }), GeminiError.NoText),
            (Reply("SAFETY", new { text = "blocked text" }), GeminiError.Blocked),
            (Reply("RECITATION", new { text = "blocked text" }), GeminiError.Blocked),
            (Reply("OTHER", new { text = "abnormal text" }), GeminiError.InvalidResponse),
            (Reply("FINISH_REASON_UNSPECIFIED", new { text = "text" }), GeminiError.InvalidResponse),
            (Reply("MAX_TOKENS", new { text = "" }), GeminiError.NoText),
            ("{\"promptFeedback\":{\"blockReason\":\"SAFETY\"},\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"blocked\"}]}}]}", GeminiError.Blocked),
            ("{\"candidates\":\"bad\"}", GeminiError.InvalidResponse),
            (Reply("STOP", new { text = 42 }), GeminiError.InvalidResponse)
        })
        {
            await Check("Response_NonSuccess_NoTextOrInternalDetails", async () =>
            {
                using var fixture = new Fixture();
                fixture.Handler.Response = pair.Item1;
                var result = await fixture.Service.GenerateAsync("private input", default);
                Require(result.Error == pair.Item2 && result.Text == "" && result.Outcome == GeminiOutcome.Error);
                Require(!fixture.Logger.Messages.Any(x => x.Contains("private input") || x.Contains("blocked text")));
            });
        }
        foreach (var reason in new[] { "STOP", "MAX_TOKENS" })
        {
            await Check("Response_Parts_OrderedTextWithoutThought", async () =>
            {
                using var fixture = new Fixture();
                fixture.Handler.Response = Reply(reason, new { text = "第一行\n" }, new { text = "thought", thought = true }, new { text = "第二行" });
                var result = await fixture.Service.GenerateAsync("test", default);
                Require(result.Text == "第一行\n第二行");
                Require(result.Outcome == (reason == "STOP" ? GeminiOutcome.Success : GeminiOutcome.Truncated));
            });
        }
        await Check("Response_FirstCandidateOnly", async () =>
        {
            using var fixture = new Fixture();
            fixture.Handler.Response = "{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"first\"}]}},{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"second\"}]}}]}";
            Require((await fixture.Service.GenerateAsync("test", default)).Text == "first");
        });
        await Check("Response_EchoedKey_NotExposed", async () =>
        {
            using var fixture = new Fixture();
            fixture.Handler.Response = Reply("STOP", new { text = FakeReader.Key });
            var result = await fixture.Service.GenerateAsync("test", default);
            Require(result.Text == "" && !result.Message.Contains(FakeReader.Key));
        });
        await Check("Network_Exception_SafeNetworkError", async () =>
        {
            using var fixture = new Fixture();
            fixture.Handler.Failure = new HttpRequestException(FakeReader.Key);
            Require((await fixture.Service.GenerateAsync("test", default)).Error == GeminiError.Network);
            Require(!fixture.Logger.Messages.Any(x => x.Contains(FakeReader.Key)));
        });
        await Check("Timeout_ClassifiedWithoutRetry", async () =>
        {
            using var fixture = new Fixture();
            fixture.Client.Timeout = TimeSpan.FromMilliseconds(40);
            fixture.Handler.Delay = TimeSpan.FromSeconds(5);
            var result = await fixture.Service.GenerateAsync("test", default);
            Require(result.Error == GeminiError.Timeout && fixture.Handler.Calls == 1);
        });
        await Check("Cancellation_NotReportedAsTimeout", async () =>
        {
            using var fixture = new Fixture();
            fixture.Handler.Delay = TimeSpan.FromSeconds(5);
            using var cancellation = new CancellationTokenSource(40);
            try
            {
                await fixture.Service.GenerateAsync("test", cancellation.Token);
                Require(false);
            }
            catch (OperationCanceledException)
            {
                Require(fixture.Logger.Messages.Count == 0);
            }
        });
        await Check("Requests_IndependentAndKeyReread", async () =>
        {
            using var fixture = new Fixture();
            await fixture.Service.GenerateAsync("first", default);
            await fixture.Service.GenerateAsync("second", default);
            using var document = JsonDocument.Parse(fixture.Handler.Body);
            Require(document.RootElement.GetProperty("contents").GetArrayLength() == 1);
            Require(document.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString() == "second");
            Require(fixture.Reader.Calls == 2 && fixture.Handler.Calls == 2);
        });
    }

    private sealed class Fixture : IDisposable
    {
        public FakeHandler Handler { get; } = new();
        public FakeReader Reader { get; } = new();
        public CapturingLogger Logger { get; } = new();
        public HttpClient Client { get; }
        public GeminiService Service { get; }

        public Fixture(string? model = null)
        {
            Client = new HttpClient(Handler) { Timeout = TimeSpan.FromSeconds(60) };
            Service = new GeminiService(Client, Reader, new GeminiSettings(model), Logger);
        }

        public void Dispose() => Client.Dispose();
    }

    internal sealed class FakeReader : IApiKeyReader
    {
        public const string Key = "synthetic-secret-never-a-real-key";
        public int Calls { get; private set; }
        public bool Fail { get; set; }

        public Task<string> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (Fail)
            {
                throw new KeyConfigurationException("本機 Key 設定錯誤");
            }
            return Task.FromResult(Key);
        }
    }

    internal sealed class FakeHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string Url { get; private set; } = "";
        public string Body { get; private set; } = "";
        public string Header { get; private set; } = "";
        public string? ContentType { get; private set; }
        public HttpMethod? Method { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string Response { get; set; } = Reply("STOP", new { text = "模擬回覆" });
        public Exception? Failure { get; set; }
        public TimeSpan Delay { get; set; }
        public bool RespondByInput { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Url = request.RequestUri!.AbsoluteUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Header = request.Headers.GetValues("x-goog-api-key").Single();
            ContentType = request.Content.Headers.ContentType?.MediaType;
            Method = request.Method;
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }
            if (Failure is not null)
            {
                throw Failure;
            }
            if (RespondByInput)
            {
                using var body = JsonDocument.Parse(Body);
                var input = body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString();
                Status = input == "simulate:quota" ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK;
                Response = input == "simulate:leading-newline"
                    ? Reply("STOP", new { text = "\n\n第一行\n第二行" })
                    : input == "simulate:script"
                    ? Reply("STOP", new { text = "</textarea><script>window.secretExecuted=true</script>\n純文字" })
                    : Reply(input == "simulate:truncated" ? "MAX_TOKENS" : "STOP", new { text = "模擬回覆\n第二行" });
            }
            return new HttpResponseMessage(Status) { Content = new StringContent(Response, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class CapturingLogger : ILogger<GeminiService>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Require(exception is null);
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Verification";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}