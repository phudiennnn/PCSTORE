using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using PCStore.Core.Entities;
using PCStore.Core.Enums;
using PCStore.Infrastructure.Data;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 1. Database Configuration
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddSignalR();

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
app.MapHub<OrderNotificationsHub>("/hubs/orders");

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
            ALTER TABLE ""Users"" ALTER COLUMN ""PhoneNumber"" DROP NOT NULL;
            ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT NOW();
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Users_PhoneNumber""
                ON ""Users"" (""PhoneNumber"")
                WHERE ""PhoneNumber"" IS NOT NULL AND ""PhoneNumber"" <> '';
            ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""PasswordResetTokenHash"" text;
            ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""PasswordResetExpiresAt"" timestamp with time zone;
            ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""PasswordResetRequestedAt"" timestamp with time zone;
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""CustomerEmail"" text;
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""InventoryReserved"" boolean NOT NULL DEFAULT TRUE;
            CREATE TABLE IF NOT EXISTS ""Reviews"" (
                ""Id"" serial PRIMARY KEY,
                ""ProductId"" integer NOT NULL REFERENCES ""Products""(""Id"") ON DELETE CASCADE,
                ""UserId"" integer NOT NULL REFERENCES ""Users""(""Id"") ON DELETE CASCADE,
                ""Rating"" integer NOT NULL,
                ""Comment"" varchar(1000) NOT NULL DEFAULT '',
                ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT NOW()
            );
            ALTER TABLE ""Reviews"" ADD COLUMN IF NOT EXISTS ""OrderId"" integer REFERENCES ""Orders""(""Id"") ON DELETE SET NULL;
            ALTER TABLE ""Reviews"" ADD COLUMN IF NOT EXISTS ""ImageUrl"" text;
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Reviews_UserId_ProductId""
                ON ""Reviews"" (""UserId"", ""ProductId"") WHERE ""OrderId"" IS NOT NULL;
            CREATE TABLE IF NOT EXISTS ""SystemSettings"" (
                ""Id"" serial PRIMARY KEY,
                ""Key"" varchar(100) NOT NULL UNIQUE,
                ""Value"" text NOT NULL DEFAULT '',
                ""Description"" text
            );
            CREATE TABLE IF NOT EXISTS ""OrderStatusHistories"" (
                ""Id"" serial PRIMARY KEY,
                ""OrderId"" integer NOT NULL REFERENCES ""Orders""(""Id"") ON DELETE CASCADE,
                ""Status"" text NOT NULL,
                ""ChangedAt"" timestamp with time zone NOT NULL DEFAULT NOW(),
                ""TrackingNumber"" varchar(100)
            );
            ALTER TABLE ""OrderStatusHistories"" ADD COLUMN IF NOT EXISTS ""Carrier"" varchar(100);
            ALTER TABLE ""OrderStatusHistories"" ADD COLUMN IF NOT EXISTS ""ChangedByName"" varchar(150);
            ALTER TABLE ""OrderStatusHistories"" ADD COLUMN IF NOT EXISTS ""Note"" varchar(500);
            CREATE INDEX IF NOT EXISTS ""IX_OrderStatusHistories_OrderId_ChangedAt""
                ON ""OrderStatusHistories"" (""OrderId"", ""ChangedAt"");
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

app.MapPost("/api/auth/password-reset/request", async (
    AppDbContext context,
    IWebHostEnvironment environment,
    ForgotPasswordDto dto) =>
{
    if (!environment.IsDevelopment())
        return Results.Problem(
            detail: "Chức năng gửi email đặt lại mật khẩu chưa được cấu hình.",
            statusCode: StatusCodes.Status503ServiceUnavailable);

    if (string.IsNullOrWhiteSpace(dto.Email))
        return Results.BadRequest(new { message = "Vui lòng nhập email." });

    const string genericMessage = "Nếu email tồn tại, hướng dẫn đặt lại mật khẩu sẽ được gửi.";
    var email = dto.Email.Trim().ToLowerInvariant();
    var user = await context.Users.FirstOrDefaultAsync(account => account.Email.ToLower() == email && account.IsActive);
    if (user == null)
        return Results.Ok(new { message = genericMessage });

    var now = DateTime.UtcNow;
    if (user.PasswordResetRequestedAt.HasValue && user.PasswordResetRequestedAt.Value > now.AddMinutes(-1))
        return Results.Ok(new { message = genericMessage });

    var resetToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
    user.PasswordResetTokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resetToken)));
    user.PasswordResetExpiresAt = now.AddMinutes(15);
    user.PasswordResetRequestedAt = now;
    await context.SaveChangesAsync();

    return Results.Ok(new
    {
        message = genericMessage,
        developmentResetToken = environment.IsDevelopment() ? resetToken : null,
        expiresInMinutes = environment.IsDevelopment() ? 15 : (int?)null
    });
});

app.MapPost("/api/auth/password-reset/confirm", async (AppDbContext context, ResetPasswordDto dto) =>
{
    if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.ResetToken))
        return Results.BadRequest(new { message = "Vui lòng nhập email và mã đặt lại." });
    if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 8)
        return Results.BadRequest(new { message = "Mật khẩu mới phải có ít nhất 8 ký tự." });

    var email = dto.Email.Trim().ToLowerInvariant();
    var user = await context.Users.FirstOrDefaultAsync(account => account.Email.ToLower() == email && account.IsActive);
    if (user?.PasswordResetTokenHash == null
        || !user.PasswordResetExpiresAt.HasValue
        || user.PasswordResetExpiresAt.Value <= DateTime.UtcNow)
        return Results.BadRequest(new { message = "Mã đặt lại không hợp lệ hoặc đã hết hạn." });

    var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(dto.ResetToken.Trim()));
    var storedHash = Convert.FromHexString(user.PasswordResetTokenHash);
    if (!CryptographicOperations.FixedTimeEquals(providedHash, storedHash))
        return Results.BadRequest(new { message = "Mã đặt lại không hợp lệ hoặc đã hết hạn." });

    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
    user.UpdatedAt = DateTime.UtcNow;
    user.PasswordResetTokenHash = null;
    user.PasswordResetExpiresAt = null;
    user.PasswordResetRequestedAt = null;
    await context.SaveChangesAsync();

    return Results.Ok(new { message = "Đặt lại mật khẩu thành công. Bạn có thể đăng nhập bằng mật khẩu mới." });
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
        status = user.IsActive ? "ACTIVE" : "INACTIVE",
        createdAt = user.CreatedAt,
        updatedAt = user.UpdatedAt
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
        var phoneNumber = dto.PhoneNumber?.Trim();
        if (phoneNumber?.Length > 20)
            return Results.BadRequest(new { message = "Số điện thoại không được dài quá 20 ký tự." });
        if (!string.IsNullOrWhiteSpace(phoneNumber)
            && await context.Users.AnyAsync(other => other.Id != id && other.PhoneNumber == phoneNumber))
            return Results.Conflict(new { message = "Số điện thoại đã được sử dụng bởi tài khoản khác." });

        user.FullName = dto.FullName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber;
        user.Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim();
        user.UpdatedAt = DateTime.UtcNow;

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
                role = user.Role.ToString(),
                status = user.IsActive ? "ACTIVE" : "INACTIVE",
                createdAt = user.CreatedAt,
                updatedAt = user.UpdatedAt
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
        user.UpdatedAt = DateTime.UtcNow;
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

