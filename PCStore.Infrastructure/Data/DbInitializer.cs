using Microsoft.EntityFrameworkCore;
using PCStore.Core.Entities;
using PCStore.Core.Enums;

namespace PCStore.Infrastructure.Data;

public static class DbInitializer // <-- Bắt buộc phải có từ khóa 'public'
{
    public static async Task SeedAsync(AppDbContext context) // <-- Bắt buộc phải có từ khóa 'public'
    {
        // 1. Seed Users
        if (!await context.Users.AnyAsync())
        {
            var users = new List<User>
            {
                new()
                {
                    FullName = "System Administrator",
                    Email = "admin@pcstore.vn",
                    PhoneNumber = "0905123456",
                    PasswordHash = "AQAAAAIAAYagAAAAEJsamplehash",
                    Role = UserRole.Admin,
                    IsActive = true
                },
                new()
                {
                    FullName = "Nguyễn Văn A",
                    Email = "khachhang@gmail.com",
                    PhoneNumber = "0905987654",
                    PasswordHash = "AQAAAAIAAYagAAAAEJsamplehash",
                    Role = UserRole.Customer,
                    IsActive = true
                }
            };
            await context.Users.AddRangeAsync(users);
            await context.SaveChangesAsync();
        }

        // 2. Seed Categories
        if (!await context.Categories.AnyAsync())
        {
            var categories = new List<Category>
            {
                new() { Name = "Vi xử lý (CPU)", ComponentType = ComponentType.CPU, Description = "Bộ vi xử lý Intel & AMD" },
                new() { Name = "Bo mạch chủ (Mainboard)", ComponentType = ComponentType.Mainboard, Description = "Bo mạch chủ Intel LGA1700, AMD AM5/AM4" },
                new() { Name = "Bộ nhớ RAM", ComponentType = ComponentType.RAM, Description = "RAM DDR4 & DDR5 cho PC" },
                new() { Name = "Card màn hình (VGA/GPU)", ComponentType = ComponentType.GPU, Description = "NVIDIA GeForce & AMD Radeon" },
                new() { Name = "Ổ cứng SSD", ComponentType = ComponentType.SSD, Description = "SSD NVMe M.2 & SATA 2.5 inch" },
                new() { Name = "Nguồn máy tính (PSU)", ComponentType = ComponentType.PSU, Description = "Nguồn công suất thực chuẩn 80 Plus" },
                new() { Name = "Vỏ case PC", ComponentType = ComponentType.Case, Description = "Case Mid Tower, Mini Tower hỗ trợ ATX, M-ATX" },
                new() { Name = "Tản nhiệt CPU", ComponentType = ComponentType.Cooler, Description = "Tản nhiệt khí & Tản nhiệt nước AIO" }
            };
            await context.Categories.AddRangeAsync(categories);
            await context.SaveChangesAsync();
        }

        // 3. Seed Products & Technical Specs
        if (!await context.Products.AnyAsync())
        {
            var cpuCat = await context.Categories.FirstAsync(c => c.ComponentType == ComponentType.CPU);
            var mainCat = await context.Categories.FirstAsync(c => c.ComponentType == ComponentType.Mainboard);
            var ramCat = await context.Categories.FirstAsync(c => c.ComponentType == ComponentType.RAM);
            var gpuCat = await context.Categories.FirstAsync(c => c.ComponentType == ComponentType.GPU);
            var psuCat = await context.Categories.FirstAsync(c => c.ComponentType == ComponentType.PSU);

            var products = new List<Product>
            {
                new()
                {
                    Name = "CPU Intel Core i5 13400F (LGA1700, up to 4.6GHz, 10C/16T, 65W)",
                    Sku = "CPU-INTEL-13400F",
                    Brand = "Intel",
                    Price = 4890000m,
                    StockQuantity = 20,
                    CategoryId = cpuCat.Id,
                    TechnicalSpec = new TechnicalSpec
                    {
                        Socket = "LGA1700",
                        TdpWattage = 65,
                        RamType = "DDR4, DDR5"
                    }
                },
                new()
                {
                    Name = "CPU AMD Ryzen 5 7600 (AM5, up to 5.1GHz, 6C/12T, 65W)",
                    Sku = "CPU-AMD-R5-7600",
                    Brand = "AMD",
                    Price = 5290000m,
                    StockQuantity = 15,
                    CategoryId = cpuCat.Id,
                    TechnicalSpec = new TechnicalSpec
                    {
                        Socket = "AM5",
                        TdpWattage = 65,
                        RamType = "DDR5"
                    }
                },
                new()
                {
                    Name = "Mainboard ASUS TUF GAMING B760M-PLUS WIFI DDR5",
                    Sku = "MB-ASUS-B760M-TUF",
                    Brand = "ASUS",
                    Price = 3990000m,
                    StockQuantity = 10,
                    CategoryId = mainCat.Id,
                    TechnicalSpec = new TechnicalSpec
                    {
                        Socket = "LGA1700",
                        Chipset = "B760",
                        FormFactor = "Micro-ATX",
                        RamType = "DDR5",
                        RamSlots = 4,
                        RamBusSpeed = 7200
                    }
                },
                new()
                {
                    Name = "RAM Kingston Fury Beast 16GB (1x16GB) DDR5 5600MHz",
                    Sku = "RAM-KST-16G-D5-5600",
                    Brand = "Kingston",
                    Price = 1350000m,
                    StockQuantity = 50,
                    CategoryId = ramCat.Id,
                    TechnicalSpec = new TechnicalSpec
                    {
                        RamType = "DDR5",
                        RamBusSpeed = 5600
                    }
                },
                new()
                {
                    Name = "VGA ASUS Dual GeForce RTX 4060 EVO OC 8GB GDDR6",
                    Sku = "VGA-ASUS-4060-8G",
                    Brand = "ASUS",
                    Price = 8490000m,
                    StockQuantity = 8,
                    CategoryId = gpuCat.Id,
                    TechnicalSpec = new TechnicalSpec
                    {
                        TdpWattage = 115,
                        RecommendedPsu = 550,
                        LengthMm = 227
                    }
                },
                new()
                {
                    Name = "Nguồn Cooler Master MWE 650W V2 80 Plus Bronze",
                    Sku = "PSU-CM-MWE-650W",
                    Brand = "Cooler Master",
                    Price = 1450000m,
                    StockQuantity = 25,
                    CategoryId = psuCat.Id,
                    TechnicalSpec = new TechnicalSpec
                    {
                        TdpWattage = 650
                    }
                }
            };

            await context.Products.AddRangeAsync(products);
            await context.SaveChangesAsync();
        }
    }
}