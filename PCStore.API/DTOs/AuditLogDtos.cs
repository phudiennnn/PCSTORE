namespace PCStore.API.DTOs;

/// <summary>One read-only activity record visible to administrators.</summary>
public sealed record AuditLogItemResponse(
    int Id,
    DateTimeOffset Timestamp,
    string? PerformedBy,
    string? PerformedByRole,
    string? Action,
    string? Module,
    string? IpAddress,
    string? UserAgent,
    string Status,
    string? Details);

/// <summary>A page of activity records with total-match metadata.</summary>
public sealed record AuditLogPageResponse(
    IReadOnlyList<AuditLogItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);
