using Microsoft.EntityFrameworkCore;
using PCStore.Core.Entities;
using PCStore.Core.Enums;
using PCStore.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// 1. Database Configuration
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2. CORS Configuration
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// ĐẶT UseCors NGAY ĐẦU TIÊN SAU KHI BUILD APP
app.UseCors("AllowAll");

// Khởi tạo Database
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync(@"
            ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""Address"" text;
            ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""IsActive"" boolean DEFAULT true;
        ");
        await DbInitializer.SeedAsync(context);
        Console.WriteLine("--> [DATABASE] Seed Data hoàn tất!");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"--> [DATABASE ERROR]: {ex.Message}");
    }
}

// CÁC ENDPOINT API TIẾP THEO Ở PHÍA DƯỚI...
// ==========================================================
// 4. API AUTHENTICATION (Đăng ký / Đăng nhập / Đăng xuất)
// ==========================================================

app.MapPost("/api/auth/register", async (AppDbContext context, RegisterDto dto) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            return Results.BadRequest(new { message = "Email và mật khẩu không được để trống." });

        string emailClean = dto.Email.Trim().ToLower();
        var existingUser = await context.Users.AnyAsync(u => u.Email.ToLower() == emailClean);
        if (existingUser)
            return Results.Conflict(new { message = "Email này đã được đăng ký tài khoản." });

        var user = new User
        {
            FullName = dto.FullName?.Trim() ?? string.Empty,
            Email = emailClean,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            PhoneNumber = dto.PhoneNumber?.Trim(),
            Address = dto.Address?.Trim(),
            IsActive = true,
            Role = UserRole.Customer,
            CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        return Results.Created($"/api/users/{user.Id}", new
        {
            message = "Đăng ký tài khoản thành công!",
            user = new
            {
                id = user.Id,
                fullName = user.FullName,
                email = user.Email,
                phoneNumber = user.PhoneNumber,
                address = user.Address,
                role = user.Role.ToString()
            }
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.InnerException?.Message ?? ex.Message, statusCode: 500);
    }
});

app.MapPost("/api/auth/login", async (AppDbContext context, LoginDto dto) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            return Results.BadRequest(new { message = "Email và mật khẩu không được để trống." });

        string emailClean = dto.Email.Trim().ToLower();
        var user = await context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == emailClean);
        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return Results.BadRequest(new { message = "Email hoặc mật khẩu không chính xác." });

        if (!user.IsActive)
            return Results.BadRequest(new { message = "Tài khoản hiện đang bị khóa." });

        return Results.Ok(new
        {
            message = "Đăng nhập thành công!",
            user = new
            {
                id = user.Id,
                fullName = user.FullName,
                email = user.Email,
                phoneNumber = user.PhoneNumber,
                address = user.Address,
                role = user.Role.ToString()
            }
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.InnerException?.Message ?? ex.Message, statusCode: 500);
    }
});

app.MapPost("/api/auth/logout", () => Results.Ok(new { message = "Đăng xuất thành công!" }));

// ==========================================================
// 5. API QUẢN LÝ HỒ SƠ CÁ NHÂN & ĐỔI MẬT KHẨU
// ==========================================================

// Lấy thông tin hồ sơ
app.MapGet("/api/users/profile/{id:int}", async (AppDbContext context, int id) =>
{
    var user = await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
    if (user == null)
        return Results.NotFound(new { message = "Không tìm thấy người dùng." });

    return Results.Ok(new
    {
        id = user.Id,
        fullName = user.FullName,
        email = user.Email,
        phoneNumber = user.PhoneNumber ?? "",
        address = user.Address ?? "",
        role = user.Role.ToString(),
        createdAt = user.CreatedAt
    });
});

// Cập nhật thông tin cá nhân (Họ tên, SĐT, Địa chỉ)
app.MapPut("/api/users/profile/{id:int}", async (AppDbContext context, int id, UpdateProfileInfoDto dto) =>
{
    try
    {
        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null)
            return Results.NotFound(new { message = "Không tìm thấy tài khoản người dùng." });

        if (string.IsNullOrWhiteSpace(dto.FullName))
            return Results.BadRequest(new { message = "Họ và tên không được để trống." });

        user.FullName = dto.FullName.Trim();
        user.PhoneNumber = dto.PhoneNumber?.Trim();
        user.Address = dto.Address?.Trim();

        await context.SaveChangesAsync();

        return Results.Ok(new
        {
            message = "Cập nhật thông tin cá nhân thành công!",
            user = new
            {
                id = user.Id,
                fullName = user.FullName,
                email = user.Email,
                phoneNumber = user.PhoneNumber,
                address = user.Address,
                role = user.Role.ToString()
            }
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.InnerException?.Message ?? ex.Message, statusCode: 500);
    }
});
// ==========================================================
// UC: SO SÁNH LINH KIỆN ĐA CHIỀU (SIDE-BY-SIDE COMPARISON)
// ==========================================================
app.MapGet("/api/products/compare", async (AppDbContext context, string ids) =>
{
    if (string.IsNullOrWhiteSpace(ids))
        return Results.BadRequest(new { message = "Danh sách ID không hợp lệ." });

    var idList = ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(id => int.TryParse(id.Trim(), out int val) ? val : 0)
                    .Where(id => id > 0)
                    .Distinct()
                    .Take(4)
                    .ToList();

    if (!idList.Any())
        return Results.BadRequest(new { message = "Không tìm thấy ID hợp lệ." });

    var products = await context.Products
        .Include(p => p.Category)
        .Include(p => p.TechnicalSpec)
        .AsNoTracking()
        .Where(p => idList.Contains(p.Id))
        .Select(p => new
        {
            id = p.Id,
            name = p.Name,
            sku = p.Sku,
            brand = p.Brand,
            price = p.Price,
            stockQuantity = p.StockQuantity,
            imageUrl = p.ImageUrl ?? "",
            categoryType = p.Category.ComponentType.ToString(),
            categoryName = p.Category.Name,
            specs = p.TechnicalSpec != null ? new
            {
                socket = p.TechnicalSpec.Socket,
                chipset = p.TechnicalSpec.Chipset,
                ramType = p.TechnicalSpec.RamType,
                ramSlots = p.TechnicalSpec.RamSlots,
                ramBusSpeed = p.TechnicalSpec.RamBusSpeed,
                tdpWattage = p.TechnicalSpec.TdpWattage,
                recommendedPsu = p.TechnicalSpec.RecommendedPsu,
                formFactor = p.TechnicalSpec.FormFactor
            } : null
        })
        .ToListAsync();

    return Results.Ok(products);
});
// Đổi mật khẩu
app.MapPost("/api/users/change-password/{id:int}", async (AppDbContext context, int id, ChangePasswordDto dto) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(dto.CurrentPassword) || string.IsNullOrWhiteSpace(dto.NewPassword))
            return Results.BadRequest(new { message = "Vui lòng nhập đầy đủ mật khẩu cũ và mới." });

        if (dto.NewPassword.Length < 6)
            return Results.BadRequest(new { message = "Mật khẩu mới phải có ít nhất 6 ký tự." });

        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null)
            return Results.NotFound(new { message = "Không tìm thấy người dùng." });

        if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
            return Results.BadRequest(new { message = "Mật khẩu hiện tại không chính xác." });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        await context.SaveChangesAsync();

        return Results.Ok(new { message = "Đổi mật khẩu thành công!" });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.InnerException?.Message ?? ex.Message, statusCode: 500);
    }
});

