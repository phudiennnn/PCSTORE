using Microsoft.EntityFrameworkCore;
using PCStore.Core.Entities;
using PCStore.Core.Enums;
using System.Text.Json;

namespace PCStore.Infrastructure.Data;

public static class PowerCaseCoolingCatalogSeeder
{
    public static async Task SeedAsync(AppDbContext context, IReadOnlyDictionary<ComponentType, Category> categories)
    {
        await SeedPowerSuppliesAsync(context, categories[ComponentType.PSU]);
        await SeedCasesAsync(context, categories[ComponentType.Case]);
        await SeedCoolersAsync(context, categories[ComponentType.Cooler]);
    }

    private static async Task SeedPowerSuppliesAsync(AppDbContext context, Category category)
    {
        var catalog = new[]
        {
            new PowerSupplySeed("Nguồn Corsair RM1200x Shift 1200W", "PSU-COR-RM1200S", "Corsair", 6590000m, 6290000m, 1200, "80 Plus Gold", "ATX", "Full Modular", "Cáp module cắm ngang độc đáo."),
            new PowerSupplySeed("Nguồn Corsair RM1000x Shift 1000W", "PSU-COR-RM1000S", "Corsair", 5590000m, 5290000m, 1000, "80 Plus Gold", "ATX", "Full Modular", "Chuẩn ATX 3.0 cho RTX 4000."),
            new PowerSupplySeed("Nguồn Corsair RM850x Shift 850W", "PSU-COR-RM850S", "Corsair", 4290000m, 3990000m, 850, "80 Plus Gold", "ATX", "Full Modular", "Hiệu suất 80 Plus Gold."),
            new PowerSupplySeed("Nguồn Corsair RM850e 850W ATX 3.0", "PSU-COR-RM850E", "Corsair", 3490000m, 3190000m, 850, "80 Plus Gold", "ATX", "Full Modular", "Hoạt động tĩnh lặng, ổn định."),
            new PowerSupplySeed("Nguồn Corsair RM750e 750W ATX 3.0", "PSU-COR-RM750E", "Corsair", 2990000m, 2790000m, 750, "80 Plus Gold", "ATX", "Full Modular", "Bản rút gọn tiết kiệm chi phí."),
            new PowerSupplySeed("Nguồn Corsair CV750 750W 80 Plus Bronze", "PSU-COR-CV750", "Corsair", 1990000m, 1850000m, 750, "80 Plus Bronze", "ATX", "Non Modular", "Chất lượng ổn định giá phổ thông."),
            new PowerSupplySeed("Nguồn Corsair CV650 650W 80 Plus Bronze", "PSU-COR-CV650", "Corsair", 1590000m, 1450000m, 650, "80 Plus Bronze", "ATX", "Non Modular", "Lựa chọn tốt cho cấu hình tầm trung."),
            new PowerSupplySeed("Nguồn Corsair CV550 550W 80 Plus Bronze", "PSU-COR-CV550", "Corsair", 1290000m, 1150000m, 550, "80 Plus Bronze", "ATX", "Non Modular", "Nguồn quốc dân cho máy văn phòng."),
            new PowerSupplySeed("Nguồn Corsair SF850L 850W SFX", "PSU-COR-SF850L", "Corsair", 4590000m, 4390000m, 850, "80 Plus Gold", "SFX-L", "Full Modular", "Nguồn form SFX cho case mini ITX."),
            new PowerSupplySeed("Nguồn Corsair SF750 750W 80 Plus Platinum SFX", "PSU-COR-SF750", "Corsair", 4290000m, 3990000m, 750, "80 Plus Platinum", "SFX", "Full Modular", "Vua nguồn SFX."),
            new PowerSupplySeed("Nguồn ASUS ROG Thor 1200W Platinum II", "PSU-ASU-THOR12", "ASUS", 9990000m, 9490000m, 1200, "80 Plus Platinum", "ATX", "Full Modular", "Màn hình OLED hiển thị công suất."),
            new PowerSupplySeed("Nguồn ASUS ROG Thor 1000W Platinum II", "PSU-ASU-THOR10", "ASUS", 8590000m, 8190000m, 1000, "80 Plus Platinum", "ATX", "Full Modular", "Tản nhiệt ROG siêu mát."),
            new PowerSupplySeed("Nguồn ASUS ROG Strix 1000W Gold Aura Edition", "PSU-ASU-STR10A", "ASUS", 6590000m, 6290000m, 1000, "80 Plus Gold", "ATX", "Full Modular", "Thiết kế đẹp, hỗ trợ Aura Sync."),
            new PowerSupplySeed("Nguồn ASUS ROG Strix 850W Gold Aura Edition", "PSU-ASU-STR85A", "ASUS", 5290000m, 4990000m, 850, "80 Plus Gold", "ATX", "Full Modular", "ATX 3.0 sẵn sàng."),
            new PowerSupplySeed("Nguồn ASUS TUF Gaming 1000W Gold", "PSU-ASU-TUF10G", "ASUS", 4590000m, 4290000m, 1000, "80 Plus Gold", "ATX", "Full Modular", "Bền bỉ chuẩn quân sự."),
            new PowerSupplySeed("Nguồn ASUS TUF Gaming 850W Gold", "PSU-ASU-TUF85G", "ASUS", 3590000m, 3290000m, 850, "80 Plus Gold", "ATX", "Full Modular", "Best seller phân khúc 850W."),
            new PowerSupplySeed("Nguồn ASUS TUF Gaming 750W Gold", "PSU-ASU-TUF75G", "ASUS", 2990000m, 2790000m, 750, "80 Plus Gold", "ATX", "Full Modular", "Mát mẻ và độ bền cao."),
            new PowerSupplySeed("Nguồn ASUS TUF Gaming 650W Bronze", "PSU-ASU-TUF65B", "ASUS", 1790000m, 1650000m, 650, "80 Plus Bronze", "ATX", "Non Modular", "Lựa chọn tiết kiệm từ TUF."),
            new PowerSupplySeed("Nguồn ASUS Prime 750W Gold", "PSU-ASU-PR75G", "ASUS", 2690000m, 2490000m, 750, "80 Plus Gold", "ATX", "Full Modular", "Phiên bản Prime thanh lịch."),
            new PowerSupplySeed("Nguồn ASUS ROG Loki SFX-L 850W Platinum", "PSU-ASU-LOKI85", "ASUS", 6590000m, 6290000m, 850, "80 Plus Platinum", "SFX-L", "Full Modular", "SFX-L cao cấp cho ITX."),
            new PowerSupplySeed("Nguồn MSI MEG Ai1300P PCIE5 1300W Platinum", "PSU-MSI-AI13", "MSI", 8990000m, 8490000m, 1300, "80 Plus Platinum", "ATX", "Full Modular", "Điều khiển phần mềm thông minh."),
            new PowerSupplySeed("Nguồn MSI MPG A1000G PCIE5 1000W Gold", "PSU-MSI-A10G5", "MSI", 4990000m, 4690000m, 1000, "80 Plus Gold", "ATX", "Full Modular", "Nguồn quốc dân cho RTX 4080/4090."),
            new PowerSupplySeed("Nguồn MSI MPG A850G PCIE5 850W Gold", "PSU-MSI-A85G5", "MSI", 3690000m, 3490000m, 850, "80 Plus Gold", "ATX", "Full Modular", "Cổng 16-pin chuẩn xác."),
            new PowerSupplySeed("Nguồn MSI MAG A850GL PCIE5 850W Gold", "PSU-MSI-A85GL", "MSI", 3190000m, 2990000m, 850, "80 Plus Gold", "ATX", "Full Modular", "Kích thước ATX cực ngắn."),
            new PowerSupplySeed("Nguồn MSI MAG A750GL PCIE5 750W Gold", "PSU-MSI-A75GL", "MSI", 2590000m, 2390000m, 750, "80 Plus Gold", "ATX", "Full Modular", "Giá siêu tốt cho chuẩn Gold."),
            new PowerSupplySeed("Nguồn MSI MAG A650BN 650W Bronze", "PSU-MSI-A65BN", "MSI", 1490000m, 1350000m, 650, "80 Plus Bronze", "ATX", "Non Modular", "Nguồn quốc dân 650W."),
            new PowerSupplySeed("Nguồn MSI MAG A550BN 550W Bronze", "PSU-MSI-A55BN", "MSI", 1190000m, 1050000m, 550, "80 Plus Bronze", "ATX", "Non Modular", "Rẻ, bền bỉ, chạy mát."),
            new PowerSupplySeed("Nguồn MSI MAG A750BN PCIE5 750W Bronze", "PSU-MSI-A75BN", "MSI", 1890000m, 1750000m, 750, "80 Plus Bronze", "ATX", "Non Modular", "Hỗ trợ RTX 40 Series mức giá thấp."),
            new PowerSupplySeed("Nguồn MSI MPG A750GF 750W Gold", "PSU-MSI-A75GF", "MSI", 2890000m, 2690000m, 750, "80 Plus Gold", "ATX", "Full Modular", "Tối ưu cho card nhiều chân nguồn."),
            new PowerSupplySeed("Nguồn MSI MPG A850GF 850W Gold", "PSU-MSI-A85GF", "MSI", 3290000m, 3090000m, 850, "80 Plus Gold", "ATX", "Full Modular", "Full modular cao cấp."),
            new PowerSupplySeed("Nguồn GIGABYTE UD1000GM PG5 1000W Gold", "PSU-GIG-UD10G5", "Gigabyte", 4290000m, 3990000m, 1000, "80 Plus Gold", "ATX", "Full Modular", "Siêu bền UD series."),
            new PowerSupplySeed("Nguồn GIGABYTE UD850GM PG5 850W Gold", "PSU-GIG-UD85G5", "Gigabyte", 3290000m, 2990000m, 850, "80 Plus Gold", "ATX", "Full Modular", "Tản nhiệt cực kì im lặng."),
            new PowerSupplySeed("Nguồn GIGABYTE UD750GM 750W Gold", "PSU-GIG-UD75G", "Gigabyte", 2490000m, 2290000m, 750, "80 Plus Gold", "ATX", "Full Modular", "Mạch thiết kế chắc chắn."),
            new PowerSupplySeed("Nguồn GIGABYTE P650B 650W Bronze", "PSU-GIG-P650B", "Gigabyte", 1390000m, 1250000m, 650, "80 Plus Bronze", "ATX", "Non Modular", "Ổn định, bảo vệ quá dòng tốt."),
            new PowerSupplySeed("Nguồn GIGABYTE P550B 550W Bronze", "PSU-GIG-P550B", "Gigabyte", 1090000m, 990000m, 550, "80 Plus Bronze", "ATX", "Non Modular", "Chi phí thấp nhất từ Gigabyte."),
            new PowerSupplySeed("Nguồn GIGABYTE AORUS P1200W Platinum", "PSU-GIG-A120P", "Gigabyte", 8590000m, 8190000m, 1200, "80 Plus Platinum", "ATX", "Full Modular", "Màn hình LCD tích hợp."),
            new PowerSupplySeed("Nguồn GIGABYTE AORUS P850W Gold", "PSU-GIG-A850G", "Gigabyte", 3590000m, 3390000m, 850, "80 Plus Gold", "ATX", "Full Modular", "Đẳng cấp gaming."),
            new PowerSupplySeed("Nguồn GIGABYTE AORUS P750W Gold", "PSU-GIG-A750G", "Gigabyte", 2990000m, 2790000m, 750, "80 Plus Gold", "ATX", "Full Modular", "Full Modular dây dẹt."),
            new PowerSupplySeed("Nguồn GIGABYTE GP-UD850GM 850W Gold", "PSU-GIG-UD850", "Gigabyte", 3090000m, 2890000m, 850, "80 Plus Gold", "ATX", "Full Modular", "Thiết kế chuẩn kích thước cũ."),
            new PowerSupplySeed("Nguồn GIGABYTE GP-P450B 450W Bronze", "PSU-GIG-P450B", "Gigabyte", 890000m, 790000m, 450, "80 Plus Bronze", "ATX", "Non Modular", "Dành cho máy tính văn phòng nhẹ."),
            new PowerSupplySeed("Nguồn Corsair HX1500i 1500W Platinum", "PSU-COR-HX15I", "Corsair", 10590000m, 9990000m, 1500, "80 Plus Platinum", "ATX", "Full Modular", "Nguồn khủng cho Workstation."),
            new PowerSupplySeed("Nguồn Corsair HX1000i 1000W Platinum", "PSU-COR-HX10I", "Corsair", 6590000m, 6290000m, 1000, "80 Plus Platinum", "ATX", "Full Modular", "Kết nối iCUE độc quyền."),
            new PowerSupplySeed("Nguồn ASUS ROG Thor 1600W Titanium", "PSU-ASU-THOR16", "ASUS", 15990000m, 15490000m, 1600, "80 Plus Titanium", "ATX", "Full Modular", "Vị thần sấm sét."),
            new PowerSupplySeed("Nguồn ASUS ROG Loki SFX-L 1000W Platinum", "PSU-ASU-LOKI10", "ASUS", 7590000m, 7190000m, 1000, "80 Plus Platinum", "SFX-L", "Full Modular", "Nhỏ nhất mà mạnh nhất."),
            new PowerSupplySeed("Nguồn MSI MEG Ai1000P PCIE5 1000W Platinum", "PSU-MSI-AI10", "MSI", 6590000m, 6290000m, 1000, "80 Plus Platinum", "ATX", "Full Modular", "Điều tốc quạt tối ưu không ồn."),
            new PowerSupplySeed("Nguồn MSI MPG A1000G 1000W Gold", "PSU-MSI-A10G", "MSI", 4290000m, 3990000m, 1000, "80 Plus Gold", "ATX", "Full Modular", "Phiên bản không PCIE5 giá rẻ."),
            new PowerSupplySeed("Nguồn GIGABYTE UD1300GM PG5 1300W Gold", "PSU-GIG-UD13", "Gigabyte", 5990000m, 5690000m, 1300, "80 Plus Gold", "ATX", "Full Modular", "Gánh vác mọi cấu hình kép."),
            new PowerSupplySeed("Nguồn Corsair CX750 750W Bronze", "PSU-COR-CX750", "Corsair", 1890000m, 1750000m, 750, "80 Plus Bronze", "ATX", "Non Modular", "Nguồn CX thế hệ mới."),
            new PowerSupplySeed("Nguồn Corsair CX650 650W Bronze", "PSU-COR-CX650", "Corsair", 1490000m, 1350000m, 650, "80 Plus Bronze", "ATX", "Non Modular", "Dây cáp bọc lưới cao cấp."),
            new PowerSupplySeed("Nguồn ASUS TUF Gaming 1200W Gold", "PSU-ASU-TUF12G", "ASUS", 5590000m, 5290000m, 1200, "80 Plus Gold", "ATX", "Full Modular", "Bền bỉ bất chấp thời gian.")
        };

        var products = await LoadCategoryProductsAsync(context, category.Id);
        foreach (var item in catalog)
        {
            var product = FindOrCreateProduct(context, products, category.Id, item.Name, item.Sku);
            product.Name = item.Name;
            product.Sku = item.Sku;
            product.Brand = item.Brand;
            product.Price = item.SalePrice;
            product.IsActive = true;
            product.TechnicalSpec ??= new TechnicalSpec();
            product.TechnicalSpec.TdpWattage = item.Wattage;
            product.TechnicalSpec.FormFactor = item.FormFactor;
            product.TechnicalSpec.AdditionalSpecsJson = JsonSerializer.Serialize(new
            {
                wattage = item.Wattage,
                efficiencyRating = item.EfficiencyRating,
                modularType = item.ModularType,
                regularPrice = item.RegularPrice,
                description = item.Description
            });
        }

        await context.SaveChangesAsync();
    }

