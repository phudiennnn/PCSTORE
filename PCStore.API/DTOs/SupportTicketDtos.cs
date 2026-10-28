namespace PCStore.API.DTOs;

public sealed record CreateSupportTicketRequest(string Subject, string Description);

public sealed record UpdateSupportTicketRequest(string Status);

public sealed record SupportTicketResponse(
    int Id,
    string Customer,
    string CustomerEmail,
    string Subject,
    string Description,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