app.MapGet("/api/products", async (AppDbContext context, string? categoryType, string? searchTerm, decimal? minPrice, decimal? maxPrice, string? sortBy) =>
{
    var query = context.Products.Include(p => p.Category).Include(p => p.TechnicalSpec).AsNoTracking().Where(p => p.IsActive);

    if (!string.IsNullOrEmpty(categoryType) && categoryType.ToUpper() != "ALL")
        query = query.Where(p => p.Category.ComponentType.ToString().ToLower() == categoryType.ToLower());

    if (!string.IsNullOrWhiteSpace(searchTerm))
    {
        var term = searchTerm.Trim().ToLower();
        query = query.Where(p => p.Name.ToLower().Contains(term) || (p.Brand != null && p.Brand.ToLower().Contains(term)) || p.Sku.ToLower().Contains(term));
    }

    if (minPrice.HasValue) query = query.Where(p => p.Price >= minPrice.Value);
    if (maxPrice.HasValue) query = query.Where(p => p.Price <= maxPrice.Value);

    query = sortBy switch
    {
        "price_asc" => query.OrderBy(p => p.Price),
        "price_desc" => query.OrderByDescending(p => p.Price),
        "name_asc" => query.OrderBy(p => p.Name),
        _ => query.OrderBy(p => p.Id)
    };

    var products = await query.Select(p => new
    {
        id = p.Id, name = p.Name, sku = p.Sku, brand = p.Brand, price = p.Price,
        stockQuantity = p.StockQuantity, imageUrl = p.ImageUrl ?? "",
        categoryType = p.Category!.ComponentType.ToString(), categoryName = p.Category.Name,
        socket = p.TechnicalSpec != null ? p.TechnicalSpec.Socket : null,
        chipset = p.TechnicalSpec != null ? p.TechnicalSpec.Chipset : null,
        ramType = p.TechnicalSpec != null ? p.TechnicalSpec.RamType : null,
        ramSlots = p.TechnicalSpec != null ? p.TechnicalSpec.RamSlots : null,
        ramBusSpeed = p.TechnicalSpec != null ? p.TechnicalSpec.RamBusSpeed : null,
        tdpWattage = p.TechnicalSpec != null ? p.TechnicalSpec.TdpWattage : 0,
        recommendedPsu = p.TechnicalSpec != null ? p.TechnicalSpec.RecommendedPsu : 0,
        additionalSpecsJson = p.TechnicalSpec != null ? p.TechnicalSpec.AdditionalSpecsJson : null,
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

    var avgRating = await context.Reviews.Where(r => r.ProductId == id).AverageAsync(r => (double?)r.Rating) ?? 0;
    var reviewCount = await context.Reviews.CountAsync(r => r.ProductId == id);

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
        avgRating = Math.Round(avgRating, 1),
        reviewCount,
        additionalSpecsJson = product.TechnicalSpec?.AdditionalSpecsJson,
        specs = product.TechnicalSpec != null ? new
        {
            socket = product.TechnicalSpec.Socket,
            chipset = product.TechnicalSpec.Chipset,
            ramType = product.TechnicalSpec.RamType,
            ramSlots = product.TechnicalSpec.RamSlots,
            ramBusSpeed = product.TechnicalSpec.RamBusSpeed,
            tdpWattage = product.TechnicalSpec.TdpWattage,
            recommendedPsu = product.TechnicalSpec.RecommendedPsu,
            lengthMm = product.TechnicalSpec.LengthMm,
            formFactor = product.TechnicalSpec.FormFactor
        } : null,
        socket = product.TechnicalSpec?.Socket,
        chipset = product.TechnicalSpec?.Chipset,
        ramType = product.TechnicalSpec?.RamType,
        ramSlots = product.TechnicalSpec?.RamSlots,
        ramBusSpeed = product.TechnicalSpec?.RamBusSpeed,
        tdpWattage = product.TechnicalSpec?.TdpWattage ?? 0,
        recommendedPsu = product.TechnicalSpec?.RecommendedPsu ?? 0,
        lengthMm = product.TechnicalSpec?.LengthMm,
        formFactor = product.TechnicalSpec?.FormFactor
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

// ==========================================================
// 7. API REVIEWS (US15)
// ==========================================================
app.MapGet("/api/products/{id:int}/reviews", async (AppDbContext context, int id, CancellationToken cancellationToken) =>
{
    var reviews = await context.Reviews
        .Include(r => r.User)
        .Where(r => r.ProductId == id)
        .OrderByDescending(r => r.CreatedAt)
        .Select(r => new
        {
            id = r.Id,
            rating = r.Rating,
            comment = r.Comment,
            imageUrl = r.ImageUrl,
            userName = r.User!.FullName,
            createdAt = r.CreatedAt
        }).ToListAsync(cancellationToken);
    return Results.Ok(reviews);
});

app.MapGet("/api/products/{id:int}/reviews/{reviewId:int}", async (
    AppDbContext context,
    int id,
    int reviewId,
    CancellationToken cancellationToken) =>
{
    var review = await context.Reviews
        .AsNoTracking()
        .Where(r => r.Id == reviewId && r.ProductId == id)
        .Select(r => new
        {
            id = r.Id,
            rating = r.Rating,
            comment = r.Comment,
            imageUrl = r.ImageUrl,
            userName = r.User!.FullName,
            createdAt = r.CreatedAt
        })
        .FirstOrDefaultAsync(cancellationToken);
    return review is null ? Results.NotFound() : Results.Ok(review);
})
    .WithSummary("Xem một đánh giá sản phẩm")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);

app.MapPost("/api/products/{id:int}/reviews", async (
    AppDbContext context,
    ILogger<Program> logger,
    int id,
    CreateReviewDto dto,
    CancellationToken cancellationToken) =>
{
    if (dto.Rating < 1 || dto.Rating > 5)
        return Results.BadRequest(new { message = "Đánh giá phải từ 1 đến 5 sao." });
    if (dto.OrderId <= 0)
        return Results.BadRequest(new { message = "Vui lòng chọn đơn hàng đã giao để đánh giá." });
    if (dto.Comment?.Length > 1000)
        return Results.BadRequest(new { message = "Nội dung đánh giá không được vượt quá 1000 ký tự." });
    if (dto.ImageUrl is { Length: > 4_200_000 }
        || (dto.ImageUrl is not null
            && !(dto.ImageUrl.StartsWith("data:image/jpeg;base64,", StringComparison.OrdinalIgnoreCase)
                || dto.ImageUrl.StartsWith("data:image/png;base64,", StringComparison.OrdinalIgnoreCase)
                || dto.ImageUrl.StartsWith("data:image/webp;base64,", StringComparison.OrdinalIgnoreCase))))
        return Results.BadRequest(new { message = "Ảnh đánh giá phải là JPEG, PNG hoặc WebP và tối đa 3 MB." });

    var product = await context.Products.FindAsync([id], cancellationToken);
    if (product == null) return Results.NotFound(new { message = "Không tìm thấy sản phẩm." });
    var user = await context.Users.FindAsync([dto.UserId], cancellationToken);
    if (user == null) return Results.NotFound(new { message = "Không tìm thấy người dùng." });

    var eligibleOrder = await context.Orders
        .AsNoTracking()
        .AnyAsync(order => order.Id == dto.OrderId
            && order.UserId == dto.UserId
            && order.Status == OrderStatus.Completed
            && order.OrderDetails.Any(detail => detail.ProductId == id), cancellationToken);
    if (!eligibleOrder)
        return Results.BadRequest(new { message = "Chỉ có thể đánh giá sản phẩm trong đơn hàng đã giao thành công." });

    if (await context.Reviews.AnyAsync(review => review.ProductId == id && review.UserId == dto.UserId, cancellationToken))
        return Results.Conflict(new { message = "Bạn đã đánh giá sản phẩm này rồi." });

    var review = new Review
    {
        ProductId = id,
        UserId = dto.UserId,
        OrderId = dto.OrderId,
        Rating = dto.Rating,
        Comment = dto.Comment?.Trim() ?? "",
        ImageUrl = dto.ImageUrl
    };
    context.Reviews.Add(review);
    try
    {
        await context.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException exception) when (exception.InnerException is Npgsql.PostgresException
        { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
    {
        logger.LogWarning(exception, "Duplicate product review rejected for user {UserId} and product {ProductId}", dto.UserId, id);
        return Results.Conflict(new { message = "Bạn đã đánh giá sản phẩm này rồi." });
    }

    return Results.Created($"/api/products/{id}/reviews/{review.Id}", new { message = "Đánh giá thành công!", id = review.Id });
})
    .WithSummary("Gửi đánh giá sản phẩm sau khi nhận hàng")
    .WithDescription("Chỉ chấp nhận đánh giá sản phẩm thuộc đơn hàng đã hoàn tất của người dùng; mỗi người dùng chỉ được đánh giá một lần cho mỗi sản phẩm.");

// ==========================================================
// 8. API ORDERS (US12-14, US16-18, US25)
// ==========================================================
app.MapPost("/api/orders", async (
    AppDbContext context,
    CreateOrderDto dto,
    IConfiguration configuration,
    ILogger<Program> logger,
    IHubContext<OrderNotificationsHub> orderNotifications,
    CancellationToken cancellationToken) =>
{
    if (dto.Items is null || dto.Items.Count == 0)
        return Results.BadRequest(new { message = "Giỏ hàng trống." });
    if (dto.Items.Any(item => item.ProductId <= 0 || item.Quantity <= 0)
        || dto.Items.Select(item => item.ProductId).Distinct().Count() != dto.Items.Count)
        return Results.BadRequest(new { message = "Danh sách sản phẩm hoặc số lượng không hợp lệ." });
    if (string.IsNullOrWhiteSpace(dto.ReceiverName)
        || string.IsNullOrWhiteSpace(dto.ReceiverPhone)
        || string.IsNullOrWhiteSpace(dto.ShippingAddress)
        || !MailAddress.TryCreate(dto.Email, out _))
        return Results.BadRequest(new { message = "Vui lòng nhập đầy đủ họ tên, số điện thoại, email và địa chỉ giao hàng hợp lệ." });
    if (!string.Equals(dto.PaymentMethod, "COD", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { message = "Hiện tại chỉ hỗ trợ thanh toán khi nhận hàng (COD)." });

    var productIds = dto.Items.Select(i => i.ProductId).ToList();
    await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    var products = await context.Products
        .Where(product => productIds.Contains(product.Id))
        .ToDictionaryAsync(product => product.Id, cancellationToken);

    decimal total = 0;
    var details = new List<OrderDetail>();
    foreach (var item in dto.Items)
    {
        if (!products.TryGetValue(item.ProductId, out var product) || !product.IsActive)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Results.BadRequest(new { message = $"Sản phẩm ID {item.ProductId} không tồn tại hoặc đã ngừng kinh doanh." });
        }

        var availableQuantity = await context.Products
            .Where(current => current.Id == item.ProductId && current.IsActive)
            .Select(current => (int?)current.StockQuantity)
            .FirstOrDefaultAsync(cancellationToken);
        if (availableQuantity is null || availableQuantity < item.Quantity)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Results.BadRequest(new
            {
                message = availableQuantity is null
                    ? $"Sản phẩm '{product.Name}' không còn kinh doanh."
                    : $"Sản phẩm '{product.Name}' hiện chỉ còn {availableQuantity} sản phẩm; vui lòng cập nhật giỏ hàng."
            });
        }

        total += product.Price * item.Quantity;
        details.Add(new OrderDetail
        {
            ProductId = product.Id,
            Quantity = item.Quantity,
            UnitPrice = product.Price
        });
    }

    var order = new Order
    {
        OrderCode = $"ORD-{DateTime.UtcNow:yyyy}-{Guid.NewGuid():N}"[..21].ToUpperInvariant(),
        TotalAmount = total,
        Status = OrderStatus.Pending,
        ShippingAddress = dto.ShippingAddress?.Trim() ?? "",
        ReceiverPhone = dto.ReceiverPhone?.Trim() ?? "",
        ReceiverName = dto.ReceiverName?.Trim() ?? "",
        CustomerEmail = dto.Email?.Trim(),
        PaymentMethod = "COD",
        IsPaid = false,
        InventoryReserved = false,
        UserId = dto.UserId,
        OrderDetails = details,
        StatusHistory =
        [
            new OrderStatusHistory
            {
                Status = OrderStatus.Pending,
                ChangedAt = DateTime.UtcNow
            }
        ]
    };
    context.Orders.Add(order);
    await context.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    try
    {
        await orderNotifications.Clients.All.SendAsync("OrderCreated", new
        {
            receivedAt = DateTime.UtcNow
        }, cancellationToken);
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "Order {OrderCode} was saved but realtime notifications could not be delivered.", order.OrderCode);
    }

    var emailSent = await SendOrderConfirmationEmailAsync(
        configuration,
        logger,
        dto.Email!,
        order,
        details.Select(detail =>
            $"{products[detail.ProductId].Name} x {detail.Quantity} - {detail.UnitPrice * detail.Quantity:N0} đ").ToList(),
        cancellationToken,
        "Chờ xử lý");

    return Results.Created($"/api/orders/{order.Id}", new
    {
        message = "Đặt hàng thành công!",
        orderId = order.Id,
        orderCode = order.OrderCode,
        totalAmount = total,
        shippingFee = 0m,
        paymentMethod = order.PaymentMethod,
        status = order.Status.ToString(),
        emailSent,
        emailWarning = emailSent ? null : "Đơn hàng đã được tạo nhưng chưa gửi được email xác nhận. Vui lòng kiểm tra cấu hình SMTP."
    });
});

app.MapGet("/api/orders/user/{userId:int}", async (AppDbContext context, int userId, CancellationToken cancellationToken) =>
{
    var orders = await context.Orders
        .Include(o => o.OrderDetails).ThenInclude(d => d.Product)
        .Include(o => o.StatusHistory)
        .Where(o => o.UserId == userId)
        .OrderByDescending(o => o.CreatedAt)
        .Select(o => new
        {
            id = o.Id,
            orderCode = o.OrderCode,
            totalAmount = o.TotalAmount,
            status = o.Status.ToString(),
            paymentMethod = o.PaymentMethod,
            isPaid = o.IsPaid,
            createdAt = o.CreatedAt,
            trackingNumber = o.StatusHistory.Where(h => h.TrackingNumber != null).OrderByDescending(h => h.ChangedAt).Select(h => h.TrackingNumber).FirstOrDefault(),
            carrier = o.StatusHistory.Where(h => h.Carrier != null).OrderByDescending(h => h.ChangedAt).Select(h => h.Carrier).FirstOrDefault(),
            itemCount = o.OrderDetails.Count,
            items = o.OrderDetails.Select(d => new
            {
                productId = d.ProductId,
                productName = d.Product!.Name,
                d.Quantity,
                d.UnitPrice,
                imageUrl = d.Product.ImageUrl,
                hasReviewed = context.Reviews.Any(r => r.UserId == userId && r.ProductId == d.ProductId)
            }).ToList(),
            statusHistory = o.StatusHistory.OrderBy(h => h.ChangedAt).Select(h => new
            {
                status = h.Status.ToString(),
                changedAt = h.ChangedAt,
                trackingNumber = h.TrackingNumber,
                carrier = h.Carrier,
                changedByName = h.ChangedByName,
                note = h.Note
            }).ToList()
        }).ToListAsync(cancellationToken);
    return Results.Ok(orders);
});

app.MapGet("/api/orders/user/{userId:int}/{orderId:int}", async (
    AppDbContext context,
    int userId,
    int orderId,
    CancellationToken cancellationToken) =>
{
    var order = await context.Orders
        .AsNoTracking()
        .Where(o => o.Id == orderId && o.UserId == userId)
        .Select(o => new
        {
            id = o.Id,
            orderCode = o.OrderCode,
            totalAmount = o.TotalAmount,
            status = o.Status.ToString(),
            paymentMethod = o.PaymentMethod,
            isPaid = o.IsPaid,
            createdAt = o.CreatedAt,
            shippingAddress = o.ShippingAddress,
            receiverName = o.ReceiverName,
            receiverPhone = o.ReceiverPhone,
            items = o.OrderDetails.Select(d => new
            {
                productId = d.ProductId,
                productName = d.Product!.Name,
                d.Quantity,
                d.UnitPrice,
                imageUrl = d.Product.ImageUrl,
                hasReviewed = context.Reviews.Any(r => r.UserId == userId && r.ProductId == d.ProductId)
            }).ToList(),
            statusHistory = o.StatusHistory.OrderBy(h => h.ChangedAt).Select(h => new
            {
                status = h.Status.ToString(),
                changedAt = h.ChangedAt,
                trackingNumber = h.TrackingNumber,
                carrier = h.Carrier,
                changedByName = h.ChangedByName,
                note = h.Note
            }).ToList()
        })
        .FirstOrDefaultAsync(cancellationToken);

    return order is null
        ? Results.NotFound(new { message = "Không tìm thấy đơn hàng của bạn." })
        : Results.Ok(order);
})
    .WithSummary("Xem chi tiết đơn hàng của khách hàng")
    .WithDescription("Trả thông tin đơn hàng, sản phẩm và lịch sử trạng thái, chỉ khi đơn thuộc về khách hàng được chỉ định.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);

app.MapGet("/api/orders/{id:int}", async (AppDbContext context, int id) =>
{
    var order = await context.Orders
        .Include(o => o.OrderDetails).ThenInclude(d => d.Product)
        .Include(o => o.User)
        .FirstOrDefaultAsync(o => o.Id == id);
    if (order == null) return Results.NotFound(new { message = "Không tìm thấy đơn hàng." });
    return Results.Ok(new
    {
        id = order.Id,
        orderCode = order.OrderCode,
        totalAmount = order.TotalAmount,
        status = order.Status.ToString(),
        shippingAddress = order.ShippingAddress,
        receiverPhone = order.ReceiverPhone,
        receiverName = order.ReceiverName,
        paymentMethod = order.PaymentMethod,
        isPaid = order.IsPaid,
        createdAt = order.CreatedAt,
        customer = order.User != null ? new { order.User.FullName, order.User.Email, order.User.PhoneNumber } : null,
        items = order.OrderDetails.Select(d => new
        {
            productId = d.ProductId,
            productName = d.Product!.Name,
            sku = d.Product.Sku,
            quantity = d.Quantity,
            unitPrice = d.UnitPrice,
            subtotal = d.UnitPrice * d.Quantity
        }).ToList()
    });
});

app.MapGet("/api/orders", async (AppDbContext context, string? status, CancellationToken cancellationToken) =>
{
    var query = context.Orders.Include(o => o.User).Include(o => o.OrderDetails).Include(o => o.StatusHistory).AsNoTracking();
    if (!string.IsNullOrEmpty(status) && Enum.TryParse<OrderStatus>(status, true, out var st))
        query = query.Where(o => o.Status == st);

    var orders = await query.OrderByDescending(o => o.CreatedAt).Select(o => new
    {
        id = o.Id,
        orderCode = o.OrderCode,
        totalAmount = o.TotalAmount,
        status = o.Status.ToString(),
        receiverName = o.ReceiverName,
        receiverPhone = o.ReceiverPhone,
        customerEmail = o.CustomerEmail ?? (o.User != null ? o.User.Email : ""),
        itemCount = o.OrderDetails.Count,
        createdAt = o.CreatedAt,
        shippingAddress = o.ShippingAddress,
        items = o.OrderDetails.Select(detail => new
        {
            productId = detail.ProductId,
            productName = detail.Product!.Name,
            sku = detail.Product.Sku,
            quantity = detail.Quantity,
            unitPrice = detail.UnitPrice
        }).ToList(),
        statusHistory = o.StatusHistory.OrderBy(history => history.ChangedAt).Select(history => new
        {
            status = history.Status.ToString(),
            changedAt = history.ChangedAt,
            trackingNumber = history.TrackingNumber,
            carrier = history.Carrier,
            changedByName = history.ChangedByName,
            note = history.Note
        }).ToList()
    }).ToListAsync(cancellationToken);
    return Results.Ok(orders);
});

app.MapPost("/api/orders/{id:int}/confirm", async (
    AppDbContext context,
    IConfiguration configuration,
    ILogger<Program> logger,
    IHubContext<OrderNotificationsHub> orderNotifications,
    int id,
    ConfirmOrderDto dto,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(dto.ChangedByName) || dto.ChangedByName.Trim().Length > 150)
        return Results.BadRequest(new { message = "Thiếu thông tin nhân viên xác nhận đơn hàng." });
    if ((dto.Note?.Trim().Length ?? 0) > 500)
        return Results.BadRequest(new { message = "Ghi chú không được vượt quá 500 ký tự." });

    await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    var order = await context.Orders
    .Include(current => current.OrderDetails)
        .ThenInclude(item => item.Product)
    .Include(current => current.StatusHistory)
    .Include(current => current.User)
    .FirstOrDefaultAsync(current => current.Id == id, cancellationToken);

    var claimed = await context.Orders
    .Where(current => current.Id == id && current.Status == OrderStatus.Pending)
    .ExecuteUpdateAsync(updates => updates
        .SetProperty(current => current.Status, OrderStatus.Confirmed),
        cancellationToken);
    if (claimed == 0)
    {
    await transaction.RollbackAsync(cancellationToken);
    return await context.Orders.AnyAsync(current => current.Id == id, cancellationToken)
        ? Results.Conflict(new { message = "Chỉ có thể xác nhận đơn hàng đang ở trạng thái Chờ xử lý." })
        : Results.NotFound(new { message = "Không tìm thấy đơn hàng." });
    }
    if (order is null)
    {
    await transaction.RollbackAsync(cancellationToken);
    return Results.NotFound(new { message = "Không tìm thấy đơn hàng." });
    }
    order.Status = OrderStatus.Confirmed;

    foreach (var item in order.OrderDetails)
    {
    if (order.InventoryReserved)
    {
        var validReservedProduct = await context.Products
            .AnyAsync(product => product.Id == item.ProductId && product.IsActive && product.StockQuantity >= 0, cancellationToken);
        if (!validReservedProduct)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Results.Conflict(new { message = $"Không thể xác nhận: linh kiện {item.Product?.Name ?? $"ID {item.ProductId}"} đã ngừng kinh doanh hoặc thông tin tồn kho không hợp lệ." });
        }
        continue;
    }

    var reserved = await context.Products
        .Where(product => product.Id == item.ProductId
            && product.IsActive
            && product.StockQuantity >= item.Quantity)
        .ExecuteUpdateAsync(updates => updates
            .SetProperty(product => product.StockQuantity, product => product.StockQuantity - item.Quantity),
            cancellationToken);
    if (reserved == 0)
    {
        var product = await context.Products
            .AsNoTracking()
            .Where(current => current.Id == item.ProductId)
            .Select(current => new { current.Name, current.StockQuantity, current.IsActive })
            .FirstOrDefaultAsync(cancellationToken);
        await transaction.RollbackAsync(cancellationToken);
        return Results.Conflict(new
        {
            message = product is null || !product.IsActive
                ? $"Không thể xác nhận: sản phẩm '{item.Product?.Name ?? $"ID {item.ProductId}"}' không còn kinh doanh."
                : $"Không đủ tồn kho để xác nhận '{product.Name}'. Cần {item.Quantity}, hiện còn {product.StockQuantity}."
        });
    }
    }

    order.InventoryReserved = true;
    order.StatusHistory.Add(new OrderStatusHistory
    {
    Status = OrderStatus.Confirmed,
    ChangedAt = DateTime.UtcNow,
    ChangedByName = dto.ChangedByName.Trim(),
    Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim()
    });
    await context.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    try
    {
        await orderNotifications.Clients.All.SendAsync("OrderStatusUpdated", new
        {
            receivedAt = DateTime.UtcNow
        }, cancellationToken);
        await orderNotifications.Clients.All.SendAsync("OrderConfirmed", new
        {
            receivedAt = DateTime.UtcNow
        }, cancellationToken);
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "Order {OrderCode} was confirmed but warehouse notifications could not be delivered.", order.OrderCode);
    }

    var recipient = order.CustomerEmail ?? order.User?.Email;
    var emailSent = !string.IsNullOrWhiteSpace(recipient)
    && await SendOrderStatusEmailAsync(configuration, logger, recipient, order, "Đã xác nhận / Đang đóng gói", cancellationToken);

    return Results.Ok(new
    {
    message = "Đã xác nhận đơn hàng; tồn kho đã được kiểm tra và đặt trước.",
    status = order.Status.ToString(),
    emailSent,
    emailWarning = string.IsNullOrWhiteSpace(recipient)
        ? "Đơn hàng đã xác nhận nhưng chưa có email khách hàng."
        : emailSent ? null : "Đơn hàng đã xác nhận nhưng chưa gửi được email thông báo."
    });
})
    .WithSummary("Xác nhận đơn hàng chờ xử lý")
    .WithDescription("Kiểm tra và trừ tồn kho nguyên tử trước khi chuyển đơn sang Đã xác nhận/Đang đóng gói.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status409Conflict);

app.MapPut("/api/orders/{id:int}/status", async (
    AppDbContext context,
    IConfiguration configuration,
    ILogger<Program> logger,
    IHubContext<OrderNotificationsHub> orderNotifications,
    int id,
    UpdateOrderStatusDto dto,
    CancellationToken cancellationToken) =>
{
    await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    var order = await context.Orders
        .Include(current => current.StatusHistory)
        .Include(current => current.OrderDetails)
            .ThenInclude(item => item.Product)
        .Include(current => current.User)
        .FirstOrDefaultAsync(current => current.Id == id, cancellationToken);
    if (order == null) return Results.NotFound(new { message = "Không tìm thấy đơn hàng." });
    if (!Enum.TryParse<OrderStatus>(dto.Status, true, out var newStatus) || !Enum.IsDefined(newStatus))
        return Results.BadRequest(new { message = "Trạng thái không hợp lệ." });
    if (!IsValidOrderTransition(order.Status, newStatus))
        return Results.Conflict(new { message = $"Không thể chuyển trạng thái từ '{order.Status}' sang '{newStatus}'. Vui lòng đi đúng tiến trình xử lý đơn." });
    if ((dto.TrackingNumber?.Trim().Length ?? 0) > 100)
        return Results.BadRequest(new { message = "Mã vận đơn không được vượt quá 100 ký tự." });
    if (newStatus == OrderStatus.Shipping
        && (string.IsNullOrWhiteSpace(dto.Carrier) || string.IsNullOrWhiteSpace(dto.TrackingNumber)))
        return Results.BadRequest(new { message = "Bắt buộc chọn đơn vị vận chuyển và nhập mã vận đơn trước khi bàn giao." });
    if (newStatus == OrderStatus.Shipping
        && !new[] { "GHN", "GHTK", "Viettel Post", "J&T Express", "VNPost", "Khác" }
            .Contains(dto.Carrier!.Trim(), StringComparer.OrdinalIgnoreCase))
        return Results.BadRequest(new { message = "Đơn vị vận chuyển không hợp lệ." });
    if (newStatus != OrderStatus.Shipping
        && (!string.IsNullOrWhiteSpace(dto.Carrier) || !string.IsNullOrWhiteSpace(dto.TrackingNumber)))
        return Results.BadRequest(new { message = "Chỉ gửi đơn vị vận chuyển và mã vận đơn khi chuyển sang Đang giao hàng." });
    if ((dto.Carrier?.Trim().Length ?? 0) > 100)
        return Results.BadRequest(new { message = "Tên đơn vị vận chuyển không được vượt quá 100 ký tự." });
    if ((dto.Note?.Trim().Length ?? 0) > 500)
        return Results.BadRequest(new { message = "Ghi chú không được vượt quá 500 ký tự." });
    if (string.IsNullOrWhiteSpace(dto.ChangedByName) || dto.ChangedByName.Trim().Length > 150)
        return Results.BadRequest(new { message = "Thiếu thông tin nhân viên thực hiện cập nhật." });

    var trackingNumber = string.IsNullOrWhiteSpace(dto.TrackingNumber) ? null : dto.TrackingNumber.Trim();
    var carrier = string.IsNullOrWhiteSpace(dto.Carrier) ? null : dto.Carrier.Trim();
    var changedByName = dto.ChangedByName.Trim();
    var note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
    var originalStatus = order.Status;
    var claimed = await context.Orders
        .Where(current => current.Id == id && current.Status == originalStatus)
        .ExecuteUpdateAsync(updates => updates
            .SetProperty(current => current.Status, newStatus),
            cancellationToken);
    if (claimed == 0)
    {
        await transaction.RollbackAsync(cancellationToken);
        return Results.Conflict(new { message = "Trạng thái đơn vừa được nhân viên khác cập nhật. Hãy tải lại đơn hàng." });
    }

    if ((newStatus is OrderStatus.Cancelled or OrderStatus.DeliveryFailed) && order.InventoryReserved)
    {
        foreach (var item in order.OrderDetails)
        {
            var restored = await context.Products
                .Where(product => product.Id == item.ProductId)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(product => product.StockQuantity, product => product.StockQuantity + item.Quantity),
                    cancellationToken);
            if (restored == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Results.Conflict(new { message = $"Không thể hoàn kho cho sản phẩm ID {item.ProductId}; trạng thái đơn chưa được cập nhật." });
            }
        }
        order.InventoryReserved = false;
    }
    order.Status = newStatus;
    if (newStatus == OrderStatus.Completed) order.IsPaid = true;
    order.StatusHistory.Add(new OrderStatusHistory
    {
        Status = newStatus,
        ChangedAt = DateTime.UtcNow,
        TrackingNumber = trackingNumber,
        Carrier = carrier,
        ChangedByName = changedByName,
        Note = note
    });
    await context.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    try
    {
        await orderNotifications.Clients.All.SendAsync("OrderStatusUpdated", new
        {
            receivedAt = DateTime.UtcNow
        }, cancellationToken);
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "Order {OrderCode} was updated but realtime status notifications could not be delivered.", order.OrderCode);
    }

    var recipient = order.CustomerEmail ?? order.User?.Email;
    var statusLabel = GetOrderStatusLabel(newStatus);
    var emailSent = !string.IsNullOrWhiteSpace(recipient)
        && await SendOrderStatusEmailAsync(configuration, logger, recipient, order, statusLabel, cancellationToken);

    return Results.Ok(new
    {
        message = "Cập nhật trạng thái thành công!",
        status = order.Status.ToString(),
        carrier,
        trackingNumber,
        emailSent,
        emailWarning = string.IsNullOrWhiteSpace(recipient)
            ? "Đơn hàng đã cập nhật nhưng chưa có email khách hàng."
            : emailSent ? null : "Đơn hàng đã cập nhật nhưng chưa gửi được email thông báo."
    });
})
    .WithSummary("Cập nhật trạng thái và thông tin vận chuyển đơn hàng")
    .WithDescription("Chỉ cho phép chuyển trạng thái theo luồng nghiệp vụ; bắt buộc đơn vị vận chuyển và mã vận đơn khi bàn giao giao hàng.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status409Conflict);

// ==========================================================
// 9. API AI (US8, US11)
// ==========================================================
app.MapPost("/api/ai/build-recommendation", async (AppDbContext context, BuildPcAiRequest request, CancellationToken cancellationToken) =>
{
    var allowedTypes = new HashSet<ComponentType>
    {
        ComponentType.CPU, ComponentType.Mainboard, ComponentType.RAM, ComponentType.GPU,
        ComponentType.SSD, ComponentType.HDD, ComponentType.PSU, ComponentType.Case, ComponentType.Cooler
    };
    var supportedPurposes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "gaming", "graphics", "office", "streaming", "general"
    };

    if (request.Budget < 1_000_000m || request.Budget > 500_000_000m)
        return Results.BadRequest(new { message = "Ngân sách phải từ 1 triệu đến 500 triệu đồng." });
    if (request.CategoryTypes is null || request.CategoryTypes.Count is < 1 or > 9)
        return Results.BadRequest(new { message = "Vui lòng chọn từ 1 đến 9 loại linh kiện." });
    if ((request.Notes?.Length ?? 0) > 500)
        return Results.BadRequest(new { message = "Ghi chú không được vượt quá 500 ký tự." });
    if (!supportedPurposes.Contains(request.Purpose ?? string.Empty))
        return Results.BadRequest(new { message = "Mục đích sử dụng không hợp lệ." });

    var selectedTypes = new List<ComponentType>();
    foreach (var typeName in request.CategoryTypes)
    {
        if (!Enum.TryParse<ComponentType>(typeName, true, out var type) || !allowedTypes.Contains(type))
            return Results.BadRequest(new { message = $"Loại linh kiện không được hỗ trợ: {typeName}." });
        if (selectedTypes.Contains(type))
            return Results.BadRequest(new { message = "Không thể chọn trùng loại linh kiện." });
        selectedTypes.Add(type);
    }

    var catalog = await context.Products
        .Include(product => product.Category)
        .Include(product => product.TechnicalSpec)
        .AsNoTracking()
        .Where(product => product.IsActive
            && selectedTypes.Contains(product.Category!.ComponentType))
        .ToListAsync(cancellationToken);
    var inventory = catalog.Where(product => product.StockQuantity > 0).ToList();

    var categoryOrder = new[]
    {
        ComponentType.CPU, ComponentType.Mainboard, ComponentType.RAM, ComponentType.GPU,
        ComponentType.SSD, ComponentType.HDD, ComponentType.PSU, ComponentType.Case, ComponentType.Cooler
    };
    selectedTypes = categoryOrder.Where(selectedTypes.Contains).ToList();

    var notes = request.Notes?.Trim() ?? string.Empty;
    var requestedRamTypes = new[] { "DDR4", "DDR5" }
        .Where(ramType => notes.Contains(ramType, StringComparison.OrdinalIgnoreCase))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (requestedRamTypes.Count > 0 && !selectedTypes.Contains(ComponentType.RAM))
        return Results.Ok(new
        {
            recommendation = (object?)null,
            message = $"Bạn yêu cầu RAM {string.Join(" hoặc ", requestedRamTypes)}. Vui lòng chọn RAM trong mục linh kiện cần mua."
        });

    bool SupportsRequestedRamType(Product product) =>
        requestedRamTypes.Count == 0
        || requestedRamTypes.Any(ramType =>
            (product.TechnicalSpec?.RamType?.Contains(ramType, StringComparison.OrdinalIgnoreCase) ?? false)
            || product.Name.Contains(ramType, StringComparison.OrdinalIgnoreCase));

    var categoryKeywords = new Dictionary<ComponentType, string[]>
    {
        [ComponentType.CPU] = ["cpu", "vi xử lý", "bộ xử lý"],
        [ComponentType.Mainboard] = ["mainboard", "main", "bo mạch chủ", "motherboard"],
        [ComponentType.RAM] = ["ram", "bộ nhớ"],
        [ComponentType.GPU] = ["gpu", "vga", "card màn hình", "card đồ họa"],
        [ComponentType.SSD] = ["ssd"],
        [ComponentType.HDD] = ["hdd"],
        [ComponentType.PSU] = ["psu", "nguồn"],
        [ComponentType.Case] = ["case", "vỏ case"],
        [ComponentType.Cooler] = ["cooler", "tản nhiệt"]
    };
    var categoryMentions = new List<(ComponentType Type, int Start, int Length)>();
    foreach (var type in selectedTypes)
    {
        foreach (var keyword in categoryKeywords[type])
        {
            var start = 0;
            while ((start = notes.IndexOf(keyword, start, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                categoryMentions.Add((type, start, keyword.Length));
                start += keyword.Length;
            }
        }
    }

    var requestedBrands = new Dictionary<ComponentType, HashSet<string>>();
    var recognizedBrands = new[]
    {
        "AMD", "Intel", "ASUS", "MSI", "Gigabyte", "ASRock", "NVIDIA", "Zotac", "Sapphire",
        "PowerColor", "XFX", "Palit", "GALAX", "Colorful", "Kingston", "Corsair", "G.Skill",
        "TeamGroup", "ADATA", "Crucial", "Samsung", "Western Digital", "Seagate", "Lexar",
        "Kioxia", "Cooler Master", "DeepCool", "Thermaltake", "NZXT", "Lian Li", "Fractal Design",
        "Antec", "FSP", "Seasonic", "SilverStone", "EVGA", "be quiet!"
    };
    var brandsInCatalog = catalog
        .Select(product => product.Brand?.Trim())
        .Where(brand => !string.IsNullOrWhiteSpace(brand))
        .Concat(recognizedBrands)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    foreach (var brand in brandsInCatalog)
    {
        var start = 0;
        while ((start = notes.IndexOf(brand!, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var nearestCategory = categoryMentions
                .Select(mention => new
                {
                    mention.Type,
                    Distance = Math.Max(0, Math.Max(mention.Start - (start + brand!.Length), start - (mention.Start + mention.Length)))
                })
                .OrderBy(mention => mention.Distance)
                .FirstOrDefault();
            if (nearestCategory is not null)
            {
                if (!requestedBrands.TryGetValue(nearestCategory.Type, out var brands))
                {
                    brands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    requestedBrands[nearestCategory.Type] = brands;
                }
                brands.Add(brand!);
            }
            start += brand!.Length;
        }
    }

    var purpose = request.Purpose!.ToLowerInvariant();
    double GetPurposeWeight(ComponentType type) => (purpose, type) switch
    {
        ("gaming", ComponentType.GPU) => 1.8,
        ("gaming", ComponentType.CPU) => 1.35,
        ("streaming", ComponentType.CPU) => 1.65,
        ("streaming", ComponentType.GPU) => 1.45,
        ("graphics", ComponentType.GPU) => 1.7,
        ("graphics", ComponentType.CPU) => 1.4,
        ("graphics", ComponentType.RAM) => 1.35,
        ("office", ComponentType.CPU) => 1.35,
        ("office", ComponentType.RAM) => 1.2,
        ("office", ComponentType.SSD) => 1.25,
        (_, ComponentType.GPU) => 1.45,
        (_, ComponentType.CPU) => 1.3,
        (_, ComponentType.RAM) => 1.15,
        _ => 0.8
    };

    var candidatesByType = new Dictionary<ComponentType, List<BuildCandidate>>();
    foreach (var type in selectedTypes)
    {
        var products = inventory
            .Where(product => product.Category!.ComponentType == type
                && product.Price > 0
                && (type is not (ComponentType.RAM or ComponentType.Mainboard)
                    || SupportsRequestedRamType(product))
                && (!requestedBrands.TryGetValue(type, out var brands)
                    || brands.Contains(product.Brand ?? string.Empty)))
            .OrderBy(product => product.Price)
            .ToList();
        if (products.Count == 0)
            return Results.Ok(new
            {
                recommendation = (object?)null,
                message = requestedRamTypes.Count > 0 && type is (ComponentType.RAM or ComponentType.Mainboard)
                    ? $"Không tìm được {type} còn hàng hỗ trợ RAM {string.Join(" hoặc ", requestedRamTypes)}. Hãy kiểm tra yêu cầu RAM hoặc thay đổi linh kiện cần mua."
                    : requestedBrands.TryGetValue(type, out var requested)
                        ? $"Hiện không có sản phẩm {type} thương hiệu {string.Join(" hoặc ", requested)} còn hàng."
                        : $"Hiện không có sản phẩm {type} còn hàng."
            });

        var metrics = products.Select(product => GetBuildMetric(product, type)).ToArray();
        var minMetric = metrics.Min();
        var maxMetric = metrics.Max();
        var minPrice = products.Min(product => product.Price);
        var maxPrice = products.Max(product => product.Price);
        var weight = GetPurposeWeight(type);

        candidatesByType[type] = products.Select((product, index) =>
        {
            var priceScore = maxPrice == minPrice
                ? 1d
                : (double)((product.Price - minPrice) / (maxPrice - minPrice));
            var metricScore = maxMetric == minMetric
                ? priceScore
                : (metrics[index] - minMetric) / (maxMetric - minMetric);
            return new BuildCandidate(product, weight * (priceScore * 0.55d + metricScore * 0.45d));
        }).ToList();
    }

    var maxAllowedPrice = decimal.Floor(request.Budget * 1.05m);
    var states = new List<BuildSearchState> { new([], 0m, 0d) };
    foreach (var type in selectedTypes)
    {
        var groupedStates = new Dictionary<int, List<BuildSearchState>>();
        foreach (var state in states)
        {
            foreach (var candidate in candidatesByType[type])
            {
                var total = state.TotalPrice + candidate.Product.Price;
                if (total > maxAllowedPrice) continue;

                var parts = new Dictionary<ComponentType, Product>(state.Parts)
                {
                    [type] = candidate.Product
                };
                if (!IsBuildCompatible(parts)) continue;

                var nextState = new BuildSearchState(parts, total, state.Score + candidate.Score);
                var bucket = (int)(total / maxAllowedPrice * 40m);
                if (!groupedStates.TryGetValue(bucket, out var bucketStates))
                {
                    bucketStates = [];
                    groupedStates[bucket] = bucketStates;
                }
                bucketStates.Add(nextState);
            }
        }

        states = groupedStates.Values
            .SelectMany(bucketStates => bucketStates
                .OrderByDescending(state => state.Score + (double)(state.TotalPrice / maxAllowedPrice) * 0.25d)
                .ThenByDescending(state => state.TotalPrice)
                .Take(12))
            .ToList();

        if (states.Count == 0) break;
    }

    var bestBuild = states
        .Where(state => state.Parts.Count == selectedTypes.Count && state.TotalPrice <= maxAllowedPrice)
        .OrderByDescending(state => state.Score + (double)(state.TotalPrice / maxAllowedPrice) * 0.25d)
        .ThenByDescending(state => state.TotalPrice)
        .FirstOrDefault();

    if (bestBuild is null)
        return Results.Ok(new
        {
            recommendation = (object?)null,
            message = "Không tìm được cấu hình còn hàng, tương thích và nằm trong giới hạn ngân sách 105%. Hãy tăng ngân sách hoặc bỏ bớt linh kiện."
        });

    var items = selectedTypes.Select(type =>
    {
        var product = bestBuild.Parts[type];
        return new
        {
            id = product.Id,
            name = product.Name,
            brand = product.Brand,
            price = product.Price,
            stockQuantity = product.StockQuantity,
            categoryType = type.ToString(),
            categoryName = product.Category!.Name,
            socket = product.TechnicalSpec?.Socket,
            ramType = product.TechnicalSpec?.RamType,
            tdpWattage = product.TechnicalSpec?.TdpWattage ?? 0,
            recommendedPsu = product.TechnicalSpec?.RecommendedPsu ?? 0
        };
    }).ToList();

    return Results.Ok(new
    {
        recommendation = new
        {
            items,
            totalPrice = bestBuild.TotalPrice,
            budget = request.Budget,
            maxAllowedPrice,
            budgetUsagePercent = (double)(bestBuild.TotalPrice / request.Budget * 100m),
            message = $"Đã chọn {items.Count} linh kiện còn hàng, ưu tiên {purpose switch
            {
                "gaming" => "chơi game",
                "graphics" => "đồ họa và dựng phim",
                "office" => "văn phòng, học tập",
                "streaming" => "gaming và livestream",
                _ => "nhu cầu đa dụng"
            }}. Tổng tiền không vượt quá 105% ngân sách."
        },
        message = (string?)null
    });
})
    .WithSummary("Tạo cấu hình PC theo ngân sách")
    .WithDescription("Chọn linh kiện đang còn hàng theo mục đích và ghi chú thương hiệu; tổng giá được giới hạn ở mức tối đa 105% ngân sách.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest);

app.MapPost("/api/ai/consult", async (AppDbContext context, AiConsultDto dto) =>
{
    var msg = dto.Message?.Trim() ?? "";
    var products = await context.Products.Include(p => p.Category).Where(p => p.IsActive).ToListAsync();

    var productList = string.Join("\n", products.Select(p => $"- {p.Category?.Name}: {p.Name} (Giá: {p.Price:N0}đ, ID: {p.Id})"));
    
    var prompt = $@"Bạn là nhân viên tư vấn nhiệt tình của PC STORE. Hãy trò chuyện và trả lời câu hỏi của khách hàng một cách tự nhiên.
Câu nói/yêu cầu của khách hàng: ""{msg}""

Danh sách linh kiện hiện có tại cửa hàng (chỉ dùng để tham khảo khi khách nhờ tư vấn cấu hình):
{productList}

Quy tắc bắt buộc:
1. Nếu khách hàng chỉ chào hỏi (ví dụ: xin chào, hi, alo) hoặc hỏi chuyện phiếm, hãy chào lại thân thiện và hỏi xem họ cần tư vấn mua PC/linh kiện gì. TUYỆT ĐỐI KHÔNG tự động đưa ra danh sách cấu hình nếu khách chưa yêu cầu.
2. Nếu khách hàng nhờ tư vấn cấu hình PC hoặc hỏi về linh kiện, hãy dựa vào danh sách trên để gợi ý, trình bày rõ ràng và tính tổng tiền.
3. Không bao giờ gợi ý các linh kiện không có trong danh sách cửa hàng.";

    var apiKey = "AQ.Ab8RN6JthUorBd4bkrLEJ6Qql6q4qspdREVlvxb8vEveHaGy7w";
    var apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash:generateContent?key={apiKey}";

    var requestBody = new
    {
        contents = new[]
        {
            new { parts = new[] { new { text = prompt } } }
        }
    };

    try
    {
        using var httpClient = new HttpClient();
        var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");
        var httpResponse = await httpClient.PostAsync(apiUrl, content);
        
        if (httpResponse.IsSuccessStatusCode)
        {
            var responseString = await httpResponse.Content.ReadAsStringAsync();
            var doc = System.Text.Json.JsonDocument.Parse(responseString);
            var aiText = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();

            return Results.Ok(new { response = aiText, suggestedProducts = Array.Empty<int>() });
        }
        else
        {
            var err = await httpResponse.Content.ReadAsStringAsync();
            return Results.Ok(new { response = "Lỗi khi gọi AI API: " + err, suggestedProducts = Array.Empty<int>() });
        }
    }
    catch (Exception ex)
    {
        return Results.Ok(new { response = "Lỗi hệ thống AI: " + ex.Message, suggestedProducts = Array.Empty<int>() });
    }
});

app.MapGet("/api/ai/upgrade-suggestions", async (AppDbContext context, string productIds) =>
{
    if (string.IsNullOrWhiteSpace(productIds)) return Results.BadRequest(new { message = "Cần productIds." });
    var ids = productIds.Split(',').Select(s => int.TryParse(s.Trim(), out var v) ? v : 0).Where(v => v > 0).ToList();
    var current = await context.Products.Include(p => p.Category).Include(p => p.TechnicalSpec).Where(p => ids.Contains(p.Id)).ToListAsync();
    var suggestions = new List<object>();

    foreach (var p in current)
    {
        var catType = p.Category!.ComponentType;
        var upgrades = await context.Products
            .Include(pr => pr.Category).Include(pr => pr.TechnicalSpec)
            .Where(pr => pr.Category.ComponentType == catType && pr.Price > p.Price && pr.IsActive && pr.StockQuantity > 0)
            .OrderBy(pr => pr.Price).Take(2).ToListAsync();

        foreach (var u in upgrades)
        {
            var reason = catType switch
            {
                ComponentType.CPU => $"Nâng cấp từ {p.Name} lên {u.Name} — hiệu năng cao hơn {(u.Price - p.Price):N0}đ",
                ComponentType.GPU => $"GPU mạnh hơn, FPS game cao hơn ~{(int)((u.Price / p.Price - 1) * 30)}%",
                ComponentType.RAM => $"Thêm dung lượng/tốc độ RAM cho đa nhiệm tốt hơn",
                ComponentType.PSU => $"Nguồn cao công suất hơn, an toàn cho nâng cấp VGA",
                _ => $"Nâng cấp linh kiện {catType}"
            };
            suggestions.Add(new { currentProduct = p.Name, upgradeProduct = u.Name, upgradeId = u.Id, price = u.Price, priceDiff = u.Price - p.Price, reason });
        }
    }
    return Results.Ok(suggestions);
});

// ==========================================================
// 10. API STAFF (US16-20)
// ==========================================================
app.MapGet("/api/staff/customers", async (AppDbContext context, string? search) =>
{
    var query = context.Users.AsNoTracking().Where(u => u.Role == UserRole.Customer);
    if (!string.IsNullOrWhiteSpace(search))
    {
        var term = search.Trim().ToLower();
        query = query.Where(u => u.FullName.ToLower().Contains(term) || u.Email.ToLower().Contains(term) || (u.PhoneNumber != null && u.PhoneNumber.Contains(term)));
    }
    var customers = await query.Select(u => new
    {
        id = u.Id, fullName = u.FullName, email = u.Email, phoneNumber = u.PhoneNumber,
        address = u.Address, isActive = u.IsActive, createdAt = u.CreatedAt,
        orderCount = u.Orders.Count
    }).ToListAsync();
    return Results.Ok(customers);
});

app.MapGet("/api/staff/support-tickets", () =>
{
    return Results.Ok(new[]
    {
        new { id = 1, customer = "Nguyễn Văn A", subject = "Tư vấn nâng cấp RAM DDR5", status = "Open", createdAt = DateTime.UtcNow.AddHours(-2) },
        new { id = 2, customer = "Trần Thị B", subject = "Kiểm tra tương thích CPU AM5", status = "Resolved", createdAt = DateTime.UtcNow.AddDays(-1) }
    });
});

// ==========================================================
// 11. API ADMIN (US21-29, US31-32)
// ==========================================================
app.MapGet("/api/admin/stats", async (AppDbContext context) =>
{
    var totalRevenue = await context.Orders.Where(o => o.Status == OrderStatus.Completed).SumAsync(o => o.TotalAmount);
    var orderCount = await context.Orders.CountAsync();
    var productCount = await context.Products.CountAsync(p => p.IsActive);
    var customerCount = await context.Users.CountAsync(u => u.Role == UserRole.Customer);
    var pendingOrders = await context.Orders.CountAsync(o => o.Status == OrderStatus.Pending);
    var monthlyRevenue = await context.Orders
        .Where(o => o.Status == OrderStatus.Completed && o.CreatedAt >= DateTime.UtcNow.AddDays(-30))
        .GroupBy(o => o.CreatedAt.Date)
        .Select(g => new { date = g.Key, revenue = g.Sum(o => o.TotalAmount), count = g.Count() })
        .OrderBy(x => x.date).ToListAsync();

    return Results.Ok(new { totalRevenue, orderCount, productCount, customerCount, pendingOrders, monthlyRevenue });
});

app.MapGet("/api/admin/users", async (AppDbContext context) =>
{
    var users = await context.Users.AsNoTracking().Select(u => new
    {
        id = u.Id, fullName = u.FullName, email = u.Email, phoneNumber = u.PhoneNumber,
        role = u.Role.ToString(), isActive = u.IsActive, createdAt = u.CreatedAt
    }).ToListAsync();
    return Results.Ok(users);
});

app.MapPut("/api/admin/users/{id:int}/role", async (AppDbContext context, int id, UpdateUserRoleDto dto) =>
{
    var user = await context.Users.FindAsync(id);
    if (user == null) return Results.NotFound(new { message = "Không tìm thấy người dùng." });
    if (!Enum.TryParse<UserRole>(dto.Role, true, out var role)) return Results.BadRequest(new { message = "Vai trò không hợp lệ." });
    user.Role = role;
    await context.SaveChangesAsync();
    return Results.Ok(new { message = "Cập nhật vai trò thành công!" });
});

app.MapPut("/api/admin/users/{id:int}/toggle-active", async (AppDbContext context, int id) =>
{
    var user = await context.Users.FindAsync(id);
    if (user == null) return Results.NotFound(new { message = "Không tìm thấy người dùng." });
    user.IsActive = !user.IsActive;
    await context.SaveChangesAsync();
    return Results.Ok(new { message = user.IsActive ? "Đã kích hoạt tài khoản." : "Đã khóa tài khoản.", isActive = user.IsActive });
});

app.MapPost("/api/admin/categories", async (AppDbContext context, CreateCategoryDto dto) =>
{
    if (!Enum.TryParse<ComponentType>(dto.ComponentType, true, out var ct)) return Results.BadRequest(new { message = "Loại linh kiện không hợp lệ." });
    var cat = new Category { Name = dto.Name.Trim(), ComponentType = ct, Description = dto.Description?.Trim() };
    context.Categories.Add(cat);
    await context.SaveChangesAsync();
    return Results.Created($"/api/admin/categories/{cat.Id}", new { message = "Thêm danh mục thành công!", id = cat.Id });
});

app.MapPut("/api/admin/categories/{id:int}", async (AppDbContext context, int id, CreateCategoryDto dto) =>
{
    var cat = await context.Categories.FindAsync(id);
    if (cat == null) return Results.NotFound(new { message = "Không tìm thấy danh mục." });
    cat.Name = dto.Name.Trim();
    cat.Description = dto.Description?.Trim();
    if (Enum.TryParse<ComponentType>(dto.ComponentType, true, out var ct)) cat.ComponentType = ct;
    await context.SaveChangesAsync();
    return Results.Ok(new { message = "Cập nhật danh mục thành công!" });
});

app.MapPost("/api/admin/products", async (AppDbContext context, CreateProductDto dto) =>
{
    var product = new Product
    {
        Name = dto.Name.Trim(), Sku = dto.Sku.Trim(), Brand = dto.Brand?.Trim(),
        Price = dto.Price, StockQuantity = dto.StockQuantity, CategoryId = dto.CategoryId, IsActive = true,
        TechnicalSpec = dto.Socket != null || dto.RamType != null ? new TechnicalSpec
        {
            Socket = dto.Socket, RamType = dto.RamType, Chipset = dto.Chipset,
            TdpWattage = dto.TdpWattage ?? 0, RecommendedPsu = dto.RecommendedPsu ?? 0, FormFactor = dto.FormFactor
        } : null
    };
    context.Products.Add(product);
    await context.SaveChangesAsync();
    return Results.Created($"/api/admin/products/{product.Id}", new { message = "Thêm sản phẩm thành công!", id = product.Id });
});

app.MapPut("/api/admin/products/{id:int}", async (AppDbContext context, int id, CreateProductDto dto) =>
{
    var product = await context.Products.Include(p => p.TechnicalSpec).FirstOrDefaultAsync(p => p.Id == id);
    if (product == null) return Results.NotFound(new { message = "Không tìm thấy sản phẩm." });
    product.Name = dto.Name.Trim(); product.Sku = dto.Sku.Trim(); product.Brand = dto.Brand?.Trim();
    product.Price = dto.Price; product.StockQuantity = dto.StockQuantity; product.CategoryId = dto.CategoryId;
    if (product.TechnicalSpec == null && (dto.Socket != null || dto.RamType != null))
        product.TechnicalSpec = new TechnicalSpec { ProductId = product.Id };
    if (product.TechnicalSpec != null)
    {
        product.TechnicalSpec.Socket = dto.Socket; product.TechnicalSpec.RamType = dto.RamType;
        product.TechnicalSpec.Chipset = dto.Chipset; product.TechnicalSpec.TdpWattage = dto.TdpWattage ?? 0;
        product.TechnicalSpec.RecommendedPsu = dto.RecommendedPsu ?? 0; product.TechnicalSpec.FormFactor = dto.FormFactor;
    }
    await context.SaveChangesAsync();
    return Results.Ok(new { message = "Cập nhật sản phẩm thành công!" });
});

app.MapPut("/api/admin/products/{id:int}/stock", async (AppDbContext context, int id, UpdateStockDto dto) =>
{
    var product = await context.Products.FindAsync(id);
    if (product == null) return Results.NotFound(new { message = "Không tìm thấy sản phẩm." });
    product.StockQuantity = dto.StockQuantity;
    await context.SaveChangesAsync();
    return Results.Ok(new { message = "Cập nhật tồn kho thành công!", stockQuantity = product.StockQuantity });
});

app.MapPut("/api/admin/products/{id:int}/price", async (AppDbContext context, int id, UpdatePriceDto dto) =>
{
    var product = await context.Products.FindAsync(id);
    if (product == null) return Results.NotFound(new { message = "Không tìm thấy sản phẩm." });
    product.Price = dto.Price;
    await context.SaveChangesAsync();
    return Results.Ok(new { message = "Cập nhật giá thành công!", price = product.Price });
});

app.MapDelete("/api/admin/products/{id:int}", async (AppDbContext context, int id) =>
{
    var product = await context.Products.FindAsync(id);
    if (product == null) return Results.NotFound(new { message = "Không tìm thấy sản phẩm." });
    product.IsActive = false;
    await context.SaveChangesAsync();
    return Results.Ok(new { message = "Đã ẩn sản phẩm." });
});

app.MapGet("/api/admin/settings", async (AppDbContext context) =>
{
    var settings = await context.SystemSettings.AsNoTracking().Select(s => new { s.Key, s.Value, s.Description }).ToListAsync();
    return Results.Ok(settings);
});

app.MapPut("/api/admin/settings", async (AppDbContext context, List<UpdateSettingDto> dtos) =>
{
    foreach (var dto in dtos)
    {
        var setting = await context.SystemSettings.FirstOrDefaultAsync(s => s.Key == dto.Key);
        if (setting != null) setting.Value = dto.Value ?? "";
    }
    await context.SaveChangesAsync();
    return Results.Ok(new { message = "Cập nhật cấu hình hệ thống thành công!" });
});

app.MapGet("/api/admin/backup", async (AppDbContext context) =>
{
    var data = new
    {
        exportedAt = DateTime.UtcNow,
        users = await context.Users.AsNoTracking().Select(u => new { u.FullName, u.Email, u.Role, u.IsActive }).ToListAsync(),
        categories = await context.Categories.AsNoTracking().ToListAsync(),
        products = await context.Products.AsNoTracking().Select(p => new { p.Name, p.Sku, p.Brand, p.Price, p.StockQuantity }).ToListAsync(),
        orders = await context.Orders.AsNoTracking().CountAsync(),
        settings = await context.SystemSettings.AsNoTracking().ToListAsync()
    };
    var setting = await context.SystemSettings.FirstOrDefaultAsync(s => s.Key == "backup_last");
    if (setting != null) { setting.Value = DateTime.UtcNow.ToString("O"); await context.SaveChangesAsync(); }
    return Results.Ok(data);
});

if (app.Environment.IsDevelopment())
{
    app.MapPut("/api/dev/catalog/product-images", async (AppDbContext context, ProductImageBatchDto dto) =>
    {
        if (dto.Images.Count == 0 || dto.Images.Count > 100)
            return Results.BadRequest(new { message = "Mỗi lô cần có từ 1 đến 100 ảnh." });

        if (dto.Images.Any(image => string.IsNullOrWhiteSpace(image.Sku)
            || (!string.IsNullOrWhiteSpace(image.ImageUrl)
                && (!Uri.TryCreate(image.ImageUrl, UriKind.Absolute, out var imageUri)
                    || imageUri.Scheme != Uri.UriSchemeHttps))))
            return Results.BadRequest(new { message = "SKU không hợp lệ hoặc URL ảnh không dùng HTTPS." });

        var requestedSkus = dto.Images.Select(image => image.Sku).ToArray();
        var products = await context.Products
            .Where(product => requestedSkus.Contains(product.Sku))
            .ToListAsync();
        var productsBySku = products.ToDictionary(product => product.Sku, StringComparer.OrdinalIgnoreCase);

        foreach (var image in dto.Images)
        {
            if (productsBySku.TryGetValue(image.Sku, out var product))
                product.ImageUrl = string.IsNullOrWhiteSpace(image.ImageUrl) ? null : image.ImageUrl;
        }

        await context.SaveChangesAsync();
        var matchedSkus = productsBySku.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Results.Ok(new
        {
            updated = products.Count,
            missingSkus = requestedSkus.Where(sku => !matchedSkus.Contains(sku))
        });
    });
}

app.Run();

static async Task<bool> SendOrderConfirmationEmailAsync(
    IConfiguration configuration,
    ILogger logger,
    string recipient,
    Order order,
    IReadOnlyList<string> orderLines,
    CancellationToken cancellationToken,
    string status)
{
    var host = configuration["Email:SmtpHost"];
    var fromAddress = configuration["Email:FromAddress"];
    if (string.IsNullOrWhiteSpace(host)
        || !int.TryParse(configuration["Email:SmtpPort"], out var port)
        || !MailAddress.TryCreate(fromAddress, out var sender))
    {
        logger.LogWarning("Order {OrderCode} was saved, but SMTP email settings are missing or invalid.", order.OrderCode);
        return false;
    }

    var username = configuration["Email:SmtpUsername"];
    var password = configuration["Email:SmtpPassword"];
    if (string.IsNullOrWhiteSpace(username) != string.IsNullOrWhiteSpace(password))
    {
        logger.LogWarning("Order {OrderCode} was saved, but SMTP credentials are incomplete.", order.OrderCode);
        return false;
    }

    try
    {
        var displayName = configuration["Email:FromName"] ?? "PC STORE";
        using var message = new MailMessage
        {
            From = new MailAddress(sender!.Address, displayName),
            Subject = $"Cập nhật đơn hàng {order.OrderCode}: {status}",
            Body = $"""
                Xin chào {order.ReceiverName},

                PC STORE đã cập nhật đơn hàng của bạn.
                Mã đơn hàng: {order.OrderCode}
                Trạng thái: {status}
                Thanh toán: COD (thanh toán khi nhận hàng)
                Giao hàng: Tiêu chuẩn - Miễn phí

                Sản phẩm:
                {string.Join(Environment.NewLine, orderLines)}

                Tổng thanh toán: {order.TotalAmount:N0} đ
                Địa chỉ giao hàng: {order.ShippingAddress}

                Cảm ơn bạn đã mua sắm tại PC STORE.
                """,
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(recipient));

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = !bool.TryParse(configuration["Email:SmtpEnableSsl"], out var enableSsl) || enableSsl
        };
        if (!string.IsNullOrWhiteSpace(username))
            client.Credentials = new NetworkCredential(username, password);

        await client.SendMailAsync(message, cancellationToken);
        return true;
    }
    catch (SmtpException exception)
    {
        logger.LogError(exception, "Order {OrderCode} was saved, but its confirmation email could not be sent.", order.OrderCode);
        return false;
    }
    catch (FormatException exception)
    {
        logger.LogError(exception, "Order {OrderCode} was saved, but its email address configuration is invalid.", order.OrderCode);
        return false;
    }
    catch (InvalidOperationException exception)
    {
        logger.LogError(exception, "Order {OrderCode} was saved, but SMTP could not send its confirmation email.", order.OrderCode);
        return false;
    }
}

static async Task<bool> SendOrderStatusEmailAsync(
    IConfiguration configuration,
    ILogger logger,
    string recipient,
    Order order,
    string status,
    CancellationToken cancellationToken)
{
    var orderLines = order.OrderDetails
        .Select(item => $"{item.Product?.Name ?? $"Sản phẩm {item.ProductId}"} x {item.Quantity} - {item.UnitPrice * item.Quantity:N0} đ")
        .ToList();
    return await SendOrderConfirmationEmailAsync(
        configuration,
        logger,
        recipient,
        order,
        orderLines,
        cancellationToken,
        status);
}

static bool IsValidOrderTransition(OrderStatus current, OrderStatus next) =>
    current switch
    {
        OrderStatus.Pending => next == OrderStatus.Cancelled,
        OrderStatus.Confirmed => next is OrderStatus.Shipping or OrderStatus.Cancelled,
        OrderStatus.Shipping => next is OrderStatus.Completed or OrderStatus.DeliveryFailed,
        _ => false
    };

static string GetOrderStatusLabel(OrderStatus status) =>
    status switch
    {
        OrderStatus.Pending => "Chờ xử lý",
        OrderStatus.Confirmed => "Đã xác nhận / Đang đóng gói",
        OrderStatus.Shipping => "Đã bàn giao vận chuyển / Đang giao hàng",
        OrderStatus.Completed => "Đã giao hàng thành công",
        OrderStatus.Cancelled => "Đã hủy đơn",
        OrderStatus.DeliveryFailed => "Giao hàng thất bại",
        OrderStatus.Refunded => "Đã hoàn tiền",
        _ => status.ToString()
    };

static double GetBuildMetric(Product product, ComponentType type)
{
    var specsJson = product.TechnicalSpec?.AdditionalSpecsJson;
    using var document = string.IsNullOrWhiteSpace(specsJson)
        ? System.Text.Json.JsonDocument.Parse("{}")
        : System.Text.Json.JsonDocument.Parse(specsJson);

    double ReadNumber(string key)
    {
        if (!document.RootElement.TryGetProperty(key, out var value)) return 0d;
        return value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            System.Text.Json.JsonValueKind.String when double.TryParse(value.GetString(), out var number) => number,
            _ => 0d
        };
    }

    return type switch
    {
        ComponentType.CPU => ReadNumber("coreCount") * Math.Max(ReadNumber("boostClock"), ReadNumber("baseClock")),
        ComponentType.RAM => ReadNumber("capacityGb") * Math.Max(product.TechnicalSpec?.RamBusSpeed ?? 0, 1),
        ComponentType.GPU => ReadNumber("vramGb"),
        ComponentType.SSD or ComponentType.HDD =>
            ReadNumber("readSpeed") + ReadNumber("writeSpeed") + ReadNumber("capacityGb") * 0.01d,
        ComponentType.PSU => product.TechnicalSpec?.TdpWattage ?? ReadNumber("wattage"),
        ComponentType.Cooler => ReadNumber("tdpSupport") + ReadNumber("coolingCapacity"),
        _ => 0d
    };
}

static bool IsBuildCompatible(IReadOnlyDictionary<ComponentType, Product> parts)
{
    parts.TryGetValue(ComponentType.CPU, out var cpu);
    parts.TryGetValue(ComponentType.Mainboard, out var mainboard);
    parts.TryGetValue(ComponentType.RAM, out var ram);
    parts.TryGetValue(ComponentType.GPU, out var gpu);
    parts.TryGetValue(ComponentType.PSU, out var psu);

    if (!string.IsNullOrWhiteSpace(cpu?.TechnicalSpec?.Socket)
        && !string.IsNullOrWhiteSpace(mainboard?.TechnicalSpec?.Socket)
        && !string.Equals(cpu.TechnicalSpec.Socket.Trim(), mainboard.TechnicalSpec.Socket.Trim(), StringComparison.OrdinalIgnoreCase))
        return false;

    if (!string.IsNullOrWhiteSpace(mainboard?.TechnicalSpec?.RamType)
        && !string.IsNullOrWhiteSpace(ram?.TechnicalSpec?.RamType))
    {
        var supportedRamTypes = mainboard.TechnicalSpec.RamType
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!supportedRamTypes.Contains(ram.TechnicalSpec.RamType.Trim(), StringComparer.OrdinalIgnoreCase))
            return false;
    }

    if (psu?.TechnicalSpec is { } psuSpec)
    {
        var psuWattage = psuSpec.TdpWattage ?? 0;
        var estimatedPower = (cpu?.TechnicalSpec?.TdpWattage ?? 0)
            + (gpu?.TechnicalSpec?.TdpWattage ?? 0)
            + 60;
        if (psuWattage > 0 && estimatedPower > psuWattage)
            return false;
    }

    return true;
}

// ==========================================================
// DTO CLASSES
// ==========================================================
/// <summary>Parameters for generating a stocked PC build within a strict budget range.</summary>
public sealed record BuildPcAiRequest(decimal Budget, string? Purpose, List<string>? CategoryTypes, string? Notes = null);

/// <summary>Records who confirmed an order and any operational note.</summary>
public sealed record ConfirmOrderDto
{
    public required string ChangedByName { get; init; }
    public string? Note { get; init; }
}

internal sealed record BuildCandidate(Product Product, double Score);

internal sealed record BuildSearchState(
    Dictionary<ComponentType, Product> Parts,
    decimal TotalPrice,
    double Score);

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

public class ForgotPasswordDto
{
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordDto
{
    public string Email { get; set; } = string.Empty;
    public string ResetToken { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
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

public class CreateReviewDto
{
    public int UserId { get; set; }
    public int OrderId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public string? ImageUrl { get; set; }
}

public class CreateOrderDto
{
    public int? UserId { get; set; }
    public string? ShippingAddress { get; set; }
    public string? ReceiverPhone { get; set; }
    public string? ReceiverName { get; set; }
    public string? Email { get; set; }
    public string? PaymentMethod { get; set; }
    public List<OrderItemDto>? Items { get; set; }
}

public class OrderItemDto
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

public class UpdateOrderStatusDto
{
    public string Status { get; set; } = string.Empty;
    public string? TrackingNumber { get; set; }
    public string? Carrier { get; set; }
    public string? Note { get; set; }
    public string? ChangedByName { get; set; }
}

public class AiConsultDto
{
    public string? Message { get; set; }
}

public class UpdateUserRoleDto
{
    public string Role { get; set; } = string.Empty;
}

public class CreateCategoryDto
{
    public string Name { get; set; } = string.Empty;
    public string ComponentType { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class CreateProductDto
{
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public int CategoryId { get; set; }
    public string? Socket { get; set; }
    public string? RamType { get; set; }
    public string? Chipset { get; set; }
    public int? TdpWattage { get; set; }
    public int? RecommendedPsu { get; set; }
    public string? FormFactor { get; set; }
}

public class ProductImageBatchDto
{
    public List<ProductImageUpdateDto> Images { get; set; } = [];
}

public sealed class OrderNotificationsHub : Hub
{
}

public class ProductImageUpdateDto
{
    public string Sku { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
}

public class UpdateStockDto
{
    public int StockQuantity { get; set; }
}

public class UpdatePriceDto
{
    public decimal Price { get; set; }
}

public class UpdateSettingDto
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
}