    private static async Task SeedCasesAsync(AppDbContext context, Category category)
    {
        var catalog = new[]
        {
            new CaseSeed("Case Corsair iCUE 5000X RGB Tempered Glass Black", "CAS-COR-500X", "Corsair", 4990000m, 4690000m, "Mid Tower", 400, 170, "360mm / 240mm", 4, "Case mặt kính đẹp xuất sắc."),
            new CaseSeed("Case Corsair iCUE 4000X RGB Tempered Glass White", "CAS-COR-400XW", "Corsair", 3590000m, 3290000m, "Mid Tower", 360, 170, "360mm / 240mm", 4, "Thoáng khí, màu trắng tinh khôi."),
            new CaseSeed("Case Corsair 4000D AIRFLOW Tempered Glass Black", "CAS-COR-400DB", "Corsair", 2290000m, 2090000m, "Mid Tower", 360, 170, "360mm / 280mm", 4, "Luồng gió cực mạnh."),
            new CaseSeed("Case Corsair 5000D AIRFLOW Tempered Glass White", "CAS-COR-500DW", "Corsair", 3890000m, 3590000m, "Mid Tower", 400, 170, "360mm / 240mm", 6, "Không gian cực rộng rãi."),
            new CaseSeed("Case Corsair 7000D AIRFLOW Full-Tower Black", "CAS-COR-700DB", "Corsair", 6590000m, 6290000m, "Full Tower", 450, 190, "420mm / 360mm", 10, "Case khổng lồ cho custom loop."),
            new CaseSeed("Case Corsair iCUE 220T RGB Airflow Black", "CAS-COR-220TB", "Corsair", 2590000m, 2390000m, "Mid Tower", 300, 160, "240mm", 4, "Gọn nhẹ, kèm 3 fan RGB."),
            new CaseSeed("Case Corsair 3000D AIRFLOW Tempered Glass", "CAS-COR-3000D", "Corsair", 1890000m, 1750000m, "Mid Tower", 360, 170, "360mm / 280mm", 4, "Dòng case quốc dân mới."),
            new CaseSeed("Case Corsair 6500X Dual Chamber White", "CAS-COR-650XW", "Corsair", 4990000m, 4690000m, "Mid Tower", 400, 190, "360mm / 240mm", 4, "Bể cá đôi cực xịn."),
            new CaseSeed("Case Corsair 2500X Micro ATX Dual Chamber Black", "CAS-COR-250XB", "Corsair", 3590000m, 3390000m, "Micro ATX", 400, 180, "360mm / 240mm", 4, "Bể cá nhỏ gọn."),
            new CaseSeed("Case Corsair Crystal 280X RGB Micro-ATX White", "CAS-COR-280X", "Corsair", 3990000m, 3690000m, "Micro ATX", 300, 150, "240mm / 280mm", 4, "Huyền thoại m-ATX khối vuông."),
            new CaseSeed("Case ASUS ROG Hyperion GR701", "CAS-ASU-GR701", "ASUS", 11990000m, 11490000m, "Full Tower", 460, 190, "420mm / 360mm", 7, "Quái vật biến hình của Republic of Gamers."),
            new CaseSeed("Case ASUS ROG Strix Helios GX601", "CAS-ASU-HELIOS", "ASUS", 7590000m, 7190000m, "Mid Tower", 450, 190, "420mm / 360mm", 6, "Mặt kính cường lực 3 mặt."),
            new CaseSeed("Case ASUS TUF Gaming GT502 Black", "CAS-ASU-GT502B", "ASUS", 3890000m, 3590000m, "Mid Tower", 400, 163, "360mm / 240mm", 4, "Thiết kế phòng chia đôi tối ưu nhiệt."),
            new CaseSeed("Case ASUS TUF Gaming GT502 White", "CAS-ASU-GT502W", "ASUS", 3990000m, 3690000m, "Mid Tower", 400, 163, "360mm / 240mm", 4, "Phiên bản trắng bể cá hoàn hảo."),
            new CaseSeed("Case ASUS TUF Gaming GT301", "CAS-ASU-GT301", "ASUS", 1890000m, 1750000m, "Mid Tower", 320, 160, "360mm", 6, "Dây đai đặc trưng trước mặt case."),
            new CaseSeed("Case ASUS Prime AP201 MicroATX Mesh Black", "CAS-ASU-AP201B", "ASUS", 1690000m, 1550000m, "Micro ATX", 338, 170, "360mm / 280mm", 4, "Vỏ lưới 33 lít nhỏ gọn mát rượi."),
            new CaseSeed("Case ASUS Prime AP201 MicroATX Tempered Glass White", "CAS-ASU-AP201W", "ASUS", 1890000m, 1750000m, "Micro ATX", 338, 170, "360mm / 280mm", 4, "Bản kính cường lực khoe nội thất."),
            new CaseSeed("Case ASUS ROG Z11 Mini-ITX", "CAS-ASU-Z11", "ASUS", 6590000m, 6190000m, "Mini ITX", 320, 130, "240mm", 2, "Case ITX góc nghiêng 11 độ độc quyền."),
            new CaseSeed("Case ASUS TUF Gaming GT501", "CAS-ASU-GT501", "ASUS", 3590000m, 3290000m, "Mid Tower", 420, 180, "360mm / 280mm", 7, "Hầm hố, có quai xách chịu lực."),
            new CaseSeed("Case ASUS A21 Micro-ATX White", "CAS-ASU-A21W", "ASUS", 1590000m, 1450000m, "Micro ATX", 380, 165, "360mm", 3, "Hỗ trợ mainboard giấu dây cáp."),
            new CaseSeed("Case MSI MEG PROSPECT 700R", "CAS-MSI-PRO7", "MSI", 9990000m, 9490000m, "Mid Tower", 400, 185, "360mm", 4, "Màn hình cảm ứng đa chức năng."),
            new CaseSeed("Case MSI MPG VELOX 100R", "CAS-MSI-VEL1", "MSI", 2890000m, 2690000m, "Mid Tower", 380, 175, "360mm", 4, "Luồng gió dồi dào với ARGB."),
            new CaseSeed("Case MSI MPG GUNGNIR 300R AIRFLOW", "CAS-MSI-GUN3", "MSI", 3290000m, 2990000m, "Mid Tower", 360, 175, "360mm", 4, "Kèm giá đỡ VGA ARGB đa chiều."),
            new CaseSeed("Case MSI MPG GUNGNIR 110R", "CAS-MSI-GUN1", "MSI", 2290000m, 2090000m, "Mid Tower", 340, 170, "360mm", 4, "Mặt trước vát cạnh thời trang."),
            new CaseSeed("Case MSI MAG VAMPIRIC 100R", "CAS-MSI-VAM1", "MSI", 1290000m, 1150000m, "Mid Tower", 300, 160, "240mm", 3, "Lựa chọn tiết kiệm từ MSI."),
            new CaseSeed("Case MSI MAG FORGE 320R AIRFLOW", "CAS-MSI-FOR3", "MSI", 1790000m, 1650000m, "Mid Tower", 330, 160, "240mm", 3, "Kèm sẵn 4 fan ARGB."),
            new CaseSeed("Case MSI MAG FORGE 120A AIRFLOW", "CAS-MSI-FOR1", "MSI", 1190000m, 1090000m, "Mid Tower", 330, 160, "240mm", 3, "Lưới thông minh cản bụi."),
            new CaseSeed("Case MSI MAG PANO M100R PZ Black", "CAS-MSI-PANO1", "MSI", 2490000m, 2290000m, "Micro ATX", 390, 175, "360mm", 3, "Bể cá panoramic cực hot."),
            new CaseSeed("Case MSI MAG PANO M100R PZ White", "CAS-MSI-PANO1W", "MSI", 2590000m, 2390000m, "Micro ATX", 390, 175, "360mm", 3, "Hỗ trợ Project Zero giấu dây hoàn toàn."),
            new CaseSeed("Case MSI Creator 400M", "CAS-MSI-CRE4", "MSI", 3990000m, 3690000m, "Mid Tower", 330, 166, "360mm", 4, "Phong cách tĩnh lặng cho người sáng tạo."),
            new CaseSeed("Case GIGABYTE AORUS C700 GLASS", "CAS-GIG-C700", "Gigabyte", 9990000m, 9490000m, "Full Tower", 490, 198, "420mm / 360mm", 10, "Bảo vật của đại bàng Aorus."),
            new CaseSeed("Case GIGABYTE AORUS C300 GLASS", "CAS-GIG-C300", "Gigabyte", 2890000m, 2690000m, "Mid Tower", 400, 170, "360mm", 5, "Lựa chọn tầm trung hợp tông Gigabyte."),
            new CaseSeed("Case GIGABYTE C200 GLASS", "CAS-GIG-C200", "Gigabyte", 1190000m, 1050000m, "Mid Tower", 330, 165, "280mm", 4, "Led RGB nhẹ nhàng tinh tế."),
            new CaseSeed("Case Corsair iCUE LINK 6500X RGB Black", "CAS-COR-650L", "Corsair", 5590000m, 5290000m, "Mid Tower", 400, 190, "360mm / 240mm", 4, "Công nghệ iCUE LINK mới."),
            new CaseSeed("Case ASUS ProArt PA602", "CAS-ASU-PA602", "ASUS", 5990000m, 5690000m, "Mid Tower", 450, 190, "420mm", 8, "Lưu thông khí siêu việt cho creator."),
            new CaseSeed("Case Corsair 2000D AIRFLOW Mini-ITX", "CAS-COR-2000D", "Corsair", 2590000m, 2390000m, "Mini ITX", 320, 90, "360mm", 3, "Tòa tháp mini cho ITX."),
            new CaseSeed("Case Corsair 4000D RGB AIRFLOW White", "CAS-COR-400RW", "Corsair", 3590000m, 3290000m, "Mid Tower", 360, 170, "360mm / 280mm", 4, "Kèm fan AF120 RGB Elite."),
            new CaseSeed("Case Corsair 5000D RGB AIRFLOW Black", "CAS-COR-500RB", "Corsair", 4990000m, 4690000m, "Mid Tower", 400, 170, "360mm", 6, "Luồng gió và ánh sáng."),
            new CaseSeed("Case ASUS ROG Strix Z11", "CAS-ASU-SZ11", "ASUS", 6590000m, 6190000m, "Mini ITX", 320, 130, "240mm", 2, "Chất liệu nhôm nguyên khối."),
            new CaseSeed("Case MSI SEKIRA 500X", "CAS-MSI-SEK5", "MSI", 5990000m, 5690000m, "Mid Tower", 400, 170, "360mm", 6, "Cửa mở 2 cánh nhôm xước."),
            new CaseSeed("Case GIGABYTE AORUS C500 GLASS", "CAS-GIG-C500", "Gigabyte", 4590000m, 4290000m, "Mid Tower", 420, 190, "420mm", 4, "Luồng gió mượt mà, khung thép."),
            new CaseSeed("Case GIGABYTE C301 GLASS White", "CAS-GIG-C301W", "Gigabyte", 2190000m, 1990000m, "Mid Tower", 400, 170, "360mm", 4, "Vỏ trắng đẹp mắt kèm fan ARGB."),
            new CaseSeed("Case ASUS TUF Gaming GT502 Plus", "CAS-ASU-GT502P", "ASUS", 4590000m, 4290000m, "Mid Tower", 400, 163, "360mm / 240mm", 4, "Kèm sẵn hub và fan ARGB TUF."),
            new CaseSeed("Case Corsair Obsidian 1000D", "CAS-COR-1000D", "Corsair", 12990000m, 12490000m, "Super Tower", 400, 180, "480mm / 420mm", 11, "Case Super-Tower hỗ trợ 2 hệ thống."),
            new CaseSeed("Case Corsair Carbide 175R RGB", "CAS-COR-175R", "Corsair", 1490000m, 1350000m, "Mid Tower", 330, 160, "360mm", 4, "Đơn giản, sang trọng."),
            new CaseSeed("Case ASUS ROG Hyperion GR701 White Edition", "CAS-ASU-GR701W", "ASUS", 12990000m, 12490000m, "Full Tower", 460, 190, "420mm", 7, "Phiên bản trắng giới hạn."),
            new CaseSeed("Case MSI MAG FORGE 112R", "CAS-MSI-FOR112", "MSI", 1390000m, 1250000m, "Mid Tower", 330, 160, "240mm", 3, "Vỏ kính cường lực 4mm."),
            new CaseSeed("Case MSI MPG SEKIRA 100R", "CAS-MSI-SEK1", "MSI", 2890000m, 2690000m, "Mid Tower", 340, 170, "360mm", 4, "Mặt trước chia đôi phong cách."),
            new CaseSeed("Case GIGABYTE C200G", "CAS-GIG-C200G", "Gigabyte", 1190000m, 1050000m, "Mid Tower", 330, 165, "280mm", 4, "Kính tối màu thanh lịch."),
            new CaseSeed("Case Corsair iCUE 7000X RGB Tempered Glass", "CAS-COR-7000X", "Corsair", 7590000m, 7290000m, "Full Tower", 450, 190, "420mm", 10, "Hệ thống ánh sáng cực đỉnh.")
        };

        var products = await LoadCategoryProductsAsync(context, category.Id);
        foreach (var item in catalog)
        {
            var product = FindOrCreateProduct(context, products, category.Id, item.Name, item.Sku);
            product.Name = item.Name;
            product.Sku = item.Sku;
            product.Brand = item.Brand;
            product.Price = item.SalePrice;
            product.IsActive = true;
            product.TechnicalSpec ??= new TechnicalSpec();
            product.TechnicalSpec.FormFactor = item.FormFactor;
            product.TechnicalSpec.AdditionalSpecsJson = JsonSerializer.Serialize(new
            {
                maxGpuLengthMm = item.MaxGpuLengthMm,
                maxCpuCoolerHeightMm = item.MaxCoolerHeightMm,
                radiatorSupport = item.RadiatorSupport,
                driveBays = item.DriveBays,
                regularPrice = item.RegularPrice,
                description = item.Description
            });
        }

        await context.SaveChangesAsync();
    }

