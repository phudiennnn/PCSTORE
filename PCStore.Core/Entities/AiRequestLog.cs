namespace PCStore.Core.Entities;

/// <summary>Nhật ký một lần gọi nhà cung cấp AI.</summary>
public class AiRequestLog
{
    public long Id { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string Intent { get; set; } = string.Empty;

    public int LatencyMilliseconds { get; set; }

    public bool IsSuccess { get; set; }

    public string? FailoverReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
