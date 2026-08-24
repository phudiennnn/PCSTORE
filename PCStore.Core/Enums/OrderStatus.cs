namespace PCStore.Core.Enums;

public enum OrderStatus
{
    Pending = 1,       // Chờ xử lý / Chờ nhân viên tiếp nhận
    Confirmed = 2,     // Đã tiếp nhận & đóng gói
    Shipping = 3,      // Đang giao hàng
    Completed = 4,     // Hoàn tất đơn
    Cancelled = 5,     // Đã hủy
    Refunded = 6       // Đã hoàn tiền
}