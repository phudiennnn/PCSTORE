namespace PCStore.Core.Entities;

/// <summary>Một dòng cấu hình Ollama hoặc Gemini trong cơ sở dữ liệu.</summary>
public class AiProviderSetting
{
    public int Id { get; set; }

    public string ProviderName { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    public string FallbackModelNames { get; set; } = "[]";

    public string? ApiVersion { get; set; }

    public string? ProtectedApiKey { get; set; }

    public string? KeepAlive { get; set; }

    public int? NumCtx { get; set; }

    public int TimeoutSeconds { get; set; }

    public int ConnectTimeoutSeconds { get; set; }

    public decimal? Temperature { get; set; }

    public int? MaxOutputTokens { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool IsMaintenanceMode { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
