namespace TestAPIKeyCalling.Services;

public static class InputValidation
{
    public const int MaximumLength = 4000;

    public static string? GetError(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "請輸入要測試的文字";
        }

        return input.Length > MaximumLength ? "輸入內容不可超過 4,000 個字元" : null;
    }
}