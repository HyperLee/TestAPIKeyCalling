namespace TestAPIKeyCalling.Services;

public sealed record GeminiChatModelOption(string Id, string DisplayName, string Hint);

public static class GeminiChatModels
{
    public const string DefaultModelId = "gemini-3.8-flash";

    public static IReadOnlyList<GeminiChatModelOption> All { get; } = Array.AsReadOnly<GeminiChatModelOption>([
        new GeminiChatModelOption("gemini-3.8-flash", "Gemini 3.8 Flash", "建議選擇"),
        new GeminiChatModelOption("gemini-3.5-flash-lite", "Gemini 3.5 Flash-Lite", "較省成本"),
        new GeminiChatModelOption("gemini-2.5-flash", "Gemini 2.5 Flash", "存取可能受限")
    ]);

    public static bool IsAllowed(string? modelId)
    {
        return modelId is not null && All.Any(model => model.Id == modelId);
    }
}