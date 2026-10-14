using System.Diagnostics;

namespace TestAPIKeyCalling.Services;

public sealed class GeminiService(GeminiApiClient apiClient, IApiKeyReader keyReader, ILogger<GeminiService> logger) : IGeminiService
{
    public async Task<GeminiResult> GenerateAsync(string input, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        if (InputValidation.GetError(input) is { } inputError)
        {
            return Failure(GeminiError.Input, inputError);
        }

        if (!apiClient.HasValidModel)
        {
            return Failure(GeminiError.Configuration, "Gemini:Model 設定錯誤，請填入單一模型識別字，不可包含 URL 或 models/ 前綴，修改後重新啟動。");
        }

        try
        {
            var key = await keyReader.ReadAsync(cancellationToken);
            var result = await apiClient.GenerateAsync(key, [new GeminiChatMessage("user", input)], cancellationToken);
            if (result.Error == GeminiError.ContextLength)
            {
                return Failure(GeminiError.Unexpected, "請求或服務設定不符，請檢查 Gemini 設定與服務可用性。");
            }

            if (result.Outcome == GeminiOutcome.Success)
            {
                return result with { Message = "測試成功" };
            }

            if (result.Error == GeminiError.Timeout)
            {
                return result with { Message = "請求逾時（60 秒），可再次送出測試。" };
            }

            if (result.Error == GeminiError.Unexpected && result.Message == "請求失敗，請檢查設定後再試。")
            {
                return result with { Message = "測試失敗，請檢查設定後再試。" };
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (KeyConfigurationException exception)
        {
            return Failure(GeminiError.Configuration, exception.Message);
        }

        GeminiResult Failure(GeminiError error, string message)
        {
            logger.LogWarning("Gemini error {Category}; HTTP {Status}; elapsed {ElapsedMilliseconds}ms",
                error, null, timer.ElapsedMilliseconds);
            return new GeminiResult(GeminiOutcome.Error, "", message, error);
        }
    }
}

public sealed class GeminiChatService(GeminiApiClient apiClient) : IGeminiChatService
{
    public async Task<GeminiResult> GenerateAsync(string modelId, string apiKey, IReadOnlyList<GeminiChatMessage> messages, CancellationToken cancellationToken)
    {
        if (!GeminiChatModels.IsAllowed(modelId))
        {
            return new GeminiResult(GeminiOutcome.Error, "", "所選模型不在允許清單，請重新選擇。", GeminiError.Configuration);
        }

        var result = await apiClient.GenerateAsync(modelId, apiKey, messages, cancellationToken);
        if (result.Outcome == GeminiOutcome.Success)
        {
            return result with { Message = "對話成功" };
        }

        if (result.Error == GeminiError.Timeout)
        {
            return result with { Message = "請求逾時（60 秒），可再次送出訊息。" };
        }

        return result;
    }
}