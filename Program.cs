using TestAPIKeyCalling.Services;

namespace TestAPIKeyCalling;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        ConfigureServices(builder.Services, builder.Configuration);
        var app = builder.Build();
        ConfigurePipeline(app);
        app.Run();
    }

    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddRazorPages();
        services.AddMemoryCache();
        services.AddSingleton<IChatSessionStore, MemoryChatSessionStore>();
        services.AddSingleton(new GeminiSettings(configuration["Gemini:Model"]));
        services.AddScoped<IApiKeyReader, FileApiKeyReader>();
        services.AddHttpClient<GeminiApiClient>("Gemini", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .RedactLoggedHeaders(_ => true)
            .RemoveAllLoggers();
        services.AddScoped<IGeminiService, GeminiService>();
        services.AddScoped<IGeminiChatService, GeminiChatService>();
    }

    public static void ConfigurePipeline(WebApplication app)
    {
        // Use safe error pages even in Development for this credential testing app.
        app.UseExceptionHandler("/Error");
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseAuthorization();
        app.MapStaticAssets();
        app.MapRazorPages().WithStaticAssets();
    }
}
