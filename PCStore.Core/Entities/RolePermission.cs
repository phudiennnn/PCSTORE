using PCStore.Core.Enums;

namespace PCStore.Core.Entities;

public class RolePermission
{
    public int Id { get; set; }
    public UserRole Role { get; set; }
    public string Module { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public bool IsAllowed { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
}
