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
    public DbSet<Product> Products => Set<Product>();
    public DbSet<TechnicalSpec> TechnicalSpecs => Set<TechnicalSpec>();
    public DbSet<PcBuild> PcBuilds => Set<PcBuild>();
    public DbSet<PcBuildItem> PcBuildItems => Set<PcBuildItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderDetail> OrderDetails => Set<OrderDetail>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

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

        // 2. Category
        modelBuilder.Entity<Category>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).IsRequired().HasMaxLength(100);
            b.Property(c => c.ComponentType).HasConversion<string>();
        });

        // 3. Product
        modelBuilder.Entity<Product>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Name).IsRequired().HasMaxLength(250);
            b.Property(p => p.Sku).IsRequired().HasMaxLength(100);
            b.Property(p => p.Price).HasPrecision(18, 2);

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