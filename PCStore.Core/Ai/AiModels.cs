namespace PCStore.Core.Ai;

/// <summary>Yêu cầu sinh nội dung gửi cho một nhà cung cấp.</summary>
public sealed class AiGenerationRequest
{
    public required string Prompt { get; init; }
    public required string TaskName { get; init; }
    public bool ExpectJson { get; init; }
    public string? JsonSchema { get; init; }
    public double Temperature { get; init; }
    public int MaxOutputTokens { get; init; }
}

/// <summary>Kết quả kiểm tra sống của một nhà cung cấp.</summary>
public sealed class AiHealthResult
{
    public required string ProviderName { get; init; }
    public required bool IsHealthy { get; init; }
    public required string Message { get; init; }
    public int? LatencyMilliseconds { get; init; }
    public DateTimeOffset CheckedAt { get; init; }
}

/// <summary>Ý định khách hàng sau khi JSON đã được kiểm tra.</summary>
public sealed class IntentExtractionResult
{
    public required string Intent { get; init; }
    public long? BudgetVnd { get; init; }
    public string? Purpose { get; init; }
    public string? CpuBrand { get; init; }
    public string? GpuBrand { get; init; }
    public bool NeedMonitor { get; init; }
    public string? Notes { get; init; }
    public string ProviderName { get; init; } = string.Empty;
    public string PromptVersion { get; init; } = string.Empty;
}

/// <summary>Kết quả kiểm schema JSON ý định.</summary>
public sealed class AiJsonValidationResult
{
    public bool IsValid { get; init; }
    public IntentExtractionResult? Intent { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>Một đoạn chữ đã stream, kèm nhà cung cấp vừa sinh ra đoạn đó.</summary>
public readonly record struct AiStreamToken(string Text, string ProviderName);

/// <summary>Dòng sẽ ghi vào AiRequestLog.</summary>
public sealed class AiRequestLogEntry
{
    public required string Provider { get; init; }
    public required string Intent { get; init; }
    public required int LatencyMilliseconds { get; init; }
    public required bool IsSuccess { get; init; }
    public string? FailoverReason { get; init; }
    public int? TimeToFirstTokenMilliseconds { get; init; }
    public bool FaqHit { get; init; }
}

/// <summary>Số liệu AI trong một khoảng giờ gần nhất.</summary>
public sealed class AiUsageSummary
{
    public int LookbackHours { get; init; }
    public int RequestCount { get; init; }
    public int SuccessCount { get; init; }
    public int FaqHitCount { get; init; }
    public int FailoverCount { get; init; }
    public int? AverageLatencyMilliseconds { get; init; }
    public int? AverageTimeToFirstTokenMilliseconds { get; init; }
    public IReadOnlyList<AiProviderUsage> Providers { get; init; } = [];
}

/// <summary>Số liệu của một nhà cung cấp trong khoảng thống kê.</summary>
public sealed class AiProviderUsage
{
    public required string Provider { get; init; }
    public int RequestCount { get; init; }
    public int SuccessCount { get; init; }
    public int? AverageLatencyMilliseconds { get; init; }
}

/// <summary>Tham số tác vụ trích ý định, lấy từ cấu hình.</summary>
public sealed class IntentTaskSettings
{
    public double Temperature { get; init; }
    public int NumPredict { get; init; }
    public int MaxRepairInputCharacters { get; init; }
    public int MaxCustomerMessageCharacters { get; init; }
}

/// <summary>Tham số tác vụ diễn giải linh kiện đã chọn.</summary>
public sealed class ExplainTaskSettings
{
    public double Temperature { get; init; }
    public int MaxOutputTokens { get; init; }
    public int MaxWords { get; init; }
}

/// <summary>Cấu hình Ollama đang có hiệu lực.</summary>
public sealed class OllamaRuntimeSettings
{
    public required string BaseUrl { get; init; }
    public required string ModelName { get; init; }
    public required string KeepAlive { get; init; }
    public int NumCtx { get; init; }
    public int TimeoutSeconds { get; init; }
    public int ConnectTimeoutSeconds { get; init; }
    public bool IsMaintenanceMode { get; init; }

    public override string ToString() => ModelName;
}

/// <summary>Cấu hình Gemini đang có hiệu lực. ToString không in API Key.</summary>
public sealed class GeminiRuntimeSettings
{
    public required string BaseUrl { get; init; }
    public required string ApiVersion { get; init; }
    public required string ModelName { get; init; }
    public required IReadOnlyList<string> FallbackModelNames { get; init; }
    public string? ApiKey { get; init; }
    public int TimeoutSeconds { get; init; }
    public int ConnectTimeoutSeconds { get; init; }
    public decimal Temperature { get; init; }
    public int MaxOutputTokens { get; init; }
    public bool IsEnabled { get; init; }
    public bool ApiKeyUnreadable { get; init; }

    public override string ToString() => $"{ModelName}, keyConfigured={!string.IsNullOrEmpty(ApiKey)}";
}

/// <summary>Ảnh cấu hình AI trong bộ nhớ, gồm cả ngưỡng circuit breaker.</summary>
public sealed class AiRuntimeSettings
{
    public required OllamaRuntimeSettings Ollama { get; init; }
    public required GeminiRuntimeSettings Gemini { get; init; }
    public int CircuitFailureThreshold { get; init; }
    public int CircuitOpenSeconds { get; init; }
    public required IntentTaskSettings ExtractIntent { get; init; }
    public required ExplainTaskSettings ExplainSelection { get; init; }
}

/// <summary>Cấu hình AI để form Admin đọc. Không chứa API Key.</summary>
public sealed class AiSettingsAdminView
{
    public required OllamaSettingsWriteModel Ollama { get; init; }
    public required GeminiSettingsWriteModel Gemini { get; init; }
    public bool GeminiApiKeyConfigured { get; init; }
    public bool GeminiApiKeyUnreadable { get; init; }
    public DateTime? OllamaUpdatedAtUtc { get; init; }
    public DateTime? GeminiUpdatedAtUtc { get; init; }
}

/// <summary>Giá trị form Admin ghi cho Ollama.</summary>
public sealed class OllamaSettingsWriteModel
{
    public required string BaseUrl { get; init; }
    public required string ModelName { get; init; }
    public required string KeepAlive { get; init; }
    public int NumCtx { get; init; }
    public int TimeoutSeconds { get; init; }
    public int ConnectTimeoutSeconds { get; init; }
    public bool IsMaintenanceMode { get; init; }
}

/// <summary>Giá trị form Admin ghi cho Gemini. ApiKey rỗng nghĩa là giữ key cũ.</summary>
public sealed class GeminiSettingsWriteModel
{
    public string? ApiKey { get; init; }
    public required string ModelName { get; init; }
    public required IReadOnlyList<string> FallbackModelNames { get; init; }
    public required string BaseUrl { get; init; }
    public required string ApiVersion { get; init; }
    public int TimeoutSeconds { get; init; }
    public int ConnectTimeoutSeconds { get; init; }
    public decimal Temperature { get; init; }
    public int MaxOutputTokens { get; init; }
    public bool IsEnabled { get; init; }
}
