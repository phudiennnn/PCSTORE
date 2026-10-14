namespace PCStore.API.DTOs;

/// <summary>Database connection fields entered by an administrator.</summary>
public sealed record DatabaseConfigurationRequest(
    string Host,
    int Port,
    string Database,
    string Username,
    string? Password);

/// <summary>AI provider and retrieval settings entered by an administrator.</summary>
public sealed record AiConfigurationRequest(
    string? ApiKey,
    string Model,
    decimal Temperature,
    int MaxTokens,
    int RagTopK,
    decimal RagThreshold);

/// <summary>Updates database and AI runtime configuration.</summary>
public sealed record UpdateSystemConfigurationRequest(
    DatabaseConfigurationRequest Database,
    AiConfigurationRequest Ai);

/// <summary>Non-secret system settings and flags indicating whether secrets exist.</summary>
public sealed record SystemConfigurationResponse(
    string DatabaseHost,
    int DatabasePort,
    string DatabaseName,
    string DatabaseUsername,
    bool DatabasePasswordConfigured,
    bool DatabaseConnectionChangePendingRestart,
    bool AiApiKeyConfigured,
    string AiModel,
    decimal AiTemperature,
    int AiMaxTokens,
    int RagTopK,
    decimal RagThreshold);

/// <summary>Database connection health-check outcome.</summary>
public sealed record ConfigurationHealthResponse(
    string Status,
    double? LatencyMs,
    string Message);
