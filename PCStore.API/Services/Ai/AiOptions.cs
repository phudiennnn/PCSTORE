namespace PCStore.API.Services.Ai;

/// <summary>Mục cấu hình Ai trong appsettings. Giá trị khởi tạo chỉ dùng khi mục đó thiếu.</summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public int CacheRefreshSeconds { get; set; } = 2;

    public int HttpHandlerLifetimeSeconds { get; set; } = 30;

    public int ConnectionLifetimeSeconds { get; set; } = 120;

    public string DataProtectionPurpose { get; set; } = "PCSTORE.API.AiProviderSettings.v1";

    public string LegacyDataProtectionPurpose { get; set; } = "PCSTORE.API.SystemConfiguration.v1";

    public string PrivacyReplacement { get; set; } = "[đã ẩn]";

    public int CircuitFailureThreshold { get; set; } = 3;

    public int CircuitOpenSeconds { get; set; } = 60;

    public int HealthCheckIntervalSeconds { get; set; } = 30;

    public OllamaClientOptions Ollama { get; set; } = new();

    public GeminiClientOptions Gemini { get; set; } = new();

    public AiTaskOptions Tasks { get; set; } = new();

    public LegacyAiSettingKeyOptions LegacySettingKeys { get; set; } = new();
}

/// <summary>Đường dẫn và giá trị seed của Ollama.</summary>
public sealed class OllamaClientOptions
{
    public string HttpClientName { get; set; } = "pcstore-ollama";

    public string BaseUrl { get; set; } = "http://127.0.0.1:11434";

    public string ModelName { get; set; } = "qwen2.5:7b";

    public string KeepAlive { get; set; } = "30m";

    public int NumCtx { get; set; } = 4096;

    public int TimeoutSeconds { get; set; } = 120;

    public int ConnectTimeoutSeconds { get; set; } = 3;

    public int HealthTimeoutSeconds { get; set; } = 5;

    public string GeneratePath { get; set; } = "/api/generate";

    public string HealthPath { get; set; } = "/api/tags";

    public string WarmUpPrompt { get; set; } = "ping";

    public int WarmUpNumPredict { get; set; } = 1;

    public bool IsMaintenanceMode { get; set; }
}

/// <summary>Đường dẫn và giá trị seed của Gemini.</summary>
public sealed class GeminiClientOptions
{
    public string HttpClientName { get; set; } = "pcstore-gemini";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com";

    public string ApiVersion { get; set; } = "v1beta";

    public string ModelName { get; set; } = "gemini-3.8-flash";

    public string[] FallbackModelNames { get; set; } = ["gemini-3.6-flash", "gemini-3.5-flash-lite"];

    public string[] RetiredModelNames { get; set; } = ["gemini-2.0-flash", "gemini-2.0-flash-001", "gemini-2.0-flash-lite", "gemini-2.0-flash-lite-001", "gemini-1.5-flash", "gemini-1.5-pro"];

    public string ApiKey { get; set; } = "";

    public string ApiKeyHeaderName { get; set; } = "x-goog-api-key";

    public string GeneratePathSuffix { get; set; } = ":generateContent";

    public string StreamPathSuffix { get; set; } = ":streamGenerateContent";

    public string StreamQuery { get; set; } = "alt=sse";

    public int TimeoutSeconds { get; set; } = 30;

    public int ConnectTimeoutSeconds { get; set; } = 3;

    public int HealthTimeoutSeconds { get; set; } = 8;

    public decimal Temperature { get; set; } = 0.4m;

    public int MaxOutputTokens { get; set; } = 512;

    public bool IsEnabled { get; set; } = true;
}

/// <summary>Tham số theo từng tác vụ, không phụ thuộc nhà cung cấp.</summary>
public sealed class AiTaskOptions
{
    public IntentTaskOptions ExtractIntent { get; set; } = new();

    public ExplainTaskOptions ExplainSelection { get; set; } = new();
}

/// <summary>Nhiệt độ và độ dài cho tác vụ trích ý định.</summary>
public sealed class IntentTaskOptions
{
    public double Temperature { get; set; } = 0.1d;

    public int NumPredict { get; set; } = 200;

    public int MaxRepairInputCharacters { get; set; } = 2000;

    public int MaxCustomerMessageCharacters { get; set; } = 4000;
}

/// <summary>Độ dài lời tư vấn. Nhiệt độ lấy từ cấu hình Gemini trong DB.</summary>
public sealed class ExplainTaskOptions
{
    public int MaxWords { get; set; } = 120;
}

/// <summary>Khóa SystemSettings cũ để copy một lần khi seed.</summary>
public sealed class LegacyAiSettingKeyOptions
{
    public string ApiKey { get; set; } = "system.ai.api-key";

    public string Model { get; set; } = "system.ai.model";

    public string Temperature { get; set; } = "system.ai.temperature";

    public string MaxTokens { get; set; } = "system.ai.max-tokens";
}
