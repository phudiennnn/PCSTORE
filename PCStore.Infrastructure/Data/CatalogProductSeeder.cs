using Microsoft.EntityFrameworkCore;
using PCStore.Core.Entities;
using PCStore.Core.Enums;
using System.Text.Json;

namespace PCStore.Infrastructure.Data;

public static class CatalogProductSeeder
{
    public static async Task SeedAsync(AppDbContext context)
    {
        var categories = await context.Categories.ToDictionaryAsync(c => c.ComponentType);
        await SeedMainboardsAsync(context, categories[ComponentType.Mainboard]);
        await SeedGraphicsCardsAsync(context, categories[ComponentType.GPU]);
        await MemoryStorageCatalogSeeder.SeedAsync(context, categories);
        await PowerCaseCoolingCatalogSeeder.SeedAsync(context, categories);
    }

    private static async Task SeedMainboardsAsync(AppDbContext context, Category category)
    {
        var catalog = new[]
        {
            new MainboardSeed("Mainboard ASUS ROG MAXIMUS Z790 HERO", "MB-ASU-Z790H", "ASUS", 15990000m, 15500000m, "LGA 1700", "Z790", "DDR5", 4, 192, "ATX", 5, "Mainboard Z790 cao cấp từ ASUS."),
            new MainboardSeed("Mainboard GIGABYTE Z790 AORUS MASTER", "MB-GIG-Z790AM", "Gigabyte", 14590000m, 13900000m, "LGA 1700", "Z790", "DDR5", 4, 192, "E-ATX", 5, "Dành cho vi xử lý Intel thế hệ 13, 14."),
            new MainboardSeed("Mainboard MSI MEG Z790 GODLIKE", "MB-MSI-Z790G", "MSI", 29990000m, 28990000m, "LGA 1700", "Z790", "DDR5", 4, 192, "E-ATX", 7, "Đỉnh cao mainboard MSI."),
            new MainboardSeed("Mainboard ASRock Z790 Taichi", "MB-ASR-Z790T", "ASRock", 12590000m, 11990000m, "LGA 1700", "Z790", "DDR5", 4, 192, "E-ATX", 5, "Thiết kế bánh răng đặc trưng."),
            new MainboardSeed("Mainboard ASUS ROG STRIX Z790-E GAMING WIFI", "MB-ASU-Z790E", "ASUS", 11990000m, 11490000m, "LGA 1700", "Z790", "DDR5", 4, 192, "ATX", 5, "Lựa chọn hàng đầu cho game thủ."),
            new MainboardSeed("Mainboard GIGABYTE Z790 AORUS ELITE AX", "MB-GIG-Z790AE", "Gigabyte", 7590000m, 7190000m, "LGA 1700", "Z790", "DDR5", 4, 192, "ATX", 4, "Hiệu năng trên giá thành tốt."),
            new MainboardSeed("Mainboard MSI MAG Z790 TOMAHAWK WIFI", "MB-MSI-Z790T", "MSI", 7890000m, 7490000m, "LGA 1700", "Z790", "DDR5", 4, 192, "ATX", 4, "Bền bỉ, tản nhiệt tốt."),
            new MainboardSeed("Mainboard ASUS TUF GAMING Z790-PLUS WIFI", "MB-ASU-Z790TUF", "ASUS", 7290000m, 6890000m, "LGA 1700", "Z790", "DDR5", 4, 192, "ATX", 4, "Chuẩn độ bền quân đội."),
            new MainboardSeed("Mainboard ASRock Z790 PRO RS", "MB-ASR-Z790PRS", "ASRock", 5590000m, 5290000m, "LGA 1700", "Z790", "DDR5", 4, 192, "ATX", 4, "Lựa chọn Z790 giá rẻ."),
            new MainboardSeed("Mainboard ASUS PRIME Z790-A WIFI", "MB-ASU-Z790PA", "ASUS", 6990000m, 6590000m, "LGA 1700", "Z790", "DDR5", 4, 192, "ATX", 4, "Thiết kế trắng bạc thanh lịch."),
            new MainboardSeed("Mainboard ASUS ROG STRIX B760-F GAMING WIFI", "MB-ASU-B760F", "ASUS", 6290000m, 5890000m, "LGA 1700", "B760", "DDR5", 4, 192, "ATX", 3, "Mainboard B760 phân khúc cận cao cấp."),
            new MainboardSeed("Mainboard GIGABYTE B760 AORUS ELITE AX", "MB-GIG-B760AE", "Gigabyte", 4890000m, 4590000m, "LGA 1700", "B760", "DDR5", 4, 192, "ATX", 3, "Bo mạch chủ tầm trung tốt nhất."),
            new MainboardSeed("Mainboard MSI MAG B760M MORTAR WIFI", "MB-MSI-B760MM", "MSI", 4590000m, 4290000m, "LGA 1700", "B760", "DDR5", 4, 192, "Micro-ATX", 2, "Chuẩn mATX nhỏ gọn mạnh mẽ."),
            new MainboardSeed("Mainboard ASUS TUF GAMING B760M-PLUS WIFI", "MB-ASU-B760MT", "ASUS", 4290000m, 3990000m, "LGA 1700", "B760", "DDR5", 4, 192, "Micro-ATX", 2, "Bền bỉ cho máy trạm mini."),
            new MainboardSeed("Mainboard ASRock B760M Steel Legend WiFi", "MB-ASR-B760MS", "ASRock", 3990000m, 3690000m, "LGA 1700", "B760", "DDR5", 4, 192, "Micro-ATX", 3, "Tông màu trắng bạc bắt mắt."),
            new MainboardSeed("Mainboard GIGABYTE B760M DS3H AX", "MB-GIG-B760MD", "Gigabyte", 3290000m, 2990000m, "LGA 1700", "B760", "DDR5", 4, 192, "Micro-ATX", 2, "Giá rẻ, có sẵn WiFi 6E."),
            new MainboardSeed("Mainboard MSI PRO B760M-A WIFI", "MB-MSI-B760MP", "MSI", 3490000m, 3190000m, "LGA 1700", "B760", "DDR5", 4, 192, "Micro-ATX", 2, "Dòng PRO tối ưu cho doanh nghiệp."),
            new MainboardSeed("Mainboard ASUS PRIME B760M-K", "MB-ASU-B760MK", "ASUS", 2590000m, 2390000m, "LGA 1700", "B760", "DDR4", 2, 64, "Micro-ATX", 2, "Bo mạch chủ B760 cơ bản nhất."),
            new MainboardSeed("Mainboard ASRock B760M-HDV/M.2", "MB-ASR-B760MH", "ASRock", 2390000m, 2190000m, "LGA 1700", "B760", "DDR4", 2, 64, "Micro-ATX", 2, "Giải pháp cực rẻ cho i3, i5."),
            new MainboardSeed("Mainboard MSI PRO H610M-E DDR4", "MB-MSI-H610ME", "MSI", 1790000m, 1650000m, "LGA 1700", "H610", "DDR4", 2, 64, "Micro-ATX", 1, "Mainboard H610 phổ thông giá rẻ."),
            new MainboardSeed("Mainboard ASUS PRIME H610M-K D4", "MB-ASU-H610MK", "ASUS", 1890000m, 1690000m, "LGA 1700", "H610", "DDR4", 2, 64, "Micro-ATX", 1, "Chất lượng linh kiện tiêu chuẩn."),
            new MainboardSeed("Mainboard GIGABYTE H610M S2H V2", "MB-GIG-H610MS", "Gigabyte", 1990000m, 1790000m, "LGA 1700", "H610", "DDR4", 2, 64, "Micro-ATX", 1, "Hỗ trợ xuất hình nhiều cổng."),
            new MainboardSeed("Mainboard ASRock H610M-HVS/M.2 R2.0", "MB-ASR-H610MH", "ASRock", 1690000m, 1550000m, "LGA 1700", "H610", "DDR4", 2, 64, "Micro-ATX", 0, "Siêu tiết kiệm cho văn phòng."),
            new MainboardSeed("Mainboard ASUS ROG CROSSHAIR X670E HERO", "MB-ASU-X670EH", "ASUS", 16990000m, 16490000m, "AM5", "X670E", "DDR5", 4, 192, "ATX", 5, "Đẳng cấp cho AMD Ryzen 7000."),
            new MainboardSeed("Mainboard GIGABYTE X670E AORUS MASTER", "MB-GIG-X670EM", "Gigabyte", 13990000m, 13490000m, "AM5", "X670E", "DDR5", 4, 192, "E-ATX", 4, "Bo mạch E-ATX siêu khủng."),
            new MainboardSeed("Mainboard MSI MEG X670E ACE", "MB-MSI-X670EA", "MSI", 18990000m, 18490000m, "AM5", "X670E", "DDR5", 4, 192, "E-ATX", 4, "Nền tảng vàng cho AMD."),
            new MainboardSeed("Mainboard ASRock X670E Taichi", "MB-ASR-X670ET", "ASRock", 12990000m, 12490000m, "AM5", "X670E", "DDR5", 4, 192, "E-ATX", 4, "Thiết kế đẹp, phase nguồn mạnh."),
            new MainboardSeed("Mainboard ASUS ROG STRIX X670E-E GAMING WIFI", "MB-ASU-X670EE", "ASUS", 11990000m, 11490000m, "AM5", "X670E", "DDR5", 4, 192, "ATX", 4, "Hoàn hảo cho Ryzen 9."),
            new MainboardSeed("Mainboard MSI MPG X670E CARBON WIFI", "MB-MSI-X670EC", "MSI", 10990000m, 10490000m, "AM5", "X670E", "DDR5", 4, 192, "ATX", 4, "Thiết kế carbon huyền bí."),
            new MainboardSeed("Mainboard GIGABYTE X670 AORUS ELITE AX", "MB-GIG-X670AE", "Gigabyte", 7990000m, 7490000m, "AM5", "X670", "DDR5", 4, 192, "ATX", 4, "Khởi đầu tốt với chipset X670."),
            new MainboardSeed("Mainboard ASUS TUF GAMING X670E-PLUS WIFI", "MB-ASU-X670TP", "ASUS", 7890000m, 7490000m, "AM5", "X670E", "DDR5", 4, 192, "ATX", 4, "Mainboard AM5 siêu bền bỉ."),
            new MainboardSeed("Mainboard ASRock X670E Pro RS", "MB-ASR-X670EP", "ASRock", 7590000m, 7190000m, "AM5", "X670E", "DDR5", 4, 192, "ATX", 4, "Mainboard X670E giá rẻ hiếm hoi."),
            new MainboardSeed("Mainboard ASUS ROG STRIX B650E-F GAMING WIFI", "MB-ASU-B650EF", "ASUS", 7290000m, 6890000m, "AM5", "B650E", "DDR5", 4, 192, "ATX", 3, "Sức mạnh cực hạn tầm trung AM5."),
            new MainboardSeed("Mainboard GIGABYTE B650 AORUS ELITE AX ICE", "MB-GIG-B650AI", "Gigabyte", 6590000m, 6190000m, "AM5", "B650", "DDR5", 4, 192, "ATX", 3, "Thiết kế trắng tinh khôi."),
            new MainboardSeed("Mainboard MSI MAG B650 TOMAHAWK WIFI", "MB-MSI-B650T", "MSI", 5990000m, 5690000m, "AM5", "B650", "DDR5", 4, 192, "ATX", 3, "Chuẩn mực B650 cho game thủ."),
            new MainboardSeed("Mainboard ASRock B650E Steel Legend WiFi", "MB-ASR-B650ES", "ASRock", 6290000m, 5890000m, "AM5", "B650E", "DDR5", 4, 192, "Micro-ATX", 3, "Tản nhiệt cực đỉnh, PCIe 5.0."),
            new MainboardSeed("Mainboard ASUS TUF GAMING B650M-PLUS WIFI", "MB-ASU-B650MP", "ASUS", 5190000m, 4890000m, "AM5", "B650", "DDR5", 4, 192, "Micro-ATX", 2, "M-ATX mạnh mẽ cho Ryzen 7000."),
            new MainboardSeed("Mainboard GIGABYTE B650M AORUS ELITE AX", "MB-GIG-B650MA", "Gigabyte", 4890000m, 4590000m, "AM5", "B650", "DDR5", 4, 192, "Micro-ATX", 2, "Nhỏ gọn, thiết kế gaming."),
            new MainboardSeed("Mainboard MSI MAG B650M MORTAR WIFI", "MB-MSI-B650MM", "MSI", 4990000m, 4690000m, "AM5", "B650", "DDR5", 4, 192, "Micro-ATX", 2, "Chất lượng linh kiện hạng nặng."),
            new MainboardSeed("Mainboard ASRock B650M Pro RS WiFi", "MB-ASR-B650MR", "ASRock", 3990000m, 3690000m, "AM5", "B650", "DDR5", 4, 192, "Micro-ATX", 3, "Best choice B650M giá rẻ."),
            new MainboardSeed("Mainboard ASUS PRIME B650M-A WIFI", "MB-ASU-B650MA", "ASUS", 4290000m, 3990000m, "AM5", "B650", "DDR5", 4, 192, "Micro-ATX", 2, "Phục vụ đa nhu cầu cơ bản."),
            new MainboardSeed("Mainboard MSI PRO B650M-P", "MB-MSI-B650MP", "MSI", 3290000m, 2990000m, "AM5", "B650", "DDR5", 4, 192, "Micro-ATX", 2, "Làm việc ổn định lâu dài."),
            new MainboardSeed("Mainboard GIGABYTE A620M GAMING X", "MB-GIG-A620MG", "Gigabyte", 2990000m, 2790000m, "AM5", "A620", "DDR5", 4, 192, "Micro-ATX", 1, "Dòng A620 mạnh nhất của GIGABYTE."),
            new MainboardSeed("Mainboard ASUS TUF GAMING A620M-PLUS WIFI", "MB-ASU-A620MT", "ASUS", 3190000m, 2890000m, "AM5", "A620", "DDR5", 4, 192, "Micro-ATX", 2, "TUF series siêu bền cho A620."),
            new MainboardSeed("Mainboard MSI PRO A620M-E", "MB-MSI-A620ME", "MSI", 2190000m, 1990000m, "AM5", "A620", "DDR5", 2, 96, "Micro-ATX", 1, "A620 phổ thông siêu rẻ."),
            new MainboardSeed("Mainboard ASRock A620M-HDV/M.2+", "MB-ASR-A620MH", "ASRock", 2090000m, 1890000m, "AM5", "A620", "DDR5", 2, 96, "Micro-ATX", 1, "Dành cho Ryzen 5 7600 tiết kiệm."),
            new MainboardSeed("Mainboard MSI B450 TOMAHAWK MAX II", "MB-MSI-B450T", "MSI", 2290000m, 2090000m, "AM4", "B450", "DDR4", 4, 128, "ATX", 1, "Bo mạch huyền thoại AM4."),
            new MainboardSeed("Mainboard ASUS TUF GAMING B450M-PRO II", "MB-ASU-B450M", "ASUS", 2190000m, 1990000m, "AM4", "B450", "DDR4", 4, 128, "Micro-ATX", 2, "Hỗ trợ Ryzen 5000 cực tốt."),
            new MainboardSeed("Mainboard GIGABYTE B450M DS3H V2", "MB-GIG-B450MD", "Gigabyte", 1590000m, 1490000m, "AM4", "B450", "DDR4", 4, 128, "Micro-ATX", 1, "Giải pháp nâng cấp cũ giá rẻ."),
            new MainboardSeed("Mainboard ASRock A320M-HDV R4.0", "MB-ASR-A320M", "ASRock", 1190000m, 1090000m, "AM4", "A320", "DDR4", 2, 32, "Micro-ATX", 1, "Mainboard AM4 rẻ nhất thị trường.")
        };

        var products = await context.Products
            .Include(p => p.TechnicalSpec)
            .Where(p => p.CategoryId == category.Id)
            .ToListAsync();

        foreach (var item in catalog)
        {
            var product = FindOrCreateProduct(context, products, category.Id, item.Name, item.Sku);
            product.Name = item.Name;
            product.Sku = item.Sku;
            product.Brand = item.Brand;
            product.Price = item.SalePrice;
            product.IsActive = true;
            product.TechnicalSpec ??= new TechnicalSpec();
            product.TechnicalSpec.Socket = item.Socket;
            product.TechnicalSpec.Chipset = item.Chipset;
            product.TechnicalSpec.RamType = item.MemoryType;
            product.TechnicalSpec.RamSlots = item.MemorySlots;
            product.TechnicalSpec.FormFactor = item.FormFactor;
            product.TechnicalSpec.AdditionalSpecsJson = JsonSerializer.Serialize(new
            {
                maxMemoryGb = item.MaxMemoryGb,
                m2Slots = item.M2Slots,
                regularPrice = item.RegularPrice,
                description = item.Description
            });
        }

        await context.SaveChangesAsync();
    }

