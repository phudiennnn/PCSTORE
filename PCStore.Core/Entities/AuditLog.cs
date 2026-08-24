using System;
using System.Collections.Generic;
using System.Text;
namespace PCStore.Core.Entities;

public class AuditLog
{
    public int Id { get; set; }
    public string? Action { get; set; }         // CREATE_PRODUCT, TOGGLE_LOCK_USER, UPDATE_PRICE...
    public string? Module { get; set; }         // AUTH, PRODUCTS, ORDERS, CONFIG
    public string? PerformedBy { get; set; }    // Email người thực hiện
    public string? IpAddress { get; set; }
    public string? Details { get; set; }        // Dữ liệu payload/thay đổi
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}