    private static async Task SeedCoolersAsync(AppDbContext context, Category category)
    {
        var catalog = new[]
        {
            new CoolerSeed("Tản nhiệt nước Corsair iCUE LINK H150i RGB", "COL-COR-L150I", "Corsair", 5990000m, 5690000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 30, 300, "AIO 360mm công nghệ LINK mới."),
            new CoolerSeed("Tản nhiệt nước Corsair iCUE LINK H100i RGB", "COL-COR-L100I", "Corsair", 4590000m, 4290000m, "AIO Liquid", "LGA1700/AM5/AM4", 240, 120, 30, 250, "AIO 240mm cáp thông minh."),
            new CoolerSeed("Tản nhiệt nước Corsair iCUE H150i ELITE LCD XT", "COL-COR-H150L", "Corsair", 7590000m, 7190000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 30, 300, "Màn hình LCD siêu nét."),
            new CoolerSeed("Tản nhiệt nước Corsair iCUE H100i ELITE CAPELLIX XT", "COL-COR-H100C", "Corsair", 3990000m, 3690000m, "AIO Liquid", "LGA1700/AM5/AM4", 240, 120, 30, 250, "Led Capellix sáng rực rỡ."),
            new CoolerSeed("Tản nhiệt nước Corsair H150 RGB 360mm", "COL-COR-H150", "Corsair", 2890000m, 2690000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 27, 280, "Giải pháp AIO phổ thông 360mm."),
            new CoolerSeed("Tản nhiệt nước Corsair H100 RGB 240mm", "COL-COR-H100", "Corsair", 2190000m, 1990000m, "AIO Liquid", "LGA1700/AM5/AM4", 240, 120, 27, 220, "Giá mềm cho mọi hệ thống."),
            new CoolerSeed("Tản nhiệt khí Corsair A115", "COL-COR-A115", "Corsair", 2690000m, 2490000m, "Air Cooler", "LGA1700/AM5/AM4", null, 140, 164, 250, "Tản tháp đôi siêu to mát mẻ."),
            new CoolerSeed("Tản nhiệt nước Corsair iCUE H170i ELITE LCD XT 420mm", "COL-COR-H170", "Corsair", 8590000m, 8190000m, "AIO Liquid", "LGA1700/AM5/AM4/sTRX4", 420, 140, 27, 350, "Đỉnh cao AIO tản nhiệt 420mm."),
            new CoolerSeed("Tản nhiệt nước Corsair iCUE LINK H150i LCD", "COL-COR-L150L", "Corsair", 8290000m, 7890000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 30, 300, "iCUE Link tích hợp LCD."),
            new CoolerSeed("Tản nhiệt nước Corsair H55 RGB 120mm", "COL-COR-H55", "Corsair", 1590000m, 1450000m, "AIO Liquid", "LGA1700/AM5/AM4", 120, 120, 27, 150, "AIO 120mm cho case mini."),
            new CoolerSeed("Tản nhiệt nước ASUS ROG Ryujin III 360 ARGB", "COL-ASU-RYJ336", "ASUS", 8990000m, 8490000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 30, 300, "Vua tản nhiệt với màn hình 3.5 inch."),
            new CoolerSeed("Tản nhiệt nước ASUS ROG Ryuo III 360 ARGB", "COL-ASU-RYU336", "ASUS", 6590000m, 6290000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 30, 300, "LED ma trận Anime Matrix."),
            new CoolerSeed("Tản nhiệt nước ASUS ROG Strix LC III 360 ARGB", "COL-ASU-LC336", "ASUS", 4990000m, 4690000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 30, 280, "Khóa nam châm bơm độc đáo."),
            new CoolerSeed("Tản nhiệt nước ASUS TUF Gaming LC II 360 ARGB", "COL-ASU-TUF336", "ASUS", 3290000m, 2990000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 27, 250, "Bền bỉ, tản nhiệt tốt."),
            new CoolerSeed("Tản nhiệt nước ASUS TUF Gaming LC II 240 ARGB", "COL-ASU-TUF240", "ASUS", 2590000m, 2390000m, "AIO Liquid", "LGA1700/AM5/AM4", 240, 120, 27, 200, "2 quạt 120mm hiệu suất cao."),
            new CoolerSeed("Tản nhiệt nước ASUS ProArt LC 420", "COL-ASU-PA420", "ASUS", 7590000m, 7190000m, "AIO Liquid", "LGA1700/AM5/AM4/sTR5", 420, 140, 30, 350, "Tản nhiệt không LED, quạt Noctua."),
            new CoolerSeed("Tản nhiệt nước ASUS ROG Strix LC II 240 ARGB", "COL-ASU-LC224", "ASUS", 3890000m, 3590000m, "AIO Liquid", "LGA1700/AM5/AM4", 240, 120, 27, 200, "AIO 240mm chuẩn ROG."),
            new CoolerSeed("Tản nhiệt khí ASUS ROG Strix XF 120", "COL-ASU-XF120", "ASUS", 1590000m, 1450000m, "Air Cooler", "LGA1700/AM5/AM4", null, 120, 155, 180, "Tản nhiệt khí ROG nhỏ gọn."),
            new CoolerSeed("Tản nhiệt nước ASUS ROG Ryujin III 360 ARGB White", "COL-ASU-RYJ33W", "ASUS", 9290000m, 8790000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 30, 300, "Bản màu trắng sang trọng."),
            new CoolerSeed("Tản nhiệt nước ASUS ROG Ryuo III 240 ARGB", "COL-ASU-RYU324", "ASUS", 5590000m, 5290000m, "AIO Liquid", "LGA1700/AM5/AM4", 240, 120, 30, 250, "240mm gọn gàng hiệu năng cao."),
            new CoolerSeed("Tản nhiệt nước MSI MEG CORELIQUID S360", "COL-MSI-S360", "MSI", 6990000m, 6590000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 27, 300, "AIO MSI cao cấp nhất có màn hình."),
            new CoolerSeed("Tản nhiệt nước MSI MPG CORELIQUID K360 V2", "COL-MSI-K360", "MSI", 5590000m, 5290000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 27, 280, "Bơm tích hợp quạt làm mát VRM."),
            new CoolerSeed("Tản nhiệt nước MSI MAG CORELIQUID E360", "COL-MSI-E360", "MSI", 3290000m, 2990000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 27, 250, "Mặt kính lật logo siêu mượt."),
            new CoolerSeed("Tản nhiệt nước MSI MAG CORELIQUID M360", "COL-MSI-M360", "MSI", 2390000m, 2190000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 27, 250, "AIO 360mm quốc dân của MSI."),
            new CoolerSeed("Tản nhiệt nước MSI MAG CORELIQUID M240", "COL-MSI-M240", "MSI", 1890000m, 1690000m, "AIO Liquid", "LGA1700/AM5/AM4", 240, 120, 27, 200, "Lựa chọn AIO 240mm giá tốt."),
            new CoolerSeed("Tản nhiệt nước MSI MAG CORELIQUID 240R V2", "COL-MSI-240R2", "MSI", 1990000m, 1790000m, "AIO Liquid", "LGA1700/AM5/AM4", 240, 120, 27, 200, "Bơm đặt tại radiator độc quyền."),
            new CoolerSeed("Tản nhiệt nước MSI MAG CORELIQUID 360R V2", "COL-MSI-360R2", "MSI", 2690000m, 2490000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 27, 250, "AIO 360mm thiết kế hầm hố."),
            new CoolerSeed("Tản nhiệt khí MSI CORE FROZR L", "COL-MSI-FROL", "MSI", 1290000m, 1150000m, "Air Cooler", "LGA1700/AM5/AM4", null, 120, 155, 200, "Tản nhiệt khí cổ điển bền bỉ."),
            new CoolerSeed("Tản nhiệt nước MSI MAG CORELIQUID I360 Black", "COL-MSI-I360B", "MSI", 2990000m, 2790000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 27, 250, "Thiết kế giấu dây quạt tiện dụng."),
            new CoolerSeed("Tản nhiệt nước MSI MAG CORELIQUID E240 White", "COL-MSI-E240W", "MSI", 2490000m, 2290000m, "AIO Liquid", "LGA1700/AM5/AM4", 240, 120, 27, 200, "Sắc trắng mượt mà."),
            new CoolerSeed("Tản nhiệt nước GIGABYTE AORUS WATERFORCE X II 360", "COL-GIG-WF3602", "Gigabyte", 6990000m, 6590000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 27, 300, "Màn hình LCD mượt mà, quạt nối tiếp."),
            new CoolerSeed("Tản nhiệt nước GIGABYTE AORUS WATERFORCE II 360", "COL-GIG-WF360", "Gigabyte", 3590000m, 3290000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 27, 250, "Hiệu suất cao, bơm ARGB êm ái."),
            new CoolerSeed("Tản nhiệt nước GIGABYTE AORUS WATERFORCE II 240", "COL-GIG-WF240", "Gigabyte", 2590000m, 2390000m, "AIO Liquid", "LGA1700/AM5/AM4", 240, 120, 27, 200, "Phiên bản 240mm gọn nhẹ."),
            new CoolerSeed("Tản nhiệt khí GIGABYTE AORUS ATC800", "COL-GIG-ATC8", "Gigabyte", 2290000m, 2090000m, "Air Cooler", "LGA1700/AM5/AM4", null, 120, 163, 250, "Tản tháp hầm hố của Aorus."),
            new CoolerSeed("Tản nhiệt nước GIGABYTE AORUS WATERFORCE X 240", "COL-GIG-WFX240", "Gigabyte", 4590000m, 4290000m, "AIO Liquid", "LGA1700/AM5/AM4", 240, 120, 27, 220, "Tích hợp khe cắm thẻ nhớ tải GIF."),
            new CoolerSeed("Tản nhiệt khí Corsair A500", "COL-COR-A500", "Corsair", 2190000m, 1990000m, "Air Cooler", "LGA1700/AM5/AM4", null, 120, 170, 250, "Điều chỉnh độ cao quạt trượt."),
            new CoolerSeed("Tản nhiệt nước ASUS ROG Ryuo III 360 ARGB White", "COL-ASU-RYU33W", "ASUS", 6890000m, 6590000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 30, 300, "Cực phẩm PC trắng."),
            new CoolerSeed("Tản nhiệt nước MSI MEG CORELIQUID S280", "COL-MSI-S280", "MSI", 5990000m, 5690000m, "AIO Liquid", "LGA1700/AM5/AM4", 280, 140, 27, 280, "AIO 280mm siêu tĩnh lặng."),
            new CoolerSeed("Tản nhiệt nước Corsair iCUE H115i ELITE CAPELLIX", "COL-COR-H115C", "Corsair", 4290000m, 3990000m, "AIO Liquid", "LGA1700/AM5/AM4", 280, 140, 27, 280, "Radiator 280mm cực mát."),
            new CoolerSeed("Tản nhiệt nước ASUS TUF Gaming LC 240 ARGB", "COL-ASU-TUF24C", "ASUS", 2290000m, 2090000m, "AIO Liquid", "LGA1700/AM5/AM4", 240, 120, 27, 200, "Độ bền vượt thời gian."),
            new CoolerSeed("Tản nhiệt nước MSI MAG CORELIQUID 240R V2 White", "COL-MSI-240RW", "MSI", 2090000m, 1890000m, "AIO Liquid", "LGA1700/AM5/AM4", 240, 120, 27, 200, "Màu trắng đẹp mắt giá rẻ."),
            new CoolerSeed("Tản nhiệt nước Corsair iCUE LINK H115i RGB", "COL-COR-L115I", "Corsair", 4990000m, 4690000m, "AIO Liquid", "LGA1700/AM5/AM4", 280, 140, 27, 280, "Hỗ trợ iCUE Link 280mm."),
            new CoolerSeed("Tản nhiệt nước GIGABYTE AORUS WATERFORCE X 280", "COL-GIG-WFX280", "Gigabyte", 5290000m, 4990000m, "AIO Liquid", "LGA1700/AM5/AM4", 280, 140, 27, 250, "Làm mát hiệu quả hơn 240mm."),
            new CoolerSeed("Tản nhiệt nước ASUS ROG Strix LC II 360 ARGB White", "COL-ASU-LC236W", "ASUS", 4890000m, 4590000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 27, 280, "Fan ROG trắng tinh xảo."),
            new CoolerSeed("Tản nhiệt nước MSI MPG CORELIQUID K240", "COL-MSI-K240", "MSI", 4290000m, 3990000m, "AIO Liquid", "LGA1700/AM5/AM4", 240, 120, 27, 220, "Màn hình LCD 2.4 inch."),
            new CoolerSeed("Tản nhiệt khí Corsair A115 White", "COL-COR-A115W", "Corsair", 2790000m, 2590000m, "Air Cooler", "LGA1700/AM5/AM4", null, 140, 164, 250, "Tản nhiệt khí kép màu trắng."),
            new CoolerSeed("Tản nhiệt nước ASUS ProArt LC 360", "COL-ASU-PA360", "ASUS", 6590000m, 6290000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 30, 300, "Hoạt động cực êm ái."),
            new CoolerSeed("Tản nhiệt nước MSI MAG CORELIQUID P360", "COL-MSI-P360", "MSI", 2190000m, 1990000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 27, 250, "Bản không LED cắt giảm chi phí."),
            new CoolerSeed("Tản nhiệt nước GIGABYTE WATERFORCE 360", "COL-GIG-W360", "Gigabyte", 2590000m, 2390000m, "AIO Liquid", "LGA1700/AM5/AM4", 360, 120, 27, 250, "Quạt ARGB cơ bản."),
            new CoolerSeed("Tản nhiệt nước Corsair H60x RGB ELITE", "COL-COR-H60X", "Corsair", 1890000m, 1750000m, "AIO Liquid", "LGA1700/AM5/AM4", 120, 120, 27, 150, "AIO 120mm tản sáng đẹp mắt.")
        };

        var products = await LoadCategoryProductsAsync(context, category.Id);
        foreach (var item in catalog)
        {
            var product = FindOrCreateProduct(context, products, category.Id, item.Name, item.Sku);
            product.Name = item.Name;
            product.Sku = item.Sku;
            product.Brand = item.Brand;
            product.Price = item.SalePrice;
            product.IsActive = true;
            product.TechnicalSpec ??= new TechnicalSpec();
            product.TechnicalSpec.Socket = item.SupportedSocket;
            product.TechnicalSpec.TdpWattage = item.TdpSupport;
            product.TechnicalSpec.AdditionalSpecsJson = JsonSerializer.Serialize(new
            {
                coolingType = item.CoolingType,
                radiatorSizeMm = item.RadiatorSizeMm,
                fanSizeMm = item.FanSizeMm,
                heightMm = item.HeightMm,
                regularPrice = item.RegularPrice,
                description = item.Description
            });
        }

        await context.SaveChangesAsync();
    }