    private static async Task SeedGraphicsCardsAsync(AppDbContext context, Category category)
    {
        var catalog = new[]
        {
            new GraphicsCardSeed("VGA ASUS ROG Strix GeForce RTX 4090 24GB", "VGA-ASU-4090R", "ASUS", 65990000m, 64990000m, 24, "GDDR6X", 357, 450, 1000, "PCIe 4.0 x16", "Vua hiệu năng đồ họa."),
            new GraphicsCardSeed("VGA GIGABYTE AORUS GeForce RTX 4090 MASTER", "VGA-GIG-4090M", "Gigabyte", 63990000m, 62990000m, 24, "GDDR6X", 358, 450, 1000, "PCIe 4.0 x16", "Tản nhiệt bionic shark độc quyền."),
            new GraphicsCardSeed("VGA MSI SUPRIM X GeForce RTX 4090 24GB", "VGA-MSI-4090S", "MSI", 64990000m, 63990000m, 24, "GDDR6X", 336, 450, 1000, "PCIe 4.0 x16", "Thiết kế kim loại xước sang trọng."),
            new GraphicsCardSeed("VGA ASUS TUF Gaming GeForce RTX 4090 24GB", "VGA-ASU-4090T", "ASUS", 58990000m, 57990000m, 24, "GDDR6X", 348, 450, 850, "PCIe 4.0 x16", "Độ bền chuẩn quân sự."),
            new GraphicsCardSeed("VGA GIGABYTE GeForce RTX 4090 GAMING OC", "VGA-GIG-4090G", "Gigabyte", 55990000m, 54990000m, 24, "GDDR6X", 340, 450, 850, "PCIe 4.0 x16", "Hiệu năng mạnh, giá hợp lý."),
            new GraphicsCardSeed("VGA ASUS ROG Strix GeForce RTX 4080 SUPER", "VGA-ASU-4080SR", "ASUS", 38990000m, 37990000m, 16, "GDDR6X", 357, 320, 850, "PCIe 4.0 x16", "Bản nâng cấp SUPER đáng giá."),
            new GraphicsCardSeed("VGA GIGABYTE AORUS GeForce RTX 4080 SUPER MASTER", "VGA-GIG-4080SM", "Gigabyte", 36990000m, 35990000m, 16, "GDDR6X", 357, 320, 850, "PCIe 4.0 x16", "Màn hình LCD edge-view."),
            new GraphicsCardSeed("VGA MSI GeForce RTX 4080 SUPER 16G GAMING X SLIM", "VGA-MSI-4080SX", "MSI", 34990000m, 33990000m, 16, "GDDR6X", 322, 320, 850, "PCIe 4.0 x16", "Thiết kế mỏng nhưng vẫn rất mát."),
            new GraphicsCardSeed("VGA ASUS ProArt GeForce RTX 4080 SUPER", "VGA-ASU-4080SP", "ASUS", 33990000m, 32990000m, 16, "GDDR6X", 300, 320, 850, "PCIe 4.0 x16", "Tối giản, tinh tế cho creator."),
            new GraphicsCardSeed("VGA GIGABYTE GeForce RTX 4080 SUPER AERO OC", "VGA-GIG-4080SA", "Gigabyte", 35990000m, 34990000m, 16, "GDDR6X", 342, 320, 850, "PCIe 4.0 x16", "Phiên bản trắng toàn diện."),
            new GraphicsCardSeed("VGA ASUS ROG Strix GeForce RTX 4070 Ti SUPER", "VGA-ASU-4070TSR", "ASUS", 28990000m, 27990000m, 16, "GDDR6X", 336, 285, 750, "PCIe 4.0 x16", "Card đồ họa 16GB VRAM mạnh mẽ."),
            new GraphicsCardSeed("VGA GIGABYTE GeForce RTX 4070 Ti SUPER GAMING OC", "VGA-GIG-4070TISG", "Gigabyte", 25990000m, 24990000m, 16, "GDDR6X", 300, 285, 750, "PCIe 4.0 x16", "Card quốc dân cho màn hình 2K."),
            new GraphicsCardSeed("VGA MSI GeForce RTX 4070 Ti SUPER VENTUS 3X", "VGA-MSI-4070TISV", "MSI", 24990000m, 23990000m, 16, "GDDR6X", 308, 285, 750, "PCIe 4.0 x16", "Phiên bản Ventus bền bỉ 3 quạt."),
            new GraphicsCardSeed("VGA ASUS TUF Gaming GeForce RTX 4070 Ti SUPER", "VGA-ASU-4070TIST", "ASUS", 26590000m, 25590000m, 16, "GDDR6X", 305, 285, 750, "PCIe 4.0 x16", "Nhiệt độ luôn ở mức lý tưởng."),
            new GraphicsCardSeed("VGA ASRock Radeon RX 7900 XTX Taichi 24GB", "VGA-ASR-7900XTX", "ASRock", 31990000m, 30990000m, 24, "GDDR6", 345, 355, 1000, "PCIe 4.0 x16", "Quái vật đỏ từ đội AMD."),
            new GraphicsCardSeed("VGA GIGABYTE Radeon RX 7900 XTX GAMING OC", "VGA-GIG-7900XTX", "Gigabyte", 29990000m, 28990000m, 24, "GDDR6", 340, 355, 850, "PCIe 4.0 x16", "Cạnh tranh trực tiếp RTX 4080."),
            new GraphicsCardSeed("VGA ASUS TUF Gaming Radeon RX 7900 XTX", "VGA-ASU-7900XTX", "ASUS", 30590000m, 29590000m, 24, "GDDR6", 352, 355, 850, "PCIe 4.0 x16", "Hiệu năng rasterization xuất sắc."),
            new GraphicsCardSeed("VGA MSI GeForce RTX 4070 SUPER 12G GAMING X SLIM", "VGA-MSI-4070S", "MSI", 20590000m, 19590000m, 12, "GDDR6X", 307, 220, 650, "PCIe 4.0 x16", "Lựa chọn cân bằng cho game 2K."),
            new GraphicsCardSeed("VGA ASUS Dual GeForce RTX 4070 SUPER", "VGA-ASU-4070SD", "ASUS", 18990000m, 18290000m, 12, "GDDR6X", 267, 220, 650, "PCIe 4.0 x16", "2 quạt nhỏ gọn, vừa vặn mọi case."),
            new GraphicsCardSeed("VGA GIGABYTE GeForce RTX 4070 SUPER AERO OC", "VGA-GIG-4070SA", "Gigabyte", 19990000m, 19190000m, 12, "GDDR6X", 261, 220, 650, "PCIe 4.0 x16", "Phiên bản trắng đẹp tinh tế."),
            new GraphicsCardSeed("VGA MSI GeForce RTX 4070 VENTUS 2X 12G OC", "VGA-MSI-4070V2", "MSI", 15990000m, 15490000m, 12, "GDDR6X", 242, 200, 650, "PCIe 4.0 x16", "Lựa chọn RTX 4070 giá cực mềm."),
            new GraphicsCardSeed("VGA ASUS Dual GeForce RTX 4070 12GB", "VGA-ASU-4070D", "ASUS", 16490000m, 15990000m, 12, "GDDR6X", 267, 200, 650, "PCIe 4.0 x16", "Công nghệ DLSS 3 đột phá."),
            new GraphicsCardSeed("VGA ASRock Radeon RX 7800 XT Steel Legend 16GB", "VGA-ASR-7800XT", "ASRock", 15590000m, 14990000m, 16, "GDDR6", 304, 263, 750, "PCIe 4.0 x16", "16GB VRAM thoả sức chiến game."),
            new GraphicsCardSeed("VGA GIGABYTE Radeon RX 7800 XT GAMING OC", "VGA-GIG-7800XT", "Gigabyte", 14990000m, 14490000m, 16, "GDDR6", 302, 263, 700, "PCIe 4.0 x16", "Sự trở lại mạnh mẽ phân khúc trung cấp."),
            new GraphicsCardSeed("VGA ASUS Dual GeForce RTX 4060 Ti 16GB", "VGA-ASU-4060TI16", "ASUS", 14590000m, 13990000m, 16, "GDDR6", 227, 165, 550, "PCIe 4.0 x8", "Bản 16GB cho ai cần làm AI nhẹ."),
            new GraphicsCardSeed("VGA MSI GeForce RTX 4060 Ti GAMING X 8G", "VGA-MSI-4060TIG", "MSI", 12590000m, 11990000m, 8, "GDDR6", 247, 160, 550, "PCIe 4.0 x8", "VGA quốc dân độ phân giải FHD."),
            new GraphicsCardSeed("VGA GIGABYTE GeForce RTX 4060 Ti EAGLE OC 8G", "VGA-GIG-4060TIE", "Gigabyte", 11990000m, 11490000m, 8, "GDDR6", 272, 160, 500, "PCIe 4.0 x8", "Ba quạt mát mẻ."),
            new GraphicsCardSeed("VGA ASUS TUF Gaming GeForce RTX 4060 Ti 8GB", "VGA-ASU-4060TIT", "ASUS", 12990000m, 12490000m, 8, "GDDR6", 300, 160, 650, "PCIe 4.0 x8", "Bọc nhôm toàn khối cứng cáp."),
            new GraphicsCardSeed("VGA ASRock Radeon RX 7700 XT Challenger 12GB", "VGA-ASR-7700XT", "ASRock", 12290000m, 11790000m, 12, "GDDR6", 266, 245, 700, "PCIe 4.0 x16", "P/P siêu tốt so với 4060Ti."),
            new GraphicsCardSeed("VGA GIGABYTE Radeon RX 7700 XT GAMING OC", "VGA-GIG-7700XTG", "Gigabyte", 12590000m, 11990000m, 12, "GDDR6", 302, 245, 700, "PCIe 4.0 x16", "12GB VRAM yên tâm xài lâu dài."),
            new GraphicsCardSeed("VGA ASUS ROG Strix GeForce RTX 4060 8GB", "VGA-ASU-4060R", "ASUS", 10990000m, 10490000m, 8, "GDDR6", 311, 115, 550, "PCIe 4.0 x8", "RTX 4060 cao cấp nhất."),
            new GraphicsCardSeed("VGA MSI GeForce RTX 4060 VENTUS 2X BLACK 8G", "VGA-MSI-4060V2", "MSI", 8590000m, 8190000m, 8, "GDDR6", 199, 115, 550, "PCIe 4.0 x8", "Card đồ họa bán chạy nhất 2024."),
            new GraphicsCardSeed("VGA GIGABYTE GeForce RTX 4060 WINDFORCE OC 8G", "VGA-GIG-4060W", "Gigabyte", 8490000m, 8090000m, 8, "GDDR6", 192, 115, 450, "PCIe 4.0 x8", "Nâng cấp lý tưởng từ GTX 1060/1660."),
            new GraphicsCardSeed("VGA ASUS Dual GeForce RTX 4060 White OC", "VGA-ASU-4060DW", "ASUS", 8990000m, 8590000m, 8, "GDDR6", 227, 115, 550, "PCIe 4.0 x8", "Card trắng build PC thẩm mỹ."),
            new GraphicsCardSeed("VGA ASRock Radeon RX 7600 Steel Legend 8GB", "VGA-ASR-7600S", "ASRock", 8190000m, 7790000m, 8, "GDDR6", 269, 165, 550, "PCIe 4.0 x8", "Radeon 7000 series giá cực rẻ."),
            new GraphicsCardSeed("VGA GIGABYTE Radeon RX 7600 GAMING OC 8G", "VGA-GIG-7600G", "Gigabyte", 7990000m, 7590000m, 8, "GDDR6", 282, 165, 550, "PCIe 4.0 x8", "Hiệu năng mạnh hơn RTX 3060."),
            new GraphicsCardSeed("VGA MSI GeForce RTX 3060 VENTUS 2X 12G", "VGA-MSI-3060V12", "MSI", 7590000m, 7190000m, 12, "GDDR6", 235, 170, 550, "PCIe 4.0 x16", "12GB VRAM làm việc ổn áp."),
            new GraphicsCardSeed("VGA ASUS Dual GeForce RTX 3060 12GB", "VGA-ASU-3060D12", "ASUS", 7690000m, 7290000m, 12, "GDDR6", 200, 170, 650, "PCIe 4.0 x16", "Huyền thoại vẫn còn sản xuất."),
            new GraphicsCardSeed("VGA GIGABYTE GeForce RTX 3050 EAGLE OC 6G", "VGA-GIG-3050E6", "Gigabyte", 5190000m, 4890000m, 6, "GDDR6", 192, 70, 300, "PCIe 4.0 x8", "VGA không cần nguồn phụ cực tiện."),
            new GraphicsCardSeed("VGA MSI GeForce RTX 3050 LP 6G OC", "VGA-MSI-3050LP", "MSI", 4990000m, 4690000m, 6, "GDDR6", 174, 70, 300, "PCIe 4.0 x8", "Dành cho các case HTPC mỏng nhỏ."),
            new GraphicsCardSeed("VGA ASUS Phoenix GeForce GTX 1650 4GB D6", "VGA-ASU-1650P", "ASUS", 3590000m, 3290000m, 4, "GDDR6", 174, 75, 300, "PCIe 3.0 x16", "Lựa chọn chữa cháy chơi game Esports."),
            new GraphicsCardSeed("VGA MSI GeForce GTX 1650 D6 VENTUS XS", "VGA-MSI-1650V", "MSI", 3690000m, 3390000m, 4, "GDDR6", 177, 75, 300, "PCIe 3.0 x16", "VGA quốc dân phòng net một thời."),
            new GraphicsCardSeed("VGA GIGABYTE GeForce GTX 1650 D6 OC 4G", "VGA-GIG-1650O", "Gigabyte", 3790000m, 3490000m, 4, "GDDR6", 191, 75, 300, "PCIe 3.0 x16", "2 quạt giá siêu hạt dẻ."),
            new GraphicsCardSeed("VGA ASRock Radeon RX 6600 Challenger D 8GB", "VGA-ASR-6600C", "ASRock", 5690000m, 5290000m, 8, "GDDR6", 269, 132, 500, "PCIe 4.0 x8", "Vô địch P/P tầm giá 5-6 triệu."),
            new GraphicsCardSeed("VGA MSI Radeon RX 6600 MECH 2X 8G", "VGA-MSI-6600M", "MSI", 5790000m, 5390000m, 8, "GDDR6", 235, 132, 500, "PCIe 4.0 x8", "Lựa chọn tối ưu chơi PUBG, CS2."),
            new GraphicsCardSeed("VGA GIGABYTE Radeon RX 6700 XT EAGLE 12G", "VGA-GIG-6700XT", "Gigabyte", 8590000m, 8190000m, 12, "GDDR6", 282, 230, 650, "PCIe 4.0 x16", "Sức mạnh ngang ngửa RTX 3060 Ti."),
            new GraphicsCardSeed("VGA ASUS Dual Radeon RX 6700 XT 12GB", "VGA-ASU-6700XT", "ASUS", 8690000m, 8290000m, 12, "GDDR6", 295, 230, 650, "PCIe 4.0 x16", "Hiệu năng làm việc và giải trí ấn tượng."),
            new GraphicsCardSeed("VGA NVIDIA Quadro T400 4GB", "VGA-NVI-T400", "NVIDIA", 4290000m, 3990000m, 4, "GDDR6", 156, 30, 200, "PCIe 3.0 x16", "Card đồ họa chuyên dụng giá rẻ."),
            new GraphicsCardSeed("VGA NVIDIA Quadro T1000 8GB", "VGA-NVI-T1000", "NVIDIA", 9590000m, 9190000m, 8, "GDDR6", 156, 50, 300, "PCIe 3.0 x16", "Xử lý CAD 2D/3D mượt mà."),
            new GraphicsCardSeed("VGA NVIDIA RTX A2000 12GB", "VGA-NVI-A2000", "NVIDIA", 15990000m, 15490000m, 12, "GDDR6", 167, 70, 300, "PCIe 4.0 x16", "Card đồ họa AI và Workstation nhỏ gọn.")
        };

        var products = await context.Products
            .Include(p => p.TechnicalSpec)
            .Where(p => p.CategoryId == category.Id)
            .ToListAsync();

        foreach (var item in catalog)
        {
            var product = FindOrCreateProduct(context, products, category.Id, item.Name, item.Sku);
            product.Name = item.Name;
            product.Sku = item.Sku;
            product.Brand = item.Brand;
            product.Price = item.SalePrice;
            product.IsActive = true;
            product.TechnicalSpec ??= new TechnicalSpec();
            product.TechnicalSpec.RamType = item.MemoryType;
            product.TechnicalSpec.TdpWattage = item.PowerWatt;
            product.TechnicalSpec.RecommendedPsu = item.RecommendedPsuWatt;
            product.TechnicalSpec.LengthMm = item.LengthMm;
            product.TechnicalSpec.AdditionalSpecsJson = JsonSerializer.Serialize(new
            {
                vramGb = item.VramGb,
                interfaceType = item.Interface,
                regularPrice = item.RegularPrice,
                description = item.Description
            });
        }

        await context.SaveChangesAsync();
    }

