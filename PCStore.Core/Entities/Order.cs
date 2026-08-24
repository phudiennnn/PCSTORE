using System;
using System.Collections.Generic;
using System.Text;
using PCStore.Core.Enums;

namespace PCStore.Core.Entities;

public class Order
{
    public int Id { get; set; }
    public string OrderCode { get; set; } = string.Empty; // Mã đơn hàng (VD: ORD-2026-0001)
    public decimal TotalAmount { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public string ShippingAddress { get; set; } = string.Empty;
    public string ReceiverPhone { get; set; } = string.Empty;
    public string ReceiverName { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = "COD"; // COD, VNPAY, MoMo
    public bool IsPaid { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int? UserId { get; set; }
    public User? User { get; set; }

    public ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
}