    private static async Task<List<Product>> LoadCategoryProductsAsync(AppDbContext context, int categoryId) =>
        await context.Products
            .Include(product => product.TechnicalSpec)
            .Where(product => product.CategoryId == categoryId)
            .ToListAsync();

    private static Product FindOrCreateProduct(AppDbContext context, List<Product> products, int categoryId, string name, string sku)
    {
        var product = products.FirstOrDefault(item =>
            string.Equals(item.Sku, sku, StringComparison.OrdinalIgnoreCase));

        if (product != null)
            return product;

        product = new Product { CategoryId = categoryId, Name = name, Sku = sku, StockQuantity = 10 };
        context.Products.Add(product);
        products.Add(product);
        return product;
    }

    private sealed record PowerSupplySeed(
        string Name, string Sku, string Brand, decimal RegularPrice, decimal SalePrice,
        int Wattage, string EfficiencyRating, string FormFactor, string ModularType, string Description);

    private sealed record CaseSeed(
        string Name, string Sku, string Brand, decimal RegularPrice, decimal SalePrice,
        string FormFactor, int MaxGpuLengthMm, int MaxCoolerHeightMm, string RadiatorSupport,
        int DriveBays, string Description);

    private sealed record CoolerSeed(
        string Name, string Sku, string Brand, decimal RegularPrice, decimal SalePrice,
        string CoolingType, string SupportedSocket, int? RadiatorSizeMm, int FanSizeMm,
        int HeightMm, int TdpSupport, string Description);
}