    private static Product FindOrCreateProduct(
        AppDbContext context,
        List<Product> existingProducts,
        int categoryId,
        string name,
        string sku)
    {
        var normalizedName = NormalizeName(name);
        var product = existingProducts.FirstOrDefault(p =>
            string.Equals(p.Sku, sku, StringComparison.OrdinalIgnoreCase))
            ?? existingProducts.FirstOrDefault(p =>
            {
                var existingName = NormalizeName(p.Name);
                return existingName.Contains(normalizedName) || normalizedName.Contains(existingName);
            });

        if (product != null)
            return product;

        product = new Product
        {
            CategoryId = categoryId,
            Name = name,
            Sku = sku,
            StockQuantity = 10
        };
        context.Products.Add(product);
        existingProducts.Add(product);
        return product;
    }

    private static string NormalizeName(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();

    private sealed record MainboardSeed(
        string Name, string Sku, string Brand, decimal RegularPrice, decimal SalePrice,
        string Socket, string Chipset, string MemoryType, int MemorySlots, int MaxMemoryGb,
        string FormFactor, int M2Slots, string Description);

    private sealed record GraphicsCardSeed(
        string Name, string Sku, string Brand, decimal RegularPrice, decimal SalePrice,
        int VramGb, string MemoryType, int LengthMm, int PowerWatt, int RecommendedPsuWatt,
        string Interface, string Description);
}
