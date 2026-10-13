using System.Text;

namespace TestAPIKeyCalling.Services;

public sealed class FileApiKeyReader(IWebHostEnvironment environment) : IApiKeyReader
{
    public const string FileName = "gemini-api-key.txt";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<string> ReadAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(environment.ContentRootPath, FileName);
        string contents;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            contents = StrictUtf8.GetString(bytes).TrimStart('\uFEFF');
        }
        catch (FileNotFoundException)
        {
            throw new KeyConfigurationException($"找不到 {FileName}，請建立 UTF-8 檔案：第一行可填 # 提示註解，下一行只填一組 Key，不加引號。");
        }
        catch (DirectoryNotFoundException)
        {
            throw new KeyConfigurationException($"找不到 {FileName}，請在專案根目錄建立檔案並只填一組 Key。");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            throw new KeyConfigurationException($"無法讀取 {FileName}，請檢查檔案權限與 UTF-8 格式。");
        }

        using var lines = new StringReader(contents);
        string? key = null;
        while (lines.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = line.Trim();
            if (value.Length == 0 || value.StartsWith('#'))
            {
                continue;
            }

            if (key is not null)
            {
                throw new KeyConfigurationException($"{FileName} 只能填入一組 Key，請移除其他有效行。");
            }
            key = value;
        }

        if (key is null || key == "YOUR_GEMINI_API_KEY")
        {
            throw new KeyConfigurationException($"請在 {FileName} 填入一組 Gemini API Key，取代 YOUR_GEMINI_API_KEY，不需加引號。");
        }

        // Printable ASCII prevents control/header injection and encoding ambiguity.
        // Do not assume a key prefix or fixed length; Gemini performs authentication.
        if (key.Any(character => character < '!' || character > '~'))
        {
            throw new KeyConfigurationException($"{FileName} 的 Key 無法安全用於認證標頭，請確認沒有內部空白或控制字元。");
        }

        return key;
    }
}