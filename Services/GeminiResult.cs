namespace TestAPIKeyCalling.Services;

public enum GeminiOutcome
{
    Success,
    Truncated,
    Error
}

public enum GeminiError
{
    None,
    Input,
    Configuration,
    Authentication,
    ModelNotFound,
    Quota,
    RemoteService,
    Network,
    Timeout,
    Blocked,
    NoText,
    InvalidResponse,
    Unexpected
}

public sealed record GeminiResult(GeminiOutcome Outcome, string Text, string Message, GeminiError Error = GeminiError.None);

public interface IApiKeyReader
{
    Task<string> ReadAsync(CancellationToken cancellationToken);
}

public interface IGeminiService
{
    Task<GeminiResult> GenerateAsync(string input, CancellationToken cancellationToken);
}

public sealed class KeyConfigurationException(string message) : Exception(message);

public sealed class GeminiSettings(string? model)
{
    public const string DefaultModel = "gemini-2.5-flash";

    public string Model { get; } = model ?? DefaultModel;
}