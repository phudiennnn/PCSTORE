using Microsoft.EntityFrameworkCore;
using PCStore.Core.Entities;
using PCStore.Core.Enums;
using System.Text.RegularExpressions;

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
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
                    Role = UserRole.Admin,
                    IsActive = true
                },
                new()
                {
                    FullName = "Nhân Viên Kho",
                    Email = "staff@pcstore.vn",
                    PhoneNumber = "0905111222",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Staff123!"),
                    Role = UserRole.Staff,
                    IsActive = true
                },
                new()
                {
                    FullName = "Nguyễn Văn A",
                    Email = "khachhang@gmail.com",
                    PhoneNumber = "0905987654",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Customer123!"),
                    Role = UserRole.Customer,
                    IsActive = true,
                    Address = "123 Nguyễn Văn Linh, Đà Nẵng"
                }
            };
            await context.Users.AddRangeAsync(users);
            await context.SaveChangesAsync();
        }

        // Fix seed users with invalid password hash
        var badHashUsers = await context.Users
            .Where(u => u.PasswordHash == "AQAAAAIAAYagAAAAEJsamplehash")
            .ToListAsync();
        foreach (var u in badHashUsers)
        {
            u.PasswordHash = u.Email == "admin@pcstore.vn"
                ? BCrypt.Net.BCrypt.HashPassword("Admin123!")
                : BCrypt.Net.BCrypt.HashPassword("Customer123!");
        }
        if (badHashUsers.Count > 0) await context.SaveChangesAsync();

        // Ensure staff user exists
        if (!await context.Users.AnyAsync(u => u.Email == "staff@pcstore.vn"))
        {
            context.Users.Add(new User
            {
                FullName = "Nhân Viên Kho",
                Email = "staff@pcstore.vn",
                PhoneNumber = "0905111222",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Staff123!"),
                Role = UserRole.Staff,
                IsActive = true
            });
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

        await SeedCpuCatalogAsync(context);
        await CatalogProductSeeder.SeedAsync(context);

        // 4. Seed System Settings
        if (!await context.SystemSettings.AnyAsync())
        {
            var settings = new List<SystemSetting>
            {
                new() { Key = "ai_api_url", Value = "https://api.openai.com/v1", Description = "URL API AI tư vấn" },
                new() { Key = "ai_api_key", Value = "", Description = "API Key AI (để trống = dùng rule-based)" },
                new() { Key = "ai_model", Value = "gpt-4o-mini", Description = "Model AI" },
                new() { Key = "store_name", Value = "PC STORE", Description = "Tên cửa hàng" },
                new() { Key = "hotline", Value = "1900 6868", Description = "Hotline hỗ trợ" },
                new() { Key = "backup_last", Value = "", Description = "Thời gian sao lưu gần nhất" }
            };
            await context.SystemSettings.AddRangeAsync(settings);
            await context.SaveChangesAsync();
        }

        // 5. Seed Reviews
        if (!await context.Reviews.AnyAsync())
        {
            var customer = await context.Users.FirstOrDefaultAsync(u => u.Email == "khachhang@gmail.com");
            var products = await context.Products.Take(3).ToListAsync();
            if (customer != null && products.Count > 0)
            {
                var reviews = new List<Review>
                {
                    new() { ProductId = products[0].Id, UserId = customer.Id, Rating = 5, Comment = "CPU chạy mượt, nhiệt thấp, đáng tiền!" },
                    new() { ProductId = products[0].Id, UserId = customer.Id, Rating = 4, Comment = "Giao hàng nhanh, đóng gói cẩn thận." }
                };
                if (products.Count > 1)
                    reviews.Add(new Review { ProductId = products[1].Id, UserId = customer.Id, Rating = 5, Comment = "Mainboard chất lượng, BIOS dễ cài." });
                await context.Reviews.AddRangeAsync(reviews);
                await context.SaveChangesAsync();
            }
        }
    }

    private static async Task SeedCpuCatalogAsync(AppDbContext context)
    {
        var cpuCategory = await context.Categories.FirstAsync(c => c.ComponentType == ComponentType.CPU);
        var existingProducts = await context.Products
            .Include(p => p.TechnicalSpec)
            .Where(p => p.CategoryId == cpuCategory.Id)
            .ToListAsync();

        var cpuCatalog = new[]
        {
            new CpuSeed("CPU Intel Core i9-14900K", "CPU-INT-14900K", "Intel", 15990000m, 14990000m, "LGA 1700", 24, 32, "3.2 GHz", "6.0 GHz", "36 MB", 125),
            new CpuSeed("CPU Intel Core i9-14900KF", "CPU-INT-14900KF", "Intel", 15290000m, 14490000m, "LGA 1700", 24, 32, "3.2 GHz", "6.0 GHz", "36 MB", 125),
            new CpuSeed("CPU Intel Core i7-14700K", "CPU-INT-14700K", "Intel", 11990000m, 11290000m, "LGA 1700", 20, 28, "3.4 GHz", "5.6 GHz", "33 MB", 125),
            new CpuSeed("CPU Intel Core i7-14700KF", "CPU-INT-14700KF", "Intel", 11290000m, 10590000m, "LGA 1700", 20, 28, "3.4 GHz", "5.6 GHz", "33 MB", 125),
            new CpuSeed("CPU Intel Core i5-14600K", "CPU-INT-14600K", "Intel", 8590000m, 7990000m, "LGA 1700", 14, 20, "3.5 GHz", "5.3 GHz", "24 MB", 125),
            new CpuSeed("CPU Intel Core i5-14600KF", "CPU-INT-14600KF", "Intel", 7990000m, 7490000m, "LGA 1700", 14, 20, "3.5 GHz", "5.3 GHz", "24 MB", 125),
            new CpuSeed("CPU Intel Core i5-14400F", "CPU-INT-14400F", "Intel", 5690000m, 5290000m, "LGA 1700", 10, 16, "2.5 GHz", "4.7 GHz", "20 MB", 65),
            new CpuSeed("CPU Intel Core i3-14100F", "CPU-INT-14100F", "Intel", 3290000m, 2990000m, "LGA 1700", 4, 8, "3.5 GHz", "4.7 GHz", "12 MB", 58),
            new CpuSeed("CPU Intel Core i9-13900K", "CPU-INT-13900K", "Intel", 14590000m, 13590000m, "LGA 1700", 24, 32, "3.0 GHz", "5.8 GHz", "36 MB", 125),
            new CpuSeed("CPU Intel Core i7-13700K", "CPU-INT-13700K", "Intel", 10590000m, 9990000m, "LGA 1700", 16, 24, "3.4 GHz", "5.4 GHz", "30 MB", 125),
            new CpuSeed("CPU Intel Core i5-13600K", "CPU-INT-13600K", "Intel", 7890000m, 7390000m, "LGA 1700", 14, 20, "3.5 GHz", "5.1 GHz", "24 MB", 125),
            new CpuSeed("CPU Intel Core i5-13400F", "CPU-INT-13400F", "Intel", 5290000m, 4890000m, "LGA 1700", 10, 16, "2.5 GHz", "4.6 GHz", "20 MB", 65),
            new CpuSeed("CPU Intel Core i3-13100F", "CPU-INT-13100F", "Intel", 2890000m, 2590000m, "LGA 1700", 4, 8, "3.4 GHz", "4.5 GHz", "12 MB", 58),
            new CpuSeed("CPU Intel Core i9-12900K", "CPU-INT-12900K", "Intel", 11590000m, 10590000m, "LGA 1700", 16, 24, "3.2 GHz", "5.2 GHz", "30 MB", 125),
            new CpuSeed("CPU Intel Core i7-12700K", "CPU-INT-12700K", "Intel", 7990000m, 7290000m, "LGA 1700", 12, 20, "3.6 GHz", "5.0 GHz", "25 MB", 125),
            new CpuSeed("CPU Intel Core i5-12400F", "CPU-INT-12400F", "Intel", 3590000m, 3190000m, "LGA 1700", 6, 12, "2.5 GHz", "4.4 GHz", "18 MB", 65),
            new CpuSeed("CPU Intel Core i3-12100F", "CPU-INT-12100F", "Intel", 2190000m, 1890000m, "LGA 1700", 4, 8, "3.3 GHz", "4.3 GHz", "12 MB", 58),
            new CpuSeed("CPU Intel Pentium Gold G7400", "CPU-INT-G7400", "Intel", 1690000m, 1490000m, "LGA 1700", 2, 4, "3.7 GHz", "3.7 GHz", "6 MB", 46),
            new CpuSeed("CPU Intel Core i9-14900KS", "CPU-INT-14900KS", "Intel", 18990000m, 17990000m, "LGA 1700", 24, 32, "3.2 GHz", "6.2 GHz", "36 MB", 150),
            new CpuSeed("CPU Intel Core i9-13900KS", "CPU-INT-13900KS", "Intel", 17590000m, 16590000m, "LGA 1700", 24, 32, "3.2 GHz", "6.0 GHz", "36 MB", 150),
            new CpuSeed("CPU Intel Core i7-14700", "CPU-INT-14700", "Intel", 10990000m, 10290000m, "LGA 1700", 20, 28, "2.1 GHz", "5.4 GHz", "33 MB", 65),
            new CpuSeed("CPU Intel Core i5-14500", "CPU-INT-14500", "Intel", 6890000m, 6290000m, "LGA 1700", 14, 20, "2.6 GHz", "4.7 GHz", "24 MB", 65),
            new CpuSeed("CPU Intel Core i5-13500", "CPU-INT-13500", "Intel", 6590000m, 5990000m, "LGA 1700", 14, 20, "2.5 GHz", "4.8 GHz", "24 MB", 65),
            new CpuSeed("CPU Intel Core i7-12700F", "CPU-INT-12700F", "Intel", 6990000m, 6490000m, "LGA 1700", 12, 20, "2.1 GHz", "4.9 GHz", "25 MB", 65),
            new CpuSeed("CPU Intel Core i5-12600K", "CPU-INT-12600K", "Intel", 5890000m, 5390000m, "LGA 1700", 10, 16, "3.7 GHz", "4.9 GHz", "20 MB", 125),
            new CpuSeed("CPU AMD Ryzen 9 7950X3D", "CPU-AMD-7950X3D", "AMD", 18990000m, 17990000m, "AM5", 16, 32, "4.2 GHz", "5.7 GHz", "144 MB", 120),
            new CpuSeed("CPU AMD Ryzen 9 7950X", "CPU-AMD-7950X", "AMD", 15990000m, 14990000m, "AM5", 16, 32, "4.5 GHz", "5.7 GHz", "64 MB", 170),
            new CpuSeed("CPU AMD Ryzen 9 7900X3D", "CPU-AMD-7900X3D", "AMD", 14590000m, 13590000m, "AM5", 12, 24, "4.4 GHz", "5.6 GHz", "140 MB", 120),
            new CpuSeed("CPU AMD Ryzen 9 7900X", "CPU-AMD-7900X", "AMD", 11990000m, 10990000m, "AM5", 12, 24, "4.7 GHz", "5.6 GHz", "64 MB", 170),
            new CpuSeed("CPU AMD Ryzen 7 7800X3D", "CPU-AMD-7800X3D", "AMD", 10990000m, 9990000m, "AM5", 8, 16, "4.2 GHz", "5.0 GHz", "96 MB", 120),
            new CpuSeed("CPU AMD Ryzen 7 7700X", "CPU-AMD-7700X", "AMD", 8990000m, 8290000m, "AM5", 8, 16, "4.5 GHz", "5.4 GHz", "32 MB", 105),
            new CpuSeed("CPU AMD Ryzen 7 7700", "CPU-AMD-7700", "AMD", 8590000m, 7890000m, "AM5", 8, 16, "3.8 GHz", "5.3 GHz", "32 MB", 65),
            new CpuSeed("CPU AMD Ryzen 5 7600X", "CPU-AMD-7600X", "AMD", 6590000m, 5990000m, "AM5", 6, 12, "4.7 GHz", "5.3 GHz", "32 MB", 105),
            new CpuSeed("CPU AMD Ryzen 5 7600", "CPU-AMD-7600", "AMD", 5990000m, 5390000m, "AM5", 6, 12, "3.8 GHz", "5.1 GHz", "32 MB", 65),
            new CpuSeed("CPU AMD Ryzen 9 5950X", "CPU-AMD-5950X", "AMD", 11590000m, 10590000m, "AM4", 16, 32, "3.4 GHz", "4.9 GHz", "64 MB", 105),
            new CpuSeed("CPU AMD Ryzen 9 5900X", "CPU-AMD-5900X", "AMD", 8590000m, 7890000m, "AM4", 12, 24, "3.7 GHz", "4.8 GHz", "64 MB", 105),
            new CpuSeed("CPU AMD Ryzen 7 5800X3D", "CPU-AMD-5800X3D", "AMD", 8290000m, 7590000m, "AM4", 8, 16, "3.4 GHz", "4.5 GHz", "96 MB", 105),
            new CpuSeed("CPU AMD Ryzen 7 5800X", "CPU-AMD-5800X", "AMD", 6590000m, 5890000m, "AM4", 8, 16, "3.8 GHz", "4.7 GHz", "32 MB", 105),
            new CpuSeed("CPU AMD Ryzen 7 5700X3D", "CPU-AMD-5700X3D", "AMD", 6490000m, 5790000m, "AM4", 8, 16, "3.0 GHz", "4.1 GHz", "96 MB", 105),
            new CpuSeed("CPU AMD Ryzen 7 5700X", "CPU-AMD-5700X", "AMD", 4990000m, 4490000m, "AM4", 8, 16, "3.4 GHz", "4.6 GHz", "32 MB", 65),
            new CpuSeed("CPU AMD Ryzen 5 5600X", "CPU-AMD-5600X", "AMD", 3990000m, 3490000m, "AM4", 6, 12, "3.7 GHz", "4.6 GHz", "32 MB", 65),
            new CpuSeed("CPU AMD Ryzen 5 5600", "CPU-AMD-5600", "AMD", 3490000m, 2990000m, "AM4", 6, 12, "3.5 GHz", "4.4 GHz", "32 MB", 65),
            new CpuSeed("CPU AMD Ryzen 5 5600G", "CPU-AMD-5600G", "AMD", 3590000m, 3190000m, "AM4", 6, 12, "3.9 GHz", "4.4 GHz", "16 MB", 65),
            new CpuSeed("CPU AMD Ryzen 5 5500", "CPU-AMD-5500", "AMD", 2590000m, 2190000m, "AM4", 6, 12, "3.6 GHz", "4.2 GHz", "16 MB", 65),
            new CpuSeed("CPU AMD Ryzen 3 4100", "CPU-AMD-4100", "AMD", 1590000m, 1390000m, "AM4", 4, 8, "3.8 GHz", "4.0 GHz", "4 MB", 65),
            new CpuSeed("CPU AMD Ryzen 5 8600G", "CPU-AMD-8600G", "AMD", 6590000m, 5990000m, "AM5", 6, 12, "4.3 GHz", "5.0 GHz", "16 MB", 65),
            new CpuSeed("CPU AMD Ryzen 7 8700G", "CPU-AMD-8700G", "AMD", 8990000m, 8290000m, "AM5", 8, 16, "4.2 GHz", "5.1 GHz", "16 MB", 65),
            new CpuSeed("CPU AMD Ryzen 9 7900", "CPU-AMD-7900", "AMD", 10990000m, 9990000m, "AM5", 12, 24, "3.7 GHz", "5.4 GHz", "64 MB", 65),
            new CpuSeed("CPU AMD Ryzen 5 7500F", "CPU-AMD-7500F", "AMD", 4990000m, 4490000m, "AM5", 6, 12, "3.7 GHz", "5.0 GHz", "32 MB", 65),
            new CpuSeed("CPU AMD Threadripper 7970X", "CPU-AMD-7970X", "AMD", 85990000m, 79990000m, "sTR5", 32, 64, "4.0 GHz", "5.3 GHz", "128 MB", 350)
        };

        foreach (var cpu in cpuCatalog)
        {
            var modelKey = GetCpuModelKey(cpu.Name);
            var product = existingProducts.FirstOrDefault(p =>
                string.Equals(p.Sku, cpu.Sku, StringComparison.OrdinalIgnoreCase))
                ?? existingProducts.FirstOrDefault(p =>
                    modelKey.Length > 0 && GetCpuModelKey(p.Name) == modelKey);

            if (product == null)
            {
                product = new Product
                {
                    CategoryId = cpuCategory.Id,
                    StockQuantity = 10
                };
                context.Products.Add(product);
                existingProducts.Add(product);
            }

            product.Name = cpu.Name;
            product.Sku = cpu.Sku;
            product.Brand = cpu.Brand;
            product.Price = cpu.SalePrice;
            product.IsActive = true;
            product.TechnicalSpec ??= new TechnicalSpec();
            product.TechnicalSpec.Socket = cpu.Socket;
            product.TechnicalSpec.TdpWattage = cpu.TdpWattage;
            product.TechnicalSpec.AdditionalSpecsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                coreCount = cpu.CoreCount,
                threadCount = cpu.ThreadCount,
                baseClock = cpu.BaseClock,
                boostClock = cpu.BoostClock,
                cache = cpu.Cache,
                regularPrice = cpu.RegularPrice
            });
        }

        await context.SaveChangesAsync();
    }

    private static string GetCpuModelKey(string name)
    {
        var match = Regex.Match(name,
            @"(?:i[3579]\s*-?\s*\d{4,5}[a-z0-9]*|ryzen\s+[3579]\s+\d{4,5}[a-z0-9]*|threadripper\s+\d{4,5}[a-z0-9]*)",
            RegexOptions.IgnoreCase);
        return Regex.Replace(match.Value, @"[^a-z0-9]", string.Empty, RegexOptions.IgnoreCase);
    }

    private sealed record CpuSeed(
        string Name,
        string Sku,
        string Brand,
        decimal RegularPrice,
        decimal SalePrice,
        string Socket,
        int CoreCount,
        int ThreadCount,
        string BaseClock,
        string BoostClock,
        string Cache,
        int TdpWattage);
}