using PCStore.Core.Enums;
using PCStore.API.DTOs;

namespace PCStore.API.Services;

public static class RolePermissionCatalog
{
    public static readonly IReadOnlyList<PermissionModuleResponse> Modules =
    [
        new("reports", "Báo cáo doanh thu"),
        new("orders", "Đơn hàng"),
        new("customers", "Khách hàng"),
        new("catalog", "Sản phẩm và danh mục"),
        new("inventory", "Kho hàng"),
        new("promotions", "Giá và voucher"),
        new("pcbuilder", "Tư vấn cấu hình PC"),
        new("accounts", "Tài khoản"),
        new("employees", "Nhân viên"),
        new("settings", "Cấu hình hệ thống"),
        new("backup", "Sao lưu dữ liệu"),
        new("permissions", "Phân quyền"),
        new("auditlogs", "Nhật ký hoạt động"),
    ];

    public static readonly IReadOnlyList<PermissionActionResponse> Actions =
    [
        new("view", "Xem"),
        new("create", "Thêm"),
        new("update", "Sửa"),
        new("delete", "Xóa"),
        new("approve", "Duyệt"),
    ];

    public static bool IsValid(string module, string action) =>
        Modules.Any(item => item.Key == module) && Actions.Any(item => item.Key == action);

    public static bool IsAllowedByDefault(UserRole role, string module, string action)
    {
        if (role == UserRole.Admin)
            return true;

        if (role == UserRole.Manager)
        {
            return module switch
            {
                "reports" => action is "view" or "create",
                "customers" => action == "view",
                "pcbuilder" => action is "view" or "create",
                "orders" => action is "view" or "update" or "approve",
                "catalog" or "inventory" or "promotions" => action is "view" or "create" or "update" or "delete",
                "employees" => action == "view",
                _ => false,
            };
        }

        if (role == UserRole.Staff)
        {
            return module switch
            {
                "orders" => action is "view" or "update",
                "pcbuilder" => action is "view" or "create",
                _ => false,
            };
        }

        return false;
    }
}