// ==========================================================
// 6. API PRODUCTS & CATEGORIES
// ==========================================================

app.MapGet("/api/products", async (AppDbContext context, string? categoryType) =>
{
    var query = context.Products.Include(p => p.Category).Include(p => p.TechnicalSpec).AsNoTracking();

    if (!string.IsNullOrEmpty(categoryType) && categoryType.ToUpper() != "ALL")
    {
        query = query.Where(p => p.Category.ComponentType.ToString().ToLower() == categoryType.ToLower());
    }

    var products = await query.Select(p => new
    {
        id = p.Id,
        name = p.Name,
        sku = p.Sku,
        brand = p.Brand,
        price = p.Price,
        stockQuantity = p.StockQuantity,
        imageUrl = p.ImageUrl ?? "",
        categoryType = p.Category.ComponentType.ToString(),
        categoryName = p.Category.Name,
        socket = p.TechnicalSpec != null ? p.TechnicalSpec.Socket : null,
        chipset = p.TechnicalSpec != null ? p.TechnicalSpec.Chipset : null,
        ramType = p.TechnicalSpec != null ? p.TechnicalSpec.RamType : null,
        ramSlots = p.TechnicalSpec != null ? p.TechnicalSpec.RamSlots : null,
        ramBusSpeed = p.TechnicalSpec != null ? p.TechnicalSpec.RamBusSpeed : null,
        tdpWattage = p.TechnicalSpec != null ? p.TechnicalSpec.TdpWattage : 0,
        recommendedPsu = p.TechnicalSpec != null ? p.TechnicalSpec.RecommendedPsu : 0,
        formFactor = p.TechnicalSpec != null ? p.TechnicalSpec.FormFactor : null
    }).ToListAsync();

    return Results.Ok(products);
});

app.MapGet("/api/products/{id:int}", async (AppDbContext context, int id) =>
{
    var product = await context.Products
        .Include(p => p.Category)
        .Include(p => p.TechnicalSpec)
        .AsNoTracking()
        .FirstOrDefaultAsync(p => p.Id == id);

    if (product == null)
        return Results.NotFound(new { message = "Không tìm thấy sản phẩm." });

    return Results.Ok(new
    {
        id = product.Id,
        name = product.Name,
        sku = product.Sku,
        brand = product.Brand,
        price = product.Price,
        stockQuantity = product.StockQuantity,
        imageUrl = product.ImageUrl ?? "",
        categoryType = product.Category!.ComponentType.ToString(),
        categoryName = product.Category.Name,
        socket = product.TechnicalSpec != null ? product.TechnicalSpec.Socket : null,
        chipset = product.TechnicalSpec != null ? product.TechnicalSpec.Chipset : null,
        ramType = product.TechnicalSpec != null ? product.TechnicalSpec.RamType : null,
        ramSlots = product.TechnicalSpec != null ? product.TechnicalSpec.RamSlots : null,
        ramBusSpeed = product.TechnicalSpec != null ? product.TechnicalSpec.RamBusSpeed : null,
        tdpWattage = product.TechnicalSpec != null ? product.TechnicalSpec.TdpWattage : 0,
        recommendedPsu = product.TechnicalSpec != null ? product.TechnicalSpec.RecommendedPsu : 0,
        formFactor = product.TechnicalSpec != null ? product.TechnicalSpec.FormFactor : null
    });
});

app.MapGet("/api/products/categories", async (AppDbContext context) =>
{
    var categories = await context.Categories.AsNoTracking().Select(c => new
    {
        id = c.Id,
        type = c.ComponentType.ToString(),
        name = c.Name,
        description = c.Description
    }).ToListAsync();

    return Results.Ok(categories);
});

app.Run();

// ==========================================================
// 7. DTO CLASSES
// ==========================================================
public class RegisterDto
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
}

public class LoginDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class UpdateProfileInfoDto
{
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
}

public class ChangePasswordDto
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}