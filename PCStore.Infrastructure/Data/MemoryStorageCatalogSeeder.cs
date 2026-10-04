using Microsoft.EntityFrameworkCore;
using PCStore.Core.Entities;
using PCStore.Core.Enums;
using System.Text.Json;

namespace PCStore.Infrastructure.Data;

public static class MemoryStorageCatalogSeeder
{
    public static async Task SeedAsync(AppDbContext context, IReadOnlyDictionary<ComponentType, Category> categories)
    {
        var categoryMap = categories.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (!categoryMap.ContainsKey(ComponentType.HDD))
        {
            var hddCategory = new Category
            {
                Name = "Ổ cứng HDD",
                ComponentType = ComponentType.HDD,
                Description = "Ổ cứng HDD SATA và ổ lưu trữ dung lượng lớn"
            };
            context.Categories.Add(hddCategory);
            await context.SaveChangesAsync();
            categoryMap.Add(ComponentType.HDD, hddCategory);
        }

        await SeedRamAsync(context, categoryMap[ComponentType.RAM]);
        await SeedStorageAsync(context, categoryMap);
    }

    private static async Task SeedRamAsync(AppDbContext context, Category category)
    {
        var catalog = new[]
        {
            new RamSeed("RAM Corsair Dominator Titanium RGB 64GB (2x32GB) DDR5 6000MHz", "RAM-COR-DTI64", "Corsair", 8590000m, 8290000m, "DDR5", 64, 6000, 2, 1.35m, "RAM cao cấp nhất của Corsair."),
            new RamSeed("RAM Corsair Dominator Platinum RGB 32GB (2x16GB) DDR5 6200MHz", "RAM-COR-DPL32", "Corsair", 4990000m, 4790000m, "DDR5", 32, 6200, 2, 1.35m, "Thiết kế nhôm cao cấp."),
            new RamSeed("RAM Corsair Vengeance RGB 32GB (2x16GB) DDR5 6000MHz", "RAM-COR-VRG32", "Corsair", 3590000m, 3390000m, "DDR5", 32, 6000, 2, 1.35m, "Tối ưu cho AMD EXPO và Intel XMP."),
            new RamSeed("RAM Corsair Vengeance 32GB (2x16GB) DDR5 5600MHz", "RAM-COR-VEN32", "Corsair", 2990000m, 2790000m, "DDR5", 32, 5600, 2, 1.25m, "Không LED, tản nhiệt nhôm đen."),
            new RamSeed("RAM Corsair Vengeance RGB Pro 32GB (2x16GB) DDR4 3600MHz", "RAM-COR-VRP32", "Corsair", 2490000m, 2290000m, "DDR4", 32, 3600, 2, 1.35m, "LED RGB rực rỡ."),
            new RamSeed("RAM Corsair Vengeance LPX 32GB (2x16GB) DDR4 3200MHz", "RAM-COR-VLX32", "Corsair", 1890000m, 1790000m, "DDR4", 32, 3200, 2, 1.35m, "Tản nhiệt cấu hình thấp."),
            new RamSeed("RAM Corsair Vengeance RGB 16GB (1x16GB) DDR5 5200MHz", "RAM-COR-VRG16", "Corsair", 1890000m, 1750000m, "DDR5", 16, 5200, 1, 1.25m, "Bản lẻ 1 thanh."),
            new RamSeed("RAM Corsair Vengeance LPX 16GB (2x8GB) DDR4 3600MHz", "RAM-COR-VLX16H", "Corsair", 1290000m, 1150000m, "DDR4", 16, 3600, 2, 1.35m, "Kit 16GB xung cao."),
            new RamSeed("RAM Corsair Vengeance LPX 16GB (2x8GB) DDR4 3200MHz", "RAM-COR-VLX16", "Corsair", 1090000m, 990000m, "DDR4", 16, 3200, 2, 1.35m, "RAM quốc dân mọi cấu hình."),
            new RamSeed("RAM Corsair Vengeance LPX 8GB (1x8GB) DDR4 3200MHz", "RAM-COR-VLX8", "Corsair", 590000m, 550000m, "DDR4", 8, 3200, 1, 1.35m, "Tiết kiệm chi phí nâng cấp."),
            new RamSeed("RAM G.Skill Trident Z5 RGB 64GB (2x32GB) DDR5 6400MHz", "RAM-GSK-TZ564", "G.Skill", 7590000m, 7290000m, "DDR5", 64, 6400, 2, 1.40m, "Tuyệt phẩm cho dân ép xung."),
            new RamSeed("RAM G.Skill Trident Z5 RGB 32GB (2x16GB) DDR5 7200MHz", "RAM-GSK-TZ532H", "G.Skill", 5490000m, 5190000m, "DDR5", 32, 7200, 2, 1.45m, "Xung nhịp cực khủng."),
            new RamSeed("RAM G.Skill Trident Z5 Neo RGB 32GB (2x16GB) DDR5 6000MHz", "RAM-GSK-TZ5N", "G.Skill", 3790000m, 3590000m, "DDR5", 32, 6000, 2, 1.35m, "Thiết kế riêng cho AMD Ryzen 7000."),
            new RamSeed("RAM G.Skill Flare X5 32GB (2x16GB) DDR5 6000MHz", "RAM-GSK-FLX32", "G.Skill", 3290000m, 3090000m, "DDR5", 32, 6000, 2, 1.35m, "Nhỏ gọn, ổn định cao."),
            new RamSeed("RAM G.Skill Trident Z Royal 32GB (2x16GB) DDR4 3600MHz", "RAM-GSK-TZR32", "G.Skill", 3690000m, 3490000m, "DDR4", 32, 3600, 2, 1.35m, "Tản nhiệt đính đá sang trọng."),
            new RamSeed("RAM G.Skill Trident Z RGB 32GB (2x16GB) DDR4 3200MHz", "RAM-GSK-TZ32", "G.Skill", 2490000m, 2290000m, "DDR4", 32, 3200, 2, 1.35m, "LED mượt mà huyền thoại."),
            new RamSeed("RAM G.Skill Ripjaws V 32GB (2x16GB) DDR4 3200MHz", "RAM-GSK-RJV32", "G.Skill", 1890000m, 1750000m, "DDR4", 32, 3200, 2, 1.35m, "Đậm chất gaming truyền thống."),
            new RamSeed("RAM G.Skill Ripjaws S5 32GB (2x16GB) DDR5 5600MHz", "RAM-GSK-RJS32", "G.Skill", 2890000m, 2690000m, "DDR5", 32, 5600, 2, 1.20m, "DDR5 giá mềm cho Intel."),
            new RamSeed("RAM G.Skill Trident Z RGB 16GB (2x8GB) DDR4 3600MHz", "RAM-GSK-TZ16", "G.Skill", 1490000m, 1390000m, "DDR4", 16, 3600, 2, 1.35m, "Kit 16GB LED đẹp."),
            new RamSeed("RAM G.Skill Ripjaws V 16GB (2x8GB) DDR4 3200MHz", "RAM-GSK-RJV16", "G.Skill", 1190000m, 1050000m, "DDR4", 16, 3200, 2, 1.35m, "Giá rẻ, hiệu năng đáng tin cậy."),
            new RamSeed("RAM G.Skill Aegis 16GB (1x16GB) DDR4 3200MHz", "RAM-GSK-AEG16", "G.Skill", 950000m, 890000m, "DDR4", 16, 3200, 1, 1.35m, "Phiên bản không tản nhiệt kim loại."),
            new RamSeed("RAM G.Skill Aegis 8GB (1x8GB) DDR4 3200MHz", "RAM-GSK-AEG8", "G.Skill", 550000m, 490000m, "DDR4", 8, 3200, 1, 1.35m, "Lựa chọn siêu tiết kiệm."),
            new RamSeed("RAM Kingston FURY Renegade RGB 64GB (2x32GB) DDR5 6000MHz", "RAM-KIN-RNG64", "Kingston", 7290000m, 6990000m, "DDR5", 64, 6000, 2, 1.35m, "Hiệu suất đỉnh cao cho game."),
            new RamSeed("RAM Kingston FURY Renegade 32GB (2x16GB) DDR5 6400MHz", "RAM-KIN-RNG32", "Kingston", 4590000m, 4390000m, "DDR5", 32, 6400, 2, 1.40m, "Đen nhám mạnh mẽ."),
            new RamSeed("RAM Kingston FURY Beast RGB 32GB (2x16GB) DDR5 6000MHz", "RAM-KIN-FBR32", "Kingston", 3590000m, 3390000m, "DDR5", 32, 6000, 2, 1.35m, "LED tùy biến dễ dàng qua phần mềm."),
            new RamSeed("RAM Kingston FURY Beast 32GB (2x16GB) DDR5 5600MHz", "RAM-KIN-FB32", "Kingston", 2990000m, 2790000m, "DDR5", 32, 5600, 2, 1.25m, "Bo lùn dễ lắp tản nhiệt khí."),
            new RamSeed("RAM Kingston FURY Beast RGB 32GB (2x16GB) DDR4 3600MHz", "RAM-KIN-FBR32D4", "Kingston", 2390000m, 2190000m, "DDR4", 32, 3600, 2, 1.35m, "DDR4 dung lượng cao."),
            new RamSeed("RAM Kingston FURY Beast 32GB (2x16GB) DDR4 3200MHz", "RAM-KIN-FB32D4", "Kingston", 1890000m, 1750000m, "DDR4", 32, 3200, 2, 1.35m, "Nâng cấp máy trạm giá tốt."),
            new RamSeed("RAM Kingston FURY Beast RGB 16GB (2x8GB) DDR4 3200MHz", "RAM-KIN-FBR16", "Kingston", 1390000m, 1250000m, "DDR4", 16, 3200, 2, 1.35m, "Phổ biến nhất tại Việt Nam."),
            new RamSeed("RAM Kingston FURY Beast 16GB (2x8GB) DDR4 3200MHz", "RAM-KIN-FB16", "Kingston", 1090000m, 990000m, "DDR4", 16, 3200, 2, 1.35m, "Bền bỉ, thương hiệu lâu năm."),
            new RamSeed("RAM Kingston FURY Beast 16GB (1x16GB) DDR5 5200MHz", "RAM-KIN-FB16D5", "Kingston", 1590000m, 1490000m, "DDR5", 16, 5200, 1, 1.25m, "RAM DDR5 đơn lẻ."),
            new RamSeed("RAM Kingston FURY Beast 8GB (1x8GB) DDR4 3200MHz", "RAM-KIN-FB8", "Kingston", 590000m, 550000m, "DDR4", 8, 3200, 1, 1.20m, "Nâng cấp 1 thanh lẻ dễ dàng."),
            new RamSeed("RAM Kingston ValueRAM 16GB (1x16GB) DDR4 3200MHz", "RAM-KIN-VR16", "Kingston", 950000m, 890000m, "DDR4", 16, 3200, 1, 1.20m, "Chuẩn JEDEC không cần XMP."),
            new RamSeed("RAM Kingston ValueRAM 8GB (1x8GB) DDR4 3200MHz", "RAM-KIN-VR8", "Kingston", 490000m, 450000m, "DDR4", 8, 3200, 1, 1.20m, "Lựa chọn tốt cho văn phòng."),
            new RamSeed("RAM Corsair Vengeance 64GB (2x32GB) DDR5 5200MHz", "RAM-COR-VEN64", "Corsair", 6590000m, 6290000m, "DDR5", 64, 5200, 2, 1.25m, "Dung lượng cực khủng."),
            new RamSeed("RAM G.Skill Ripjaws S5 64GB (2x32GB) DDR5 5200MHz", "RAM-GSK-RJS64", "G.Skill", 6490000m, 6190000m, "DDR5", 64, 5200, 2, 1.25m, "Chuyên dụng dựng phim 3D."),
            new RamSeed("RAM Kingston FURY Beast 64GB (2x32GB) DDR5 5600MHz", "RAM-KIN-FB64", "Kingston", 6690000m, 6390000m, "DDR5", 64, 5600, 2, 1.25m, "Nhanh và nhiều dung lượng."),
            new RamSeed("RAM Corsair Vengeance LPX 64GB (2x32GB) DDR4 3200MHz", "RAM-COR-VLX64", "Corsair", 3890000m, 3690000m, "DDR4", 64, 3200, 2, 1.35m, "Tối đa hóa bo mạch DDR4."),
            new RamSeed("RAM G.Skill Trident Z Neo 32GB (2x16GB) DDR4 3600MHz", "RAM-GSK-TZN32", "G.Skill", 2890000m, 2690000m, "DDR4", 32, 3600, 2, 1.35m, "Thiết kế cực chất cho AM4."),
            new RamSeed("RAM Kingston FURY Renegade 16GB (1x16GB) DDR4 3600MHz", "RAM-KIN-RNG16D4", "Kingston", 1290000m, 1190000m, "DDR4", 16, 3600, 1, 1.35m, "Đơn lẻ xung nhịp cao."),
            new RamSeed("RAM Corsair Vengeance RGB 48GB (2x24GB) DDR5 6000MHz", "RAM-COR-VRG48", "Corsair", 5590000m, 5290000m, "DDR5", 48, 6000, 2, 1.35m, "Mức dung lượng mới lẻ 24GB."),
            new RamSeed("RAM G.Skill Trident Z5 RGB 48GB (2x24GB) DDR5 6400MHz", "RAM-GSK-TZ548", "G.Skill", 5990000m, 5690000m, "DDR5", 48, 6400, 2, 1.40m, "Kit 48GB hoàn hảo cho gamer."),
            new RamSeed("RAM Kingston FURY Beast 48GB (2x24GB) DDR5 6000MHz", "RAM-KIN-FB48", "Kingston", 5490000m, 5190000m, "DDR5", 48, 6000, 2, 1.35m, "Ổn định với mức dung lượng đặc biệt."),
            new RamSeed("RAM Corsair Dominator Platinum RGB 32GB (2x16GB) DDR4 3200MHz", "RAM-COR-DPL32D4", "Corsair", 3990000m, 3790000m, "DDR4", 32, 3200, 2, 1.35m, "Hàng độc cho nền tảng cũ."),
            new RamSeed("RAM G.Skill Ripjaws V 64GB (2x32GB) DDR4 3600MHz", "RAM-GSK-RJV64", "G.Skill", 3990000m, 3790000m, "DDR4", 64, 3600, 2, 1.35m, "Chạy máy ảo mượt mà."),
            new RamSeed("RAM Kingston FURY Impact 32GB (2x16GB) SO-DIMM DDR4 3200MHz", "RAM-KIN-FI32", "Kingston", 2190000m, 1990000m, "DDR4", 32, 3200, 2, 1.20m, "RAM Laptop/Mini PC cao cấp."),
            new RamSeed("RAM Corsair Vengeance 32GB (2x16GB) SO-DIMM DDR5 4800MHz", "RAM-COR-VS32", "Corsair", 2990000m, 2790000m, "DDR5", 32, 4800, 2, 1.10m, "DDR5 cho Laptop."),
            new RamSeed("RAM G.Skill Ripjaws 16GB (1x16GB) SO-DIMM DDR4 3200MHz", "RAM-GSK-RJL16", "G.Skill", 1090000m, 990000m, "DDR4", 16, 3200, 1, 1.20m, "Tăng tốc laptop nhanh chóng."),
            new RamSeed("RAM Kingston FURY Impact 16GB (1x16GB) SO-DIMM DDR5 5600MHz", "RAM-KIN-FI16", "Kingston", 1690000m, 1590000m, "DDR5", 16, 5600, 1, 1.10m, "DDR5 Laptop tốc độ cao."),
            new RamSeed("RAM Corsair Vengeance 16GB (1x16GB) SO-DIMM DDR4 2666MHz", "RAM-COR-VS1626", "Corsair", 950000m, 890000m, "DDR4", 16, 2666, 1, 1.20m, "Tương thích laptop thế hệ cũ.")
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
            product.TechnicalSpec.RamBusSpeed = item.SpeedMhz;
            product.TechnicalSpec.AdditionalSpecsJson = JsonSerializer.Serialize(new
            {
                capacityGb = item.CapacityGb,
                moduleCount = item.ModuleCount,
                voltageV = item.Voltage,
                regularPrice = item.RegularPrice,
                description = item.Description
            });
        }

        await context.SaveChangesAsync();
    }

