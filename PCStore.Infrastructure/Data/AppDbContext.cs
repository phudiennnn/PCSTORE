using Microsoft.EntityFrameworkCore;
using PCStore.Core.Entities;

namespace PCStore.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<TechnicalSpec> TechnicalSpecs => Set<TechnicalSpec>();
    public DbSet<PcBuild> PcBuilds => Set<PcBuild>();
    public DbSet<PcBuildItem> PcBuildItems => Set<PcBuildItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderDetail> OrderDetails => Set<OrderDetail>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<OrderActionRequest> OrderActionRequests => Set<OrderActionRequest>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. User
        modelBuilder.Entity<User>(b =>
        {
            b.HasKey(u => u.Id);
            b.HasIndex(u => u.Email).IsUnique();
            b.Property(u => u.Email).IsRequired().HasMaxLength(150);
            b.Property(u => u.FullName).IsRequired().HasMaxLength(100);
            b.Property(u => u.Role).HasConversion<string>();
        });

        modelBuilder.Entity<RolePermission>(b =>
        {
            b.HasKey(permission => permission.Id);
            b.Property(permission => permission.Role).HasConversion<string>().HasMaxLength(20);
            b.Property(permission => permission.Module).IsRequired().HasMaxLength(50);
            b.Property(permission => permission.Action).IsRequired().HasMaxLength(20);
            b.Property(permission => permission.UpdatedBy).HasMaxLength(150);
            b.HasIndex(permission => new { permission.Role, permission.Module, permission.Action }).IsUnique();
        });

        modelBuilder.Entity<SupportTicket>(b =>
        {
            b.HasKey(ticket => ticket.Id);
            b.Property(ticket => ticket.CustomerName).IsRequired().HasMaxLength(100);
            b.Property(ticket => ticket.CustomerEmail).IsRequired().HasMaxLength(150);
            b.Property(ticket => ticket.Subject).IsRequired().HasMaxLength(200);
            b.Property(ticket => ticket.Description).IsRequired().HasMaxLength(2000);
            b.Property(ticket => ticket.Status).IsRequired().HasMaxLength(20).HasDefaultValue("Open");
            b.HasIndex(ticket => new { ticket.Status, ticket.CreatedAt });
            b.HasOne<User>()
                .WithMany()
                .HasForeignKey(ticket => ticket.CustomerUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AuditLog>(b =>
        {
            b.Property(log => log.Action).HasMaxLength(100);
            b.Property(log => log.Module).HasMaxLength(50);
            b.Property(log => log.PerformedBy).HasMaxLength(150);
            b.Property(log => log.PerformedByRole).HasMaxLength(20);
            b.Property(log => log.IpAddress).HasMaxLength(64);
            b.Property(log => log.UserAgent).HasMaxLength(1000);
            b.Property(log => log.Status).IsRequired().HasMaxLength(20).HasDefaultValue("Success");
            b.HasIndex(log => log.Timestamp);
            b.HasIndex(log => new { log.Module, log.Action, log.Timestamp });
        });

        // 2. Category
        modelBuilder.Entity<Category>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).IsRequired().HasMaxLength(100);
            b.Property(c => c.Slug).IsRequired().HasMaxLength(120);
            b.Property(c => c.ComponentType).HasConversion<string>();
            b.HasIndex(c => c.Name).IsUnique();
        });

        modelBuilder.Entity<Brand>(b =>
        {
            b.HasKey(brand => brand.Id);
            b.Property(brand => brand.Name).IsRequired().HasMaxLength(100);
            b.Property(brand => brand.Slug).IsRequired().HasMaxLength(120);
            b.Property(brand => brand.LogoUrl).HasMaxLength(500);
            b.Property(brand => brand.Description).HasMaxLength(500);
            b.HasIndex(brand => brand.Name).IsUnique();
            b.HasIndex(brand => brand.Slug).IsUnique();
        });

        // 3. Product
        modelBuilder.Entity<Product>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Name).IsRequired().HasMaxLength(250);
            b.Property(p => p.Sku).IsRequired().HasMaxLength(100);
            b.Property(p => p.Description).HasMaxLength(2000);
            b.HasIndex(p => p.Sku).IsUnique();
            b.Property(p => p.Price).HasPrecision(18, 2);
            b.Property(p => p.PromotionalPrice).HasPrecision(18, 2);
            b.Property(p => p.LowStockThreshold).HasDefaultValue(5);
            b.Property(p => p.WarehouseLocation).HasMaxLength(100);

            b.HasOne(p => p.Category)
             .WithMany(c => c.Products)
             .HasForeignKey(p => p.CategoryId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // 4. TechnicalSpec (1-1 voi Product)
        modelBuilder.Entity<TechnicalSpec>(b =>
        {
            b.HasKey(ts => ts.Id);
            b.HasOne(ts => ts.Product)
             .WithOne(p => p.TechnicalSpec)
             .HasForeignKey<TechnicalSpec>(ts => ts.ProductId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // 5. PcBuild & PcBuildItem
        modelBuilder.Entity<PcBuild>(b =>
        {
            b.HasKey(pb => pb.Id);
            b.Property(pb => pb.Purpose).IsRequired().HasMaxLength(30).HasDefaultValue("general");
            b.Property(pb => pb.TotalPrice).HasPrecision(18, 2);
            b.HasOne(pb => pb.User)
             .WithMany(u => u.PcBuilds)
             .HasForeignKey(pb => pb.UserId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PcBuildItem>(b =>
        {
            b.HasKey(bi => bi.Id);
            b.Property(bi => bi.UnitPrice).HasPrecision(18, 2);
            b.HasOne(bi => bi.PcBuild)
             .WithMany(pb => pb.Items)
             .HasForeignKey(bi => bi.PcBuildId)
             .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(bi => bi.Product)
             .WithMany()
             .HasForeignKey(bi => bi.ProductId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // 6. Order & OrderDetail
        modelBuilder.Entity<Order>(b =>
        {
            b.HasKey(o => o.Id);
            b.HasIndex(o => o.OrderCode).IsUnique();
            b.Property(o => o.OrderCode).IsRequired().HasMaxLength(50);
            b.Property(o => o.TotalAmount).HasPrecision(18, 2);
            b.Property(o => o.Status).HasConversion<string>();
            b.HasOne(o => o.User)
             .WithMany(u => u.Orders)
             .HasForeignKey(o => o.UserId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OrderDetail>(b =>
        {
            b.HasKey(od => od.Id);
            b.Property(od => od.UnitPrice).HasPrecision(18, 2);
            b.HasOne(od => od.Order)
             .WithMany(o => o.OrderDetails)
             .HasForeignKey(od => od.OrderId)
             .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(od => od.Product)
             .WithMany()
             .HasForeignKey(od => od.ProductId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Voucher>(b =>
        {
            b.HasKey(voucher => voucher.Id);
            b.Property(voucher => voucher.Code).IsRequired().HasMaxLength(50);
            b.Property(voucher => voucher.DiscountType).IsRequired().HasMaxLength(20);
            b.Property(voucher => voucher.DiscountValue).HasPrecision(18, 2);
            b.Property(voucher => voucher.MinimumOrderAmount).HasPrecision(18, 2);
            b.Property(voucher => voucher.MaximumDiscountAmount).HasPrecision(18, 2);
            b.HasIndex(voucher => voucher.Code).IsUnique();
        });

        modelBuilder.Entity<OrderStatusHistory>(b =>
        {
            b.HasKey(h => h.Id);
            b.Property(h => h.Status).HasConversion<string>();
            b.Property(h => h.TrackingNumber).HasMaxLength(100);
            b.Property(h => h.Carrier).HasMaxLength(100);
            b.Property(h => h.ChangedByName).HasMaxLength(150);
            b.Property(h => h.Note).HasMaxLength(500);
            b.HasIndex(h => new { h.OrderId, h.ChangedAt });
            b.HasOne(h => h.Order)
             .WithMany(o => o.StatusHistory)
             .HasForeignKey(h => h.OrderId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderActionRequest>(b =>
        {
            b.HasKey(request => request.Id);
            b.Property(request => request.RequestType).IsRequired().HasMaxLength(20);
            b.Property(request => request.Reason).IsRequired().HasMaxLength(500);
            b.Property(request => request.Status).IsRequired().HasMaxLength(20);
            b.Property(request => request.ReviewNote).HasMaxLength(500);
            b.HasIndex(request => new { request.OrderId, request.Status });
            b.HasOne(request => request.Order)
                .WithMany(order => order.ActionRequests)
                .HasForeignKey(request => request.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(request => request.ReviewedByUser)
                .WithMany()
                .HasForeignKey(request => request.ReviewedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Review>(b =>
        {
            b.HasKey(r => r.Id);
            b.Property(r => r.Comment).HasMaxLength(1000);
            b.HasIndex(r => new { r.UserId, r.ProductId })
             .IsUnique()
             .HasFilter("\"OrderId\" IS NOT NULL");
            b.HasOne(r => r.Product)
             .WithMany()
             .HasForeignKey(r => r.ProductId)
             .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(r => r.User)
             .WithMany()
             .HasForeignKey(r => r.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(r => r.Order)
             .WithMany()
             .HasForeignKey(r => r.OrderId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SystemSetting>(b =>
        {
            b.HasKey(s => s.Id);
            b.HasIndex(s => s.Key).IsUnique();
            b.Property(s => s.Key).IsRequired().HasMaxLength(100);
        });
    }
}