namespace PCStore.Core.Ai;

/// <summary>Đồng hồ để circuit breaker thử lại đúng hạn trong unit test.</summary>
public interface IAiClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Nhà cung cấp AI. JSON một lần, hoặc luồng chữ, hoặc kiểm tra sống.</summary>
public interface IAiProvider
{
    string ProviderName { get; }

    Task<string> GenerateJsonAsync(AiGenerationRequest request, CancellationToken cancellationToken);

    IAsyncEnumerable<string> StreamTextAsync(AiGenerationRequest request, CancellationToken cancellationToken);

    Task<AiHealthResult> CheckHealthAsync(CancellationToken cancellationToken);
}

/// <summary>Chọn nhà cung cấp, thử lại JSON một lần, rồi chuyển dự phòng.</summary>
public interface IAiOrchestrator
{
    Task<IntentExtractionResult> ExtractIntentAsync(string customerMessage, CancellationToken cancellationToken);

    IAsyncEnumerable<string> ExplainSelectionAsync(string selectedPartsJson, CancellationToken cancellationToken);

    IAsyncEnumerable<AiStreamToken> StreamExplanationAsync(string selectedPartsJson, CancellationToken cancellationToken);

    Task<string> GenerateCustomerReplyAsync(string customerMessage, CancellationToken cancellationToken);
}

/// <summary>Đọc và ghi cấu hình AI, có bộ nhớ đệm tự làm mới.</summary>
public interface IAiSettingsService
{
    Task<AiRuntimeSettings> GetRuntimeSettingsAsync(CancellationToken cancellationToken);

    Task<AiSettingsAdminView> GetAdminViewAsync(CancellationToken cancellationToken);

    Task SaveOllamaSettingsAsync(OllamaSettingsWriteModel model, CancellationToken cancellationToken);

    Task SaveGeminiSettingsAsync(GeminiSettingsWriteModel model, CancellationToken cancellationToken);

    void InvalidateCache();

    int GetOllamaConnectTimeoutSeconds();

    int GetGeminiConnectTimeoutSeconds();
}

/// <summary>Ghi nhật ký gọi AI.</summary>
public interface IAiRequestLogService
{
    Task WriteAsync(AiRequestLogEntry entry, CancellationToken cancellationToken);

    Task<AiUsageSummary> ReadRecentSummaryAsync(int lookbackHours, CancellationToken cancellationToken);
}

/// <summary>Ghép prompt theo phiên bản cố định.</summary>
public interface IPromptTemplateProvider
{
    string PromptVersion { get; }

    string BuildIntentPrompt(string customerMessage);

    string BuildIntentRepairPrompt(string invalidOutput);

    string BuildExplanationPrompt(string selectedPartsJson);

    string BuildCustomerReplyPrompt(string customerMessage);

    string BuildIntentJsonSchema();
}

/// <summary>Tách JSON ra khỏi phần chữ thừa của mô hình.</summary>
public interface IAiJsonParser
{
    string? ParseAiJsonResponse(string? rawResponse);
}

/// <summary>Kiểm tra JSON ý định theo schema cố định.</summary>
public interface IAiJsonValidator
{
    AiJsonValidationResult ValidateIntentJson(string json);
}

/// <summary>Kết quả health check Ollama gần nhất.</summary>
public interface IOllamaHealthState
{
    AiHealthResult? LastResult { get; }

    void Record(AiHealthResult result);
}
