using PCStore.Core.Ai;

namespace PCStore.API.Services.Ai;

/// <summary>Đồng hồ UTC của hệ thống.</summary>
public sealed class SystemAiClock : IAiClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
