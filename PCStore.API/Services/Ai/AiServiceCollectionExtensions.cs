using PCStore.Core.Ai;

namespace PCStore.API.Services.Ai;

/// <summary>Đăng ký tầng AI, HTTP client và health-check.</summary>
public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddPcStoreAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        var options = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();

        services.AddSingleton<IAiClock, SystemAiClock>();
        services.AddSingleton<IAiJsonParser, AiJsonParser>();
        services.AddSingleton<IAiJsonValidator, AiJsonValidator>();
        services.AddSingleton<IPromptTemplateProvider, PromptTemplateProvider>();
        services.AddSingleton<IAiSettingsService, AiSettingsService>();
        services.AddSingleton<IAiRequestLogService, AiRequestLogService>();
        services.AddSingleton<IOllamaHealthState, OllamaHealthState>();
        services.AddSingleton<OllamaProvider>();
        services.AddSingleton<IAiProvider>(serviceProvider => serviceProvider.GetRequiredService<OllamaProvider>());
        services.AddSingleton<GeminiProvider>();
        services.AddSingleton<IAiProvider>(serviceProvider => serviceProvider.GetRequiredService<GeminiProvider>());
        services.AddSingleton<IAiOrchestrator, AiOrchestrator>();
        services.AddHostedService<OllamaHealthCheckService>();

        AddNamedClient(services, options.Ollama.HttpClientName, options, useOllamaTimeout: true);
        AddNamedClient(services, options.Gemini.HttpClientName, options, useOllamaTimeout: false);
        return services;
    }

    private static void AddNamedClient(IServiceCollection services, string clientName, AiOptions options, bool useOllamaTimeout)
    {
        services.AddHttpClient(clientName)
            .RemoveAllLoggers()
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .SetHandlerLifetime(TimeSpan.FromSeconds(Math.Max(1, options.HttpHandlerLifetimeSeconds)))
            .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
            {
                var settings = serviceProvider.GetRequiredService<IAiSettingsService>();
                var seconds = useOllamaTimeout
                    ? settings.GetOllamaConnectTimeoutSeconds()
                    : settings.GetGeminiConnectTimeoutSeconds();
                return new SocketsHttpHandler
                {
                    ConnectTimeout = TimeSpan.FromSeconds(Math.Max(1, seconds)),
                    PooledConnectionLifetime = TimeSpan.FromSeconds(Math.Max(1, options.ConnectionLifetimeSeconds)),
                };
            });
    }
}
