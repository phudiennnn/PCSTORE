using PCStore.Core.Enums;

namespace PCStore.Core.Entities;

public class OrderStatusHistory
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public string? TrackingNumber { get; set; }
    public string? Carrier { get; set; }
    public string? ChangedByName { get; set; }
    public string? Note { get; set; }
}
