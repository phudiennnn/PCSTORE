namespace PCStore.API.Services;

public static class SupportTicketWorkflow
{
    private static readonly string[] Statuses = ["Open", "InProgress", "Resolved", "Closed"];

    public static string? NormalizeStatus(string? status) =>
        Statuses.FirstOrDefault(candidate =>
            string.Equals(candidate, status?.Trim(), StringComparison.OrdinalIgnoreCase));
}
