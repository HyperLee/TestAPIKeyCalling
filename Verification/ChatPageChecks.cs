using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Verification;

internal static partial class Program
{
    private static async Task ChatPageTests()
    {
        var handler = new FakeHandler();
        handler.RespondByInput = true;
        var logs = new CapturingProvider();
        await using var app = CreateTestApp(handler, new FakeReader(), logs, "http://127.0.0.1:0");
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        await using var fixture = new ChatPageFixture(address);

        await Check("ChatPage_Get_NoCacheBlankKeyAndNoGeminiCall", async () =>
        {
            using var client = fixture.NewClient();
            using var response = await client.GetAsync("/ApiKeyChat");
            var html = await response.Content.ReadAsStringAsync();
            Require(response.StatusCode == HttpStatusCode.OK);
            Require(response.Headers.CacheControl?.NoStore == true);
            Require(html.Contains("type=\"password\"") && html.Contains("name=\"ApiKey\""));
            Require(!html.Contains(FakeReader.Key) && handler.Calls == 0);
        });

        var invalidKeyCases = new[] { "", "   ", "fake key", "fake\nkey", "fake\tkey", "fake\0key", "fake中文" };
        for (var index = 0; index < invalidKeyCases.Length; index++)
        {
            var invalid = invalidKeyCases[index];
            var caseNumber = index + 1;
            await Check($"ChatPage_Begin_InvalidKeyCase{caseNumber}_NotEchoedOrStored", async () =>
            {
                using var client = fixture.NewClient();
                var before = handler.Calls;
                using var response = await fixture.BeginAsync(client, invalid);
                var body = await response.Content.ReadAsStringAsync();
                if (caseNumber == 6)
                {
                    Require(response.StatusCode == HttpStatusCode.BadRequest && body.Length == 0
                        && handler.Calls == before && !fixture.HasSessionCookie(client));
                    return;
                }

                var decodedBody = WebUtility.HtmlDecode(body);
                if (caseNumber == 6)
                {
                    Console.Error.WriteLine($"NUL-key response: http={(int)response.StatusCode}; safePrompt={decodedBody.Contains("Key 格式", StringComparison.Ordinal) || decodedBody.Contains("請輸入 API Key", StringComparison.Ordinal)}; length={body.Length}");
                }
                Require(response.StatusCode == HttpStatusCode.OK
                    && (decodedBody.Contains("Key 格式", StringComparison.Ordinal) || decodedBody.Contains("請輸入 API Key", StringComparison.Ordinal)));
                Require((string.IsNullOrWhiteSpace(invalid) || !body.Contains(invalid, StringComparison.Ordinal)) && handler.Calls == before);
                Require(!fixture.HasSessionCookie(client));
            });
        }

        await Check("ChatPage_Begin_SetsScopedRandomCookieWithoutReturningKey", async () =>
        {
            using var client = fixture.NewClient();
            using var response = await fixture.BeginAsync(client, FakeReader.Key);
            var body = await response.Content.ReadAsStringAsync();
            var cookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToArray() : [];
            var sessionCookie = cookies.SingleOrDefault(value => value.StartsWith("ApiKeyChat.Session=", StringComparison.Ordinal));
            Require(response.StatusCode == HttpStatusCode.Redirect && sessionCookie is not null);
            Require(sessionCookie!.Contains("path=/ApiKeyChat", StringComparison.OrdinalIgnoreCase));
            Require(sessionCookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
            Require(sessionCookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
            Require(sessionCookie.Split(';')[0].Split('=', 2)[1].Length == 43);
            Require(!sessionCookie.Contains(FakeReader.Key, StringComparison.Ordinal));
            Require(!body.Contains(FakeReader.Key, StringComparison.Ordinal) && handler.Calls == 0);
            using var page = await client.GetAsync("/ApiKeyChat");
            var html = await page.Content.ReadAsStringAsync();
            Require(html.Contains("api-key-chat.js") && html.Contains("新對話") && html.Contains("清除 Key"));
            Require(!html.Contains(FakeReader.Key, StringComparison.Ordinal));
        });

        await Check("ChatPage_Antiforgery_RequiredForEveryPostHandler", async () =>
        {
            using var client = fixture.NewClient();
            var before = handler.Calls;
            using (var begin = await client.PostAsync("/ApiKeyChat?handler=Begin", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["ApiKey"] = FakeReader.Key,
                ["__RequestVerificationToken"] = "invalid"
            })))
            {
                Require(begin.StatusCode == HttpStatusCode.BadRequest);
            }

            Require(!fixture.HasSessionCookie(client) && handler.Calls == before);
            using var validBegin = await fixture.BeginAsync(client, FakeReader.Key);
            foreach (var name in new[] { "Send", "NewChat", "ClearKey" })
            {
                using var invalid = await client.PostAsync($"/ApiKeyChat?handler={name}", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Message"] = "forged",
                    ["RequestId"] = Guid.NewGuid().ToString("D"),
                    ["__RequestVerificationToken"] = "invalid"
                }));
                Require(invalid.StatusCode == HttpStatusCode.BadRequest && fixture.HasSessionCookie(client) && handler.Calls == before);
            }
        });

        await Check("ChatPage_Send_ValidatesLengthAndBlankInput", async () =>
        {
            using var client = fixture.NewClient();
            using var begin = await fixture.BeginAsync(client, FakeReader.Key);
            Require(begin.StatusCode == HttpStatusCode.Redirect);
            foreach (var input in new[] { "", " \r\n", new string('x', 4001), string.Concat(Enumerable.Repeat("😀", 2001)) })
            {
                var before = handler.Calls;
                using var response = await fixture.SendAsync(client, input, Guid.NewGuid());
                var body = await response.Content.ReadAsStringAsync();
                Require(response.StatusCode == HttpStatusCode.BadRequest);
                Require((input.Length == 0 || !body.Contains(input, StringComparison.Ordinal)) && handler.Calls == before);
            }
        });

        await Check("ChatPage_Send_AcceptsExactly4000Utf16Units", async () =>
        {
            using var client = fixture.NewClient();
            using var begin = await fixture.BeginAsync(client, FakeReader.Key);
            var input = new string('x', 4000);
            using var response = await fixture.SendAsync(client, input, Guid.NewGuid());
            Require(response.StatusCode == HttpStatusCode.OK && handler.Bodies.Count > 0);
            using var body = JsonDocument.Parse(handler.Bodies[^1]);
            Require(body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString() == input);
        });

        await Check("ChatPage_Send_MultiTurnRefreshReplayAndRequestIdConflict", async () =>
        {
            using var client = fixture.NewClient();
            using var begin = await fixture.BeginAsync(client, FakeReader.Key);
            var before = handler.Calls;
            var firstId = Guid.NewGuid();
            using var first = await fixture.SendAsync(client, "第一則", firstId);
            var firstBody = await first.Content.ReadAsStringAsync();
            Require(first.StatusCode == HttpStatusCode.OK && ReadJsonString(firstBody, "text") == "模擬回覆\n第二行");
            using var refreshed = await client.GetAsync("/ApiKeyChat");
            var html = await refreshed.Content.ReadAsStringAsync();
            var decodedHtml = WebUtility.HtmlDecode(html);
            Require(decodedHtml.Contains("第一則") && decodedHtml.Contains("模擬回覆") && !html.Contains(FakeReader.Key));

            using var second = await fixture.SendAsync(client, "第二則", Guid.NewGuid());
            Require(second.StatusCode == HttpStatusCode.OK);
            Require(handler.Calls == before + 2 && handler.Headers.All(header => header == FakeReader.Key));
            using (var body = JsonDocument.Parse(handler.Bodies[^1]))
            {
                var contents = body.RootElement.GetProperty("contents");
                Require(contents.GetArrayLength() == 3);
                Require(contents[0].GetProperty("role").GetString() == "user");
                Require(contents[0].GetProperty("parts")[0].GetProperty("text").GetString() == "第一則");
                Require(contents[1].GetProperty("role").GetString() == "model");
                Require(contents[1].GetProperty("parts")[0].GetProperty("text").GetString() == "模擬回覆\n第二行");
                Require(contents[2].GetProperty("parts")[0].GetProperty("text").GetString() == "第二則");
            }
            Require(handler.Bodies.All(body => !body.Contains(FakeReader.Key, StringComparison.Ordinal)));

            var calls = handler.Calls;
            using var replay = await fixture.SendAsync(client, "第一則", firstId);
            Require(replay.StatusCode == HttpStatusCode.OK && handler.Calls == calls);
            using var conflict = await fixture.SendAsync(client, "不同內容", firstId);
            Require(conflict.StatusCode == HttpStatusCode.BadRequest && handler.Calls == calls);
            Require(!logs.Messages.Any(message => message.Contains(FakeReader.Key) || message.Contains("第一則") || message.Contains("模擬回覆")));
        });

        await Check("ChatPage_NewChatClearsHistoryAndRetainsKey", async () =>
        {
            using var client = fixture.NewClient();
            using var begin = await fixture.BeginAsync(client, FakeReader.Key);
            using var first = await fixture.SendAsync(client, "舊對話", Guid.NewGuid());
            using var reset = await fixture.PostHandlerAsync(client, "NewChat", new Dictionary<string, string>());
            Require(reset.StatusCode == HttpStatusCode.Redirect);
            using var page = await client.GetAsync("/ApiKeyChat");
            var html = await page.Content.ReadAsStringAsync();
            Require(!html.Contains("舊對話") && html.Contains("清除 Key") && !html.Contains(FakeReader.Key));
            var before = handler.Calls;
            using var next = await fixture.SendAsync(client, "新對話第一則", Guid.NewGuid());
            Require(next.StatusCode == HttpStatusCode.OK && handler.Calls == before + 1);
            using var body = JsonDocument.Parse(handler.Bodies[^1]);
            Require(body.RootElement.GetProperty("contents").GetArrayLength() == 1);
            Require(body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString() == "新對話第一則");
        });

        await Check("ChatPage_ClearKeyRemovesCookieAndReturnsBlankKeyForm", async () =>
        {
            using var client = fixture.NewClient();
            using var begin = await fixture.BeginAsync(client, FakeReader.Key);
            using var send = await fixture.SendAsync(client, "敏感歷史", Guid.NewGuid());
            using var clear = await fixture.PostHandlerAsync(client, "ClearKey", new Dictionary<string, string>());
            Require(clear.StatusCode == HttpStatusCode.Redirect && !fixture.HasSessionCookie(client));
            using var page = await client.GetAsync("/ApiKeyChat");
            var html = await page.Content.ReadAsStringAsync();
            Require(html.Contains("name=\"ApiKey\"") && !html.Contains("敏感歷史") && !html.Contains(FakeReader.Key));
        });

        await Check("ChatPage_ExpiredSessionReturnsToKeyEntry", async () =>
        {
            using var client = fixture.NewClient();
            client.DefaultRequestHeaders.Add("Cookie", "ApiKeyChat.Session=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
            using var response = await client.GetAsync("/ApiKeyChat");
            var html = await response.Content.ReadAsStringAsync();
            Require(response.StatusCode == HttpStatusCode.OK && html.Contains("name=\"ApiKey\"") && !html.Contains("對話紀錄"));
        });

        await Check("ChatPage_FailureKeepsHistoryUnchangedAndAllowsExplicitRetry", async () =>
        {
            using var client = fixture.NewClient();
            using var begin = await fixture.BeginAsync(client, FakeReader.Key);
            using var success = await fixture.SendAsync(client, "已完成", Guid.NewGuid());
            handler.RespondByInput = false;
            handler.Status = HttpStatusCode.TooManyRequests;
            handler.Response = "synthetic upstream diagnostic";
            using var failed = await fixture.SendAsync(client, "待重試", Guid.NewGuid());
            var failureBody = await failed.Content.ReadAsStringAsync();
            Require(failed.StatusCode == HttpStatusCode.OK && ReadJsonString(failureBody, "message").Contains("配額", StringComparison.Ordinal));
            Require(!failureBody.Contains("synthetic upstream diagnostic", StringComparison.Ordinal));
            using var page = await client.GetAsync("/ApiKeyChat");
            var html = await page.Content.ReadAsStringAsync();
            var decodedHtml = WebUtility.HtmlDecode(html);
            Require(decodedHtml.Contains("已完成") && !decodedHtml.Contains("待重試"));
            handler.Status = HttpStatusCode.OK;
            handler.Response = Reply("STOP", new { text = "模擬回覆\n第二行" });
            using var retry = await fixture.SendAsync(client, "待重試", Guid.NewGuid());
            Require(retry.StatusCode == HttpStatusCode.OK
                && ReadJsonString(await retry.Content.ReadAsStringAsync(), "text") == "模擬回覆\n第二行");
            handler.RespondByInput = true;
        });

        await Check("ChatPage_ScriptLikeResponseIsRenderedAsText", async () =>
        {
            using var client = fixture.NewClient();
            using var begin = await fixture.BeginAsync(client, FakeReader.Key);
            using var response = await fixture.SendAsync(client, "simulate:script", Guid.NewGuid());
            using var page = await client.GetAsync("/ApiKeyChat");
            var html = await page.Content.ReadAsStringAsync();
            Require(response.StatusCode == HttpStatusCode.OK);
            Require(html.Contains("&lt;script&gt;window.secretExecuted=true&lt;/script&gt;") && !html.Contains("<script>window.secretExecuted=true</script>"));
        });

        await Check("ChatPage_TruncatedReplyAndWarningPersistAfterRefresh", async () =>
        {
            using var client = fixture.NewClient();
            using var begin = await fixture.BeginAsync(client, FakeReader.Key);
            using var response = await fixture.SendAsync(client, "simulate:truncated", Guid.NewGuid());
            var body = await response.Content.ReadAsStringAsync();
            Require(response.StatusCode == HttpStatusCode.OK && ReadJsonString(body, "status") == "truncated");
            using var page = await client.GetAsync("/ApiKeyChat");
            var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
            Require(html.Contains("模擬回覆\n第二行") && html.Contains("內容可能不完整"));
        });

        await Check("ChatPage_ConcurrentSendBusyAndNewChatWaits", async () =>
        {
            using var client = fixture.NewClient();
            using var begin = await fixture.BeginAsync(client, FakeReader.Key);
            handler.Delay = TimeSpan.FromMilliseconds(450);
            var before = handler.Calls;
            var active = fixture.SendAsync(client, "進行中的訊息", Guid.NewGuid());
            var startedAt = DateTime.UtcNow;
            while (handler.Calls == before && DateTime.UtcNow - startedAt < TimeSpan.FromSeconds(2))
            {
                await Task.Delay(10);
            }

            Require(handler.Calls == before + 1);
            using var busy = await fixture.SendAsync(client, "不應呼叫", Guid.NewGuid());
            Require(busy.StatusCode == HttpStatusCode.Conflict && handler.Calls == before + 1);
            var reset = fixture.PostHandlerAsync(client, "NewChat", new Dictionary<string, string>());
            await Task.Delay(60);
            Require(!reset.IsCompleted);
            using var completed = await active;
            using var resetResponse = await reset;
            Require(completed.StatusCode == HttpStatusCode.OK && resetResponse.StatusCode == HttpStatusCode.Redirect);
            using var page = await client.GetAsync("/ApiKeyChat");
            var html = await page.Content.ReadAsStringAsync();
            Require(!html.Contains("進行中的訊息") && fixture.HasSessionCookie(client));
            handler.Delay = TimeSpan.Zero;
        });

        await app.StopAsync();
    }

    private static string ReadJsonString(string json, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private sealed class ChatPageFixture(string address) : IAsyncDisposable
    {
        private readonly Dictionary<HttpClient, CookieContainer> _containers = [];

        public HttpClient NewClient()
        {
            var cookies = new CookieContainer();
            var handler = new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false };
            var client = new HttpClient(handler) { BaseAddress = new Uri(address) };
            _containers.Add(client, cookies);
            return client;
        }

        public async Task<HttpResponseMessage> BeginAsync(HttpClient client, string key)
        {
            var token = await GetTokenAsync(client);
            return await client.PostAsync("/ApiKeyChat?handler=Begin", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["ApiKey"] = key,
                ["__RequestVerificationToken"] = token
            }));
        }

        public async Task<HttpResponseMessage> SendAsync(HttpClient client, string input, Guid requestId)
        {
            var token = await GetTokenAsync(client);
            return await client.PostAsync("/ApiKeyChat?handler=Send", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Message"] = input,
                ["RequestId"] = requestId.ToString("D"),
                ["__RequestVerificationToken"] = token
            }));
        }

        public async Task<HttpResponseMessage> PostHandlerAsync(HttpClient client, string handler, Dictionary<string, string> fields)
        {
            var token = await GetTokenAsync(client);
            fields["__RequestVerificationToken"] = token;
            return await client.PostAsync($"/ApiKeyChat?handler={handler}", new FormUrlEncodedContent(fields));
        }

        public bool HasSessionCookie(HttpClient client)
        {
            return _containers[client].GetCookies(new Uri(address + "/ApiKeyChat"))
                .Cast<Cookie>().Any(cookie => cookie.Name == "ApiKeyChat.Session");
        }

        private static async Task<string> GetTokenAsync(HttpClient client)
        {
            using var response = await client.GetAsync("/ApiKeyChat");
            var html = await response.Content.ReadAsStringAsync();
            var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            Require(match.Success);
            return WebUtility.HtmlDecode(match.Groups[1].Value);
        }

        public ValueTask DisposeAsync()
        {
            foreach (var client in _containers.Keys)
            {
                client.Dispose();
            }
            return ValueTask.CompletedTask;
        }
    }
}