    private static async Task SeedStorageAsync(AppDbContext context, IReadOnlyDictionary<ComponentType, Category> categories)
    {
        var catalog = new[]
        {
            new StorageSeed("Ổ cứng SSD Samsung 990 Pro 2TB PCIe Gen 4x4", "SSD-SAM-990P2", "Samsung", 5590000m, 5290000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 2000, 7450, 6900, "M.2 2280", "Tốc độ đỉnh cao cho PS5 và PC."),
            new StorageSeed("Ổ cứng SSD Samsung 990 Pro 1TB PCIe Gen 4x4", "SSD-SAM-990P1", "Samsung", 3290000m, 2990000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 1000, 7450, 6900, "M.2 2280", "Siêu nhanh, tản nhiệt tốt."),
            new StorageSeed("Ổ cứng SSD Samsung 980 Pro 2TB PCIe Gen 4x4", "SSD-SAM-980P2", "Samsung", 4590000m, 4290000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 2000, 7000, 5100, "M.2 2280", "Hiệu suất PCIe 4.0 hàng đầu."),
            new StorageSeed("Ổ cứng SSD Samsung 980 Pro 1TB PCIe Gen 4x4", "SSD-SAM-980P1", "Samsung", 2590000m, 2390000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 1000, 7000, 5000, "M.2 2280", "Lựa chọn đáng tin cậy."),
            new StorageSeed("Ổ cứng SSD Samsung 970 EVO Plus 2TB PCIe Gen 3x4", "SSD-SAM-970E2", "Samsung", 3590000m, 3290000m, ComponentType.SSD, "SSD", "PCIe 3.0 x4", 2000, 3500, 3300, "M.2 2280", "Chuẩn Gen 3 dung lượng cao."),
            new StorageSeed("Ổ cứng SSD Samsung 970 EVO Plus 1TB PCIe Gen 3x4", "SSD-SAM-970E1", "Samsung", 1890000m, 1690000m, ComponentType.SSD, "SSD", "PCIe 3.0 x4", 1000, 3500, 3300, "M.2 2280", "Chạy mượt mà, tuổi thọ cao."),
            new StorageSeed("Ổ cứng SSD Samsung 970 EVO Plus 500GB PCIe Gen 3x4", "SSD-SAM-970E5", "Samsung", 1190000m, 1050000m, ComponentType.SSD, "SSD", "PCIe 3.0 x4", 500, 3500, 3200, "M.2 2280", "Đủ dùng cài Win và game cơ bản."),
            new StorageSeed("Ổ cứng SSD Samsung 870 EVO 1TB SATA III 2.5 inch", "SSD-SAM-870E1", "Samsung", 2290000m, 2090000m, ComponentType.SSD, "SSD", "SATA III", 1000, 560, 530, "2.5 inch", "SSD chuẩn SATA tốt nhất."),
            new StorageSeed("Ổ cứng SSD Samsung 870 EVO 500GB SATA III 2.5 inch", "SSD-SAM-870E5", "Samsung", 1290000m, 1150000m, ComponentType.SSD, "SSD", "SATA III", 500, 560, 530, "2.5 inch", "Nâng cấp máy tính cũ hoàn hảo."),
            new StorageSeed("Ổ cứng SSD Samsung 990 EVO 1TB PCIe Gen 4x4/5x2", "SSD-SAM-990E1", "Samsung", 2490000m, 2290000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 1000, 5000, 4200, "M.2 2280", "Băng thông linh hoạt 4.0 và 5.0."),
            new StorageSeed("Ổ cứng SSD WD Black SN850X 2TB PCIe Gen 4x4", "SSD-WD-850X2", "WD", 4890000m, 4590000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 2000, 7300, 6600, "M.2 2280", "Đối thủ trực tiếp của 990 Pro."),
            new StorageSeed("Ổ cứng SSD WD Black SN850X 1TB PCIe Gen 4x4", "SSD-WD-850X1", "WD", 2890000m, 2690000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 1000, 7300, 6300, "M.2 2280", "Load game siêu tốc."),
            new StorageSeed("Ổ cứng SSD WD Black SN770 2TB PCIe Gen 4x4", "SSD-WD-770B2", "WD", 3790000m, 3590000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 2000, 5150, 4850, "M.2 2280", "Gen 4 giá bình dân."),
            new StorageSeed("Ổ cứng SSD WD Black SN770 1TB PCIe Gen 4x4", "SSD-WD-770B1", "WD", 2190000m, 1990000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 1000, 5150, 4900, "M.2 2280", "Best seller phân khúc 1TB Gen 4."),
            new StorageSeed("Ổ cứng SSD WD Black SN770 500GB PCIe Gen 4x4", "SSD-WD-770B5", "WD", 1390000m, 1250000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 500, 5000, 4000, "M.2 2280", "Dung lượng vừa phải, siêu tốc."),
            new StorageSeed("Ổ cứng SSD WD Blue SN580 1TB PCIe Gen 4x4", "SSD-WD-580B1", "WD", 1890000m, 1690000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 1000, 4150, 4150, "M.2 2280", "Mát mẻ, bền bỉ cho laptop."),
            new StorageSeed("Ổ cứng SSD WD Blue SN580 500GB PCIe Gen 4x4", "SSD-WD-580B5", "WD", 1190000m, 1050000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 500, 4000, 3600, "M.2 2280", "Vô địch P/P tầm trung."),
            new StorageSeed("Ổ cứng SSD WD Green SN350 1TB PCIe Gen 3x4", "SSD-WD-350G1", "WD", 1490000m, 1350000m, ComponentType.SSD, "SSD", "PCIe 3.0 x4", 1000, 3200, 3000, "M.2 2280", "Lưu trữ tài liệu và game dung lượng nhẹ."),
            new StorageSeed("Ổ cứng SSD WD Green SN350 500GB PCIe Gen 3x4", "SSD-WD-350G5", "WD", 890000m, 790000m, ComponentType.SSD, "SSD", "PCIe 3.0 x4", 500, 2400, 1500, "M.2 2280", "SSD NVMe cực rẻ."),
            new StorageSeed("Ổ cứng SSD WD Green SN350 240GB PCIe Gen 3x4", "SSD-WD-350G2", "WD", 550000m, 490000m, ComponentType.SSD, "SSD", "PCIe 3.0 x4", 240, 2400, 900, "M.2 2280", "Giải pháp cài Windows giá sinh viên."),
            new StorageSeed("Ổ cứng HDD WD Blue 2TB 3.5 inch SATA III 7200RPM", "HDD-WD-B2TB", "WD", 1690000m, 1550000m, ComponentType.HDD, "HDD", "SATA III", 2000, 150, 150, "3.5 inch", "Ổ cơ chất lượng cao để lưu trữ."),
            new StorageSeed("Ổ cứng HDD WD Blue 1TB 3.5 inch SATA III 7200RPM", "HDD-WD-B1TB", "WD", 1190000m, 1050000m, ComponentType.HDD, "HDD", "SATA III", 1000, 150, 150, "3.5 inch", "Lưu trữ hình ảnh, video truyền thống."),
            new StorageSeed("Ổ cứng HDD WD Black 4TB 3.5 inch SATA III 7200RPM", "HDD-WD-BL4TB", "WD", 4590000m, 4290000m, ComponentType.HDD, "HDD", "SATA III", 4000, 202, 202, "3.5 inch", "Ổ HDD Gaming cao cấp."),
            new StorageSeed("Ổ cứng SSD Kingston KC3000 2TB PCIe Gen 4x4", "SSD-KIN-KC2T", "Kingston", 4290000m, 3990000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 2000, 7000, 7000, "M.2 2280", "Hiệu suất đồ họa và chơi game mượt mà."),
            new StorageSeed("Ổ cứng SSD Kingston KC3000 1TB PCIe Gen 4x4", "SSD-KIN-KC1T", "Kingston", 2490000m, 2290000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 1000, 7000, 6000, "M.2 2280", "Tản nhiệt graphene tản nhiệt nhanh."),
            new StorageSeed("Ổ cứng SSD Kingston FURY Renegade 2TB PCIe Gen 4x4", "SSD-KIN-FR2T", "Kingston", 4590000m, 4290000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 2000, 7300, 7000, "M.2 2280", "Tương thích tốt với PS5."),
            new StorageSeed("Ổ cứng SSD Kingston FURY Renegade 1TB PCIe Gen 4x4", "SSD-KIN-FR1T", "Kingston", 2690000m, 2490000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 1000, 7300, 6000, "M.2 2280", "Tốc độ siêu khủng 7300MB/s."),
            new StorageSeed("Ổ cứng SSD Kingston NV2 2TB PCIe Gen 4x4", "SSD-KIN-NV2T", "Kingston", 2890000m, 2690000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 2000, 3500, 2800, "M.2 2280", "NVMe 2TB giá rẻ nhất thị trường."),
            new StorageSeed("Ổ cứng SSD Kingston NV2 1TB PCIe Gen 4x4", "SSD-KIN-NV1T", "Kingston", 1590000m, 1450000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 1000, 3500, 2100, "M.2 2280", "SSD quốc dân phân khúc 1TB."),
            new StorageSeed("Ổ cứng SSD Kingston NV2 500GB PCIe Gen 4x4", "SSD-KIN-NV5G", "Kingston", 950000m, 890000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 500, 3500, 2100, "M.2 2280", "Bán chạy nhất cho máy văn phòng."),
            new StorageSeed("Ổ cứng SSD Kingston NV2 250GB PCIe Gen 4x4", "SSD-KIN-NV2G", "Kingston", 650000m, 590000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 250, 3000, 1300, "M.2 2280", "Phù hợp làm ổ cài hệ điều hành."),
            new StorageSeed("Ổ cứng SSD Kingston A400 960GB SATA III 2.5 inch", "SSD-KIN-A4009", "Kingston", 1690000m, 1550000m, ComponentType.SSD, "SSD", "SATA III", 960, 500, 450, "2.5 inch", "SATA dung lượng cao."),
            new StorageSeed("Ổ cứng SSD Kingston A400 480GB SATA III 2.5 inch", "SSD-KIN-A4004", "Kingston", 950000m, 890000m, ComponentType.SSD, "SSD", "SATA III", 480, 500, 450, "2.5 inch", "Phục hồi tốc độ laptop cũ."),
            new StorageSeed("Ổ cứng SSD Kingston A400 240GB SATA III 2.5 inch", "SSD-KIN-A4002", "Kingston", 550000m, 490000m, ComponentType.SSD, "SSD", "SATA III", 240, 500, 350, "2.5 inch", "Lựa chọn cứu cánh cho HDD."),
            new StorageSeed("Ổ cứng SSD Samsung 990 Pro 4TB PCIe Gen 4x4", "SSD-SAM-990P4", "Samsung", 9990000m, 9490000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 4000, 7450, 6900, "M.2 2280", "Dung lượng khổng lồ, max tốc độ."),
            new StorageSeed("Ổ cứng SSD WD Black SN850X 4TB PCIe Gen 4x4", "SSD-WD-850X4", "WD", 9590000m, 9190000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 4000, 7300, 6600, "M.2 2280", "Lưu trọn bộ thư viện Game AAA."),
            new StorageSeed("Ổ cứng SSD Kingston KC3000 4TB PCIe Gen 4x4", "SSD-KIN-KC4T", "Kingston", 8590000m, 8190000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 4000, 7000, 7000, "M.2 2280", "Chi phí tối ưu cho 4TB NVMe."),
            new StorageSeed("Ổ cứng HDD WD Red Plus 4TB 3.5 inch SATA III", "HDD-WD-RED4", "WD", 3590000m, 3390000m, ComponentType.HDD, "HDD", "SATA III", 4000, 175, 175, "3.5 inch", "Chuyên dụng cho hệ thống NAS."),
            new StorageSeed("Ổ cứng HDD WD Red Plus 8TB 3.5 inch SATA III", "HDD-WD-RED8", "WD", 6290000m, 5990000m, ComponentType.HDD, "HDD", "SATA III", 8000, 210, 210, "3.5 inch", "An toàn dữ liệu server dài lâu."),
            new StorageSeed("Ổ cứng HDD WD Purple 4TB 3.5 inch SATA III", "HDD-WD-PUR4", "WD", 2890000m, 2690000m, ComponentType.HDD, "HDD", "SATA III", 4000, 150, 150, "3.5 inch", "Tối ưu cho camera giám sát 24/7."),
            new StorageSeed("Ổ cứng SSD Samsung T7 Shield 1TB Portable", "SSD-SAM-T7S1", "Samsung", 2990000m, 2790000m, ComponentType.SSD, "SSD", "USB 3.2", 1000, 1050, 1000, "Portable", "SSD di động chống sốc, chống nước."),
            new StorageSeed("Ổ cứng SSD Samsung T7 Shield 2TB Portable", "SSD-SAM-T7S2", "Samsung", 4990000m, 4690000m, ComponentType.SSD, "SSD", "USB 3.2", 2000, 1050, 1000, "Portable", "Lưu trữ footage quay phim chuyên nghiệp."),
            new StorageSeed("Ổ cứng SSD Kingston XS2000 1TB Portable", "SSD-KIN-XS1T", "Kingston", 2590000m, 2390000m, ComponentType.SSD, "SSD", "USB 3.2 Gen2", 1000, 2000, 2000, "Portable", "Bỏ túi siêu nhỏ gọn, tốc độ USB 3.2 Gen 2x2."),
            new StorageSeed("Ổ cứng SSD WD My Passport 1TB Portable", "SSD-WD-MYP1", "WD", 2790000m, 2590000m, ComponentType.SSD, "SSD", "USB 3.2", 1000, 1050, 1000, "Portable", "Thiết kế đẹp, bảo mật phần cứng."),
            new StorageSeed("Ổ cứng SSD Samsung 870 QVO 2TB SATA III 2.5 inch", "SSD-SAM-870Q2", "Samsung", 3590000m, 3390000m, ComponentType.SSD, "SSD", "SATA III", 2000, 560, 530, "2.5 inch", "SSD SATA dung lượng lớn giá rẻ."),
            new StorageSeed("Ổ cứng HDD WD Gold 8TB 3.5 inch SATA III", "HDD-WD-GLD8", "WD", 7590000m, 7190000m, ComponentType.HDD, "HDD", "SATA III", 8000, 255, 255, "3.5 inch", "Độ tin cậy chuẩn Data Center."),
            new StorageSeed("Ổ cứng SSD Samsung 980 1TB PCIe Gen 3x4", "SSD-SAM-9801T", "Samsung", 2190000m, 1990000m, ComponentType.SSD, "SSD", "PCIe 3.0 x4", 1000, 3500, 3000, "M.2 2280", "Bản DRAM-less tiết kiệm điện năng."),
            new StorageSeed("Ổ cứng SSD Samsung 980 500GB PCIe Gen 3x4", "SSD-SAM-9805G", "Samsung", 1290000m, 1150000m, ComponentType.SSD, "SSD", "PCIe 3.0 x4", 500, 3100, 2600, "M.2 2280", "Mát mẻ cho laptop văn phòng."),
            new StorageSeed("Ổ cứng SSD Kingston NV3 1TB PCIe Gen 4x4", "SSD-KIN-NV31T", "Kingston", 1690000m, 1550000m, ComponentType.SSD, "SSD", "PCIe 4.0 x4", 1000, 3500, 2100, "M.2 2280", "Phiên bản cải tiến nhẹ của NV2."),
            new StorageSeed("Ổ cứng SSD WD Blue SN570 1TB PCIe Gen 3x4", "SSD-WD-570B1", "WD", 1790000m, 1650000m, ComponentType.SSD, "SSD", "PCIe 3.0 x4", 1000, 3500, 3000, "M.2 2280", "Huyền thoại Gen 3 vẫn còn bán.")
        };

        var categoriesToSeed = categories.Values
            .Where(category => category.ComponentType is ComponentType.SSD or ComponentType.HDD)
            .ToDictionary(category => category.ComponentType, category => category);
        var productsByCategory = new Dictionary<int, List<Product>>();
        foreach (var category in categoriesToSeed.Values)
        {
            productsByCategory[category.Id] = await context.Products
                .Include(p => p.TechnicalSpec)
                .Where(p => p.CategoryId == category.Id)
                .ToListAsync();
        }

        foreach (var item in catalog)
        {
            var category = categoriesToSeed[item.CategoryType];
            var products = productsByCategory[category.Id];
            var product = FindOrCreateProduct(context, products, category.Id, item.Name, item.Sku);
            product.Name = item.Name;
            product.Sku = item.Sku;
            product.Brand = item.Brand;
            product.Price = item.SalePrice;
            product.IsActive = true;
            product.TechnicalSpec ??= new TechnicalSpec();
            product.TechnicalSpec.AdditionalSpecsJson = JsonSerializer.Serialize(new
            {
                storageType = item.StorageType,
                interfaceType = item.InterfaceType,
                capacityGb = item.CapacityGb,
                readSpeed = item.ReadSpeed,
                writeSpeed = item.WriteSpeed,
                formFactor = item.FormFactor,
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

    private sealed record RamSeed(
        string Name, string Sku, string Brand, decimal RegularPrice, decimal SalePrice,
        string MemoryType, int CapacityGb, int SpeedMhz, int ModuleCount, decimal Voltage, string Description);

    private sealed record StorageSeed(
        string Name, string Sku, string Brand, decimal RegularPrice, decimal SalePrice,
        ComponentType CategoryType, string StorageType, string InterfaceType, int CapacityGb,
        int ReadSpeed, int WriteSpeed, string FormFactor, string Description);
}
