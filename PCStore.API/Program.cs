using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using PCStore.Core.Ai;
using PCStore.Core.Entities;
using PCStore.Core.Enums;
using PCStore.Infrastructure.Data;
using PCStore.API.Services;
using PCStore.API.Services.Ai;
using PCStore.API.DTOs;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// 1. Database Configuration
var databaseConnectionProvider = new DatabaseConnectionProvider(builder.Configuration);
builder.Services.AddSingleton(databaseConnectionProvider);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(databaseConnectionProvider.ConnectionString));
builder.Services.AddDataProtection();
builder.Services.AddSignalR();
builder.Services.AddHttpClient();
builder.Services.AddPcStoreAi(builder.Configuration);
builder.Services.AddSingleton<OrderStatusEmailQueue>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<OrderStatusEmailQueue>());
builder.Services.AddSingleton<DatabaseBackupService>();

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
app.Use(async (httpContext, next) =>
{
    var backupService = httpContext.RequestServices.GetRequiredService<DatabaseBackupService>();
    if (httpContext.Request.Path.StartsWithSegments("/hubs"))
    {
        await next();
        return;
    }

    if (httpContext.Request.Path.StartsWithSegments("/api/admin/backups"))
    {
        if (backupService.IsRestoring)
        {
            httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await httpContext.Response.WriteAsJsonAsync(new
            {
                message = "Cơ sở dữ liệu đang được phục hồi. Vui lòng thử lại sau.",
            });
            return;
        }

        await next();
        return;
    }

    if (!backupService.TryEnterRequest())
    {
        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await httpContext.Response.WriteAsJsonAsync(new
        {
            message = "Cơ sở dữ liệu đang được phục hồi. Vui lòng thử lại sau.",
        });
        return;
    }

    try
    {
        await next();
    }
    finally
    {
        backupService.ExitRequest();
    }
});
app.Use(async (httpContext, next) =>
{
    var requestContext = httpContext.RequestServices.GetRequiredService<AppDbContext>();
    var initialAuditEntries = requestContext.ChangeTracker.Entries<AuditLog>()
        .Select(entry => entry.Entity)
        .ToHashSet();
    try
    {
        await next();
    }
    catch (Exception exception)
    {
        try
        {
            using var scope = httpContext.RequestServices.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dataProtectionProvider = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
            var actor = await GetActiveAuthenticatedUserAsync(
                context,
                httpContext,
                dataProtectionProvider,
                CancellationToken.None);
            context.AuditLogs.Add(new AuditLog
            {
                Action = "SYSTEM_ERROR",
                Module = "SYSTEM",
                PerformedBy = actor?.Email ?? "Anonymous",
                PerformedByRole = actor?.Role.ToString(),
                IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = GetRequestUserAgent(httpContext),
                Status = "Failed",
                Details = System.Text.Json.JsonSerializer.Serialize(new
                {
                    path = httpContext.Request.Path.Value,
                    errorType = exception.GetType().Name,
                    message = exception.Message,
                }),
                Timestamp = DateTime.UtcNow,
            });
            await context.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception auditException)
        {
            httpContext.RequestServices.GetRequiredService<ILogger<Program>>()
                .LogError(auditException, "Could not persist an audit record for an unhandled API error.");
        }

        throw;
    }

    if (httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError
        && !requestContext.ChangeTracker.Entries<AuditLog>()
            .Any(entry => !initialAuditEntries.Contains(entry.Entity)))
    {
        var errorDataProtection = httpContext.RequestServices.GetRequiredService<IDataProtectionProvider>();
        var errorActor = await GetActiveAuthenticatedUserAsync(
            requestContext,
            httpContext,
            errorDataProtection,
            httpContext.RequestAborted);
        requestContext.AuditLogs.Add(new AuditLog
        {
            Action = "SYSTEM_ERROR",
            Module = "SYSTEM",
            PerformedBy = errorActor?.Email ?? "Anonymous",
            PerformedByRole = errorActor?.Role.ToString(),
            IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = GetRequestUserAgent(httpContext),
            Status = "Failed",
            Details = System.Text.Json.JsonSerializer.Serialize(new
            {
                path = httpContext.Request.Path.Value,
                statusCode = httpContext.Response.StatusCode,
            }),
            Timestamp = DateTime.UtcNow,
        });
    }

    var auditEntries = requestContext.ChangeTracker.Entries<AuditLog>()
        .Where(entry => !initialAuditEntries.Contains(entry.Entity)
            && entry.State is EntityState.Added or EntityState.Unchanged)
        .ToList();
    if (auditEntries.Count == 0) return;

    var dataProtection = httpContext.RequestServices.GetRequiredService<IDataProtectionProvider>();
    var currentActor = await GetActiveAuthenticatedUserAsync(
        requestContext,
        httpContext,
        dataProtection,
        httpContext.RequestAborted);
    foreach (var entry in auditEntries)
    {
        entry.Entity.UserAgent ??= GetRequestUserAgent(httpContext);
        entry.Entity.PerformedByRole ??= currentActor?.Role.ToString();
        entry.Entity.Status = httpContext.Response.StatusCode >= StatusCodes.Status400BadRequest
            ? "Failed"
            : entry.Entity.Status;
    }

    await requestContext.SaveChangesAsync(httpContext.RequestAborted);
});
app.Use(async (httpContext, next) =>
{
    var requirement = GetRolePermissionRequirement(
        httpContext.Request.Path.Value ?? string.Empty,
        httpContext.Request.Method);
    if (requirement is null)
    {
        await next();
        return;
    }

    var context = httpContext.RequestServices.GetRequiredService<AppDbContext>();
    var dataProtectionProvider = httpContext.RequestServices.GetRequiredService<IDataProtectionProvider>();
    var user = await GetActiveAuthenticatedUserAsync(
        context,
        httpContext,
        dataProtectionProvider,
        httpContext.RequestAborted);
    if (user is null)
    {
        httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    var (module, action) = requirement.Value;
    if (module == "auditlogs" && user.Role != UserRole.Admin)
    {
        httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    var allowed = user.Role == UserRole.Admin
        || await context.RolePermissions.AsNoTracking().AnyAsync(permission =>
            permission.Role == user.Role
            && permission.Module == module
            && permission.Action == action
            && permission.IsAllowed,
            httpContext.RequestAborted);
    if (!allowed)
    {
        httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    httpContext.Items["RolePermissionAuthorized"] = true;
    await next();
});

try
{
    var bootstrapConnectionString = databaseConnectionProvider.ConnectionString;
    await using var bootstrapConnection = new NpgsqlConnection(bootstrapConnectionString);
    await bootstrapConnection.OpenAsync();
    await using var command = bootstrapConnection.CreateCommand();
    command.CommandText = """SELECT "Value" FROM "SystemSettings" WHERE "Key" = 'system.database.connection' LIMIT 1""";
    try
    {
        var encryptedConnectionString = await command.ExecuteScalarAsync();
        if (encryptedConnectionString is string protectedValue && !string.IsNullOrWhiteSpace(protectedValue))
        {
            var dataProtection = app.Services.GetRequiredService<IDataProtectionProvider>();
            var connectionString = dataProtection
                .CreateProtector("PCSTORE.API.SystemConfiguration.v1")
                .Unprotect(protectedValue);
            databaseConnectionProvider.SetConnectionString(connectionString);
        }
    }
    catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        // A first-run database has not created SystemSettings yet.
    }
}
catch (NpgsqlException exception)
{
    app.Logger.LogWarning(exception, "Could not load saved database configuration; using the configured bootstrap connection.");
}

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
            CREATE TABLE IF NOT EXISTS ""OrderActionRequests"" (
                ""Id"" serial PRIMARY KEY,
                ""OrderId"" integer NOT NULL REFERENCES ""Orders""(""Id"") ON DELETE CASCADE,
                ""RequestType"" varchar(20) NOT NULL,
                ""Reason"" varchar(500) NOT NULL,
                ""Status"" varchar(20) NOT NULL DEFAULT 'Pending',
                ""RequestedAt"" timestamp with time zone NOT NULL DEFAULT NOW(),
                ""ReviewedByUserId"" integer REFERENCES ""Users""(""Id"") ON DELETE SET NULL,
                ""ReviewedAt"" timestamp with time zone,
                ""ReviewNote"" varchar(500)
            );
            CREATE INDEX IF NOT EXISTS ""IX_OrderActionRequests_OrderId_Status""
                ON ""OrderActionRequests"" (""OrderId"", ""Status"");
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_OrderActionRequests_OrderId_Pending""
                ON ""OrderActionRequests"" (""OrderId"") WHERE ""Status"" = 'Pending';
            ALTER TABLE ""Products"" ADD COLUMN IF NOT EXISTS ""WarehouseLocation"" varchar(100);
            ALTER TABLE ""Products"" ADD COLUMN IF NOT EXISTS ""Description"" varchar(2000);
            ALTER TABLE ""Products"" ADD COLUMN IF NOT EXISTS ""PromotionalPrice"" numeric(18,2);
            ALTER TABLE ""Products"" ADD COLUMN IF NOT EXISTS ""PromotionStartsAt"" timestamp with time zone;
            ALTER TABLE ""Products"" ADD COLUMN IF NOT EXISTS ""PromotionEndsAt"" timestamp with time zone;
            ALTER TABLE ""Products"" ADD COLUMN IF NOT EXISTS ""LowStockThreshold"" integer NOT NULL DEFAULT 5;
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""VoucherCode"" varchar(50);
            ALTER TABLE ""Orders"" ADD COLUMN IF NOT EXISTS ""DiscountAmount"" numeric(18,2) NOT NULL DEFAULT 0;
            CREATE TABLE IF NOT EXISTS ""Vouchers"" (
                ""Id"" serial PRIMARY KEY,
                ""Code"" varchar(50) NOT NULL,
                ""DiscountType"" varchar(20) NOT NULL,
                ""DiscountValue"" numeric(18,2) NOT NULL,
                ""MinimumOrderAmount"" numeric(18,2) NOT NULL DEFAULT 0,
                ""MaximumDiscountAmount"" numeric(18,2),
                ""UsageLimit"" integer NOT NULL,
                ""UsageCount"" integer NOT NULL DEFAULT 0,
                ""StartsAt"" timestamp with time zone NOT NULL,
                ""EndsAt"" timestamp with time zone NOT NULL,
                ""IsActive"" boolean NOT NULL DEFAULT TRUE,
                ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT NOW(),
                ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT NOW()
            );
            ALTER TABLE ""Categories"" ADD COLUMN IF NOT EXISTS ""Slug"" varchar(120) NOT NULL DEFAULT '';
            ALTER TABLE ""Categories"" ADD COLUMN IF NOT EXISTS ""DisplayOrder"" integer NOT NULL DEFAULT 0;
            ALTER TABLE ""Categories"" ADD COLUMN IF NOT EXISTS ""IsActive"" boolean NOT NULL DEFAULT TRUE;
            CREATE TABLE IF NOT EXISTS ""Brands"" (
                ""Id"" serial PRIMARY KEY,
                ""Name"" varchar(100) NOT NULL,
                ""Slug"" varchar(120) NOT NULL,
                ""LogoUrl"" varchar(500),
                ""Description"" varchar(500),
                ""DisplayOrder"" integer NOT NULL DEFAULT 0,
                ""IsActive"" boolean NOT NULL DEFAULT TRUE
            );
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
            ALTER TABLE ""PcBuilds"" ADD COLUMN IF NOT EXISTS ""Purpose"" varchar(30) NOT NULL DEFAULT 'general';
            ALTER TABLE ""AuditLogs"" ADD COLUMN IF NOT EXISTS ""PerformedByRole"" varchar(20);
            ALTER TABLE ""AuditLogs"" ADD COLUMN IF NOT EXISTS ""UserAgent"" varchar(1000);
            ALTER TABLE ""AuditLogs"" ADD COLUMN IF NOT EXISTS ""Status"" varchar(20) NOT NULL DEFAULT 'Success';
            CREATE INDEX IF NOT EXISTS ""IX_AuditLogs_Timestamp"" ON ""AuditLogs"" (""Timestamp"");
            CREATE INDEX IF NOT EXISTS ""IX_AuditLogs_Module_Action_Timestamp""
                ON ""AuditLogs"" (""Module"", ""Action"", ""Timestamp"");
            CREATE TABLE IF NOT EXISTS ""RolePermissions"" (
                ""Id"" serial PRIMARY KEY,
                ""Role"" varchar(20) NOT NULL,
                ""Module"" varchar(50) NOT NULL,
                ""Action"" varchar(20) NOT NULL,
                ""IsAllowed"" boolean NOT NULL DEFAULT FALSE,
                ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT NOW(),
                ""UpdatedBy"" varchar(150)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RolePermissions_Role_Module_Action""
                ON ""RolePermissions"" (""Role"", ""Module"", ""Action"");
            CREATE INDEX IF NOT EXISTS ""IX_Orders_CreatedAt_Status"" ON ""Orders"" (""CreatedAt"", ""Status"");
            CREATE INDEX IF NOT EXISTS ""IX_PcBuilds_CreatedAt_Purpose"" ON ""PcBuilds"" (""CreatedAt"", ""Purpose"");
        ");
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "AiProviderSettings" (
                "Id" serial PRIMARY KEY,
                "ProviderName" varchar(20) NOT NULL,
                "BaseUrl" varchar(300) NOT NULL,
                "ModelName" varchar(120) NOT NULL,
                "FallbackModelNames" text NOT NULL DEFAULT '[]',
                "ApiVersion" varchar(30),
                "ProtectedApiKey" text,
                "KeepAlive" varchar(20),
                "NumCtx" integer,
                "TimeoutSeconds" integer NOT NULL,
                "ConnectTimeoutSeconds" integer NOT NULL,
                "Temperature" numeric(4,2),
                "MaxOutputTokens" integer,
                "IsEnabled" boolean NOT NULL DEFAULT TRUE,
                "IsMaintenanceMode" boolean NOT NULL DEFAULT FALSE,
                "UpdatedAt" timestamp with time zone NOT NULL DEFAULT NOW()
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_AiProviderSettings_ProviderName" ON "AiProviderSettings" ("ProviderName");
            ALTER TABLE "AiProviderSettings" ADD COLUMN IF NOT EXISTS "ConnectTimeoutSeconds" integer NOT NULL DEFAULT 3;
            ALTER TABLE "AiProviderSettings" ADD COLUMN IF NOT EXISTS "FallbackModelNames" text NOT NULL DEFAULT '[]';
            ALTER TABLE "AiProviderSettings" ADD COLUMN IF NOT EXISTS "ApiVersion" varchar(30);
            ALTER TABLE "AiProviderSettings" ADD COLUMN IF NOT EXISTS "ProtectedApiKey" text;
            ALTER TABLE "AiProviderSettings" ADD COLUMN IF NOT EXISTS "KeepAlive" varchar(20);
            ALTER TABLE "AiProviderSettings" ADD COLUMN IF NOT EXISTS "NumCtx" integer;
            ALTER TABLE "AiProviderSettings" ADD COLUMN IF NOT EXISTS "Temperature" numeric(4,2);
            ALTER TABLE "AiProviderSettings" ADD COLUMN IF NOT EXISTS "MaxOutputTokens" integer;
            ALTER TABLE "AiProviderSettings" ADD COLUMN IF NOT EXISTS "IsEnabled" boolean NOT NULL DEFAULT TRUE;
            ALTER TABLE "AiProviderSettings" ADD COLUMN IF NOT EXISTS "IsMaintenanceMode" boolean NOT NULL DEFAULT FALSE;
            ALTER TABLE "AiProviderSettings" ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone NOT NULL DEFAULT NOW();
            CREATE TABLE IF NOT EXISTS "AiRequestLog" (
                "Id" bigserial PRIMARY KEY,
                "Provider" varchar(20) NOT NULL,
                "Intent" varchar(40) NOT NULL,
                "LatencyMilliseconds" integer NOT NULL,
                "IsSuccess" boolean NOT NULL,
                "FailoverReason" varchar(300),
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS "IX_AiRequestLog_CreatedAt" ON "AiRequestLog" ("CreatedAt");
            """);
        await DbInitializer.SeedAsync(context);
        await AiProviderSettingsSeeder.SeedAsync(
            context,
            scope.ServiceProvider.GetRequiredService<IOptions<AiOptions>>().Value,
            scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(),
            scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(AiProviderSettingsSeeder)),
            CancellationToken.None);
        await InitializeCategoryAndBrandCatalogAsync(context);
        await InitializeRolePermissionsAsync(context);
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

app.MapPost("/api/auth/register", async (
    AppDbContext context,
    IDataProtectionProvider dataProtectionProvider,
    RegisterDto dto,
    CancellationToken cancellationToken) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            return Results.BadRequest(new { message = "Email và mật khẩu không được để trống." });

        string emailClean = dto.Email.Trim().ToLower();
        var existingUser = await context.Users.AnyAsync(u => u.Email.ToLower() == emailClean, cancellationToken);
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
        await context.SaveChangesAsync(cancellationToken);
        var accessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(4);
        var accessToken = dataProtectionProvider
            .CreateProtector("PCSTORE.API.AccessToken.v1")
            .Protect($"{user.Id}|{accessTokenExpiresAt.ToUnixTimeSeconds()}");

        return Results.Created($"/api/users/{user.Id}", new
        {
            message = "Đăng ký tài khoản thành công!",
            accessToken,
            accessTokenExpiresAt,
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

app.MapPost("/api/auth/login", async (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    LoginDto dto,
    CancellationToken cancellationToken) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
        {
            context.AuditLogs.Add(new AuditLog
            {
                Action = "LOGIN",
                Module = "AUTH",
                PerformedBy = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim().ToLowerInvariant(),
                IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = GetRequestUserAgent(httpContext),
                Status = "Failed",
                Details = "Email hoặc mật khẩu không được cung cấp.",
                Timestamp = DateTime.UtcNow,
            });
            await context.SaveChangesAsync(cancellationToken);
            return Results.BadRequest(new { message = "Email và mật khẩu không được để trống." });
        }

        string emailClean = dto.Email.Trim().ToLower();
        var user = await context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == emailClean, cancellationToken);
        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
        {
            context.AuditLogs.Add(new AuditLog
            {
                Action = "LOGIN",
                Module = "AUTH",
                PerformedBy = emailClean,
                IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = GetRequestUserAgent(httpContext),
                Status = "Failed",
                Details = "Thông tin đăng nhập không chính xác.",
                Timestamp = DateTime.UtcNow,
            });
            await context.SaveChangesAsync(cancellationToken);
            return Results.BadRequest(new { message = "Email hoặc mật khẩu không chính xác." });
        }

        if (!user.IsActive)
        {
            context.AuditLogs.Add(new AuditLog
            {
                Action = "LOGIN",
                Module = "AUTH",
                PerformedBy = user.Email,
                PerformedByRole = user.Role.ToString(),
                IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = GetRequestUserAgent(httpContext),
                Status = "Failed",
                Details = "Tài khoản đang bị khóa.",
                Timestamp = DateTime.UtcNow,
            });
            await context.SaveChangesAsync(cancellationToken);
            return Results.BadRequest(new { message = "Tài khoản hiện đang bị khóa." });
        }

        context.AuditLogs.Add(new AuditLog
        {
            Action = "LOGIN",
            Module = "AUTH",
            PerformedBy = user.Email,
            PerformedByRole = user.Role.ToString(),
            IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = GetRequestUserAgent(httpContext),
            Status = "Success",
            Details = "Đăng nhập thành công.",
            Timestamp = DateTime.UtcNow,
        });

        var expiresAt = DateTimeOffset.UtcNow.AddHours(4);
        var accessToken = dataProtectionProvider
            .CreateProtector("PCSTORE.API.AccessToken.v1")
            .Protect($"{user.Id}|{expiresAt.ToUnixTimeSeconds()}");

        return Results.Ok(new
        {
            message = "Đăng nhập thành công!",
            accessToken,
            accessTokenExpiresAt = expiresAt,
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
        context.AuditLogs.Add(new AuditLog
        {
            Action = "LOGIN",
            Module = "AUTH",
            PerformedBy = dto.Email?.Trim().ToLowerInvariant(),
            IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = GetRequestUserAgent(httpContext),
            Status = "Failed",
            Details = System.Text.Json.JsonSerializer.Serialize(new { errorType = ex.GetType().Name, message = ex.Message }),
            Timestamp = DateTime.UtcNow,
        });
        await context.SaveChangesAsync(CancellationToken.None);
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

app.MapGet("/api/auth/permissions", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    CancellationToken cancellationToken) =>
{
    var user = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (user is null) return Results.Unauthorized();

    var granted = user.Role == UserRole.Admin
        ? RolePermissionCatalog.Modules
            .SelectMany(module => RolePermissionCatalog.Actions.Select(action =>
                new RolePermissionResponse(module.Key, action.Key, true)))
            .ToList()
        : await context.RolePermissions.AsNoTracking()
            .Where(permission => permission.Role == user.Role && permission.IsAllowed)
            .Select(permission => new RolePermissionResponse(permission.Module, permission.Action, permission.IsAllowed))
            .ToListAsync(cancellationToken);

    return Results.Ok(new { role = user.Role.ToString(), permissions = granted });
})
    .WithSummary("Lấy quyền hiện tại của tài khoản")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized);

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
        .Where(p => idList.Contains(p.Id) && p.Category != null)
        .Select(p => new
        {
            id = p.Id,
            name = p.Name,
            sku = p.Sku,
            brand = p.Brand,
            price = p.PromotionalPrice.HasValue
                && p.PromotionStartsAt <= DateTimeOffset.UtcNow
                && p.PromotionEndsAt > DateTimeOffset.UtcNow
                    ? p.PromotionalPrice.Value
                    : p.Price,
            listPrice = p.Price,
            promotionalPrice = p.PromotionalPrice,
            promotionStartsAt = p.PromotionStartsAt,
            promotionEndsAt = p.PromotionEndsAt,
            stockQuantity = p.StockQuantity,
            warehouseLocation = p.WarehouseLocation,
            imageUrl = p.ImageUrl ?? "",
            categoryType = p.Category!.ComponentType.ToString(),
            categoryName = p.Category!.Name,
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
    var now = DateTimeOffset.UtcNow;
    var query = context.Products.Include(p => p.Category).Include(p => p.TechnicalSpec).AsNoTracking()
        .Where(p => p.IsActive && p.Category != null && p.Category.IsActive);

    if (!string.IsNullOrEmpty(categoryType) && categoryType.ToUpper() != "ALL")
        query = query.Where(p => p.Category!.ComponentType.ToString().ToLower() == categoryType.ToLower());

    if (!string.IsNullOrWhiteSpace(searchTerm))
    {
        var term = searchTerm.Trim().ToLower();
        query = query.Where(p => p.Name.ToLower().Contains(term) || (p.Brand != null && p.Brand.ToLower().Contains(term)) || p.Sku.ToLower().Contains(term));
    }

    if (minPrice.HasValue) query = query.Where(p => (p.PromotionalPrice.HasValue && p.PromotionStartsAt <= now && p.PromotionEndsAt > now ? p.PromotionalPrice.Value : p.Price) >= minPrice.Value);
    if (maxPrice.HasValue) query = query.Where(p => (p.PromotionalPrice.HasValue && p.PromotionStartsAt <= now && p.PromotionEndsAt > now ? p.PromotionalPrice.Value : p.Price) <= maxPrice.Value);

    query = sortBy switch
    {
        "price_asc" => query.OrderBy(p => p.PromotionalPrice.HasValue && p.PromotionStartsAt <= now && p.PromotionEndsAt > now ? p.PromotionalPrice.Value : p.Price),
        "price_desc" => query.OrderByDescending(p => p.PromotionalPrice.HasValue && p.PromotionStartsAt <= now && p.PromotionEndsAt > now ? p.PromotionalPrice.Value : p.Price),
        "name_asc" => query.OrderBy(p => p.Name),
        _ => query.OrderBy(p => p.Id)
    };

    var products = await query.Select(p => new
    {
        id = p.Id, name = p.Name, sku = p.Sku, brand = p.Brand, description = p.Description,
        price = p.PromotionalPrice.HasValue
            && p.PromotionStartsAt <= DateTimeOffset.UtcNow
            && p.PromotionEndsAt > DateTimeOffset.UtcNow
                ? p.PromotionalPrice.Value
                : p.Price,
        listPrice = p.Price,
        promotionalPrice = p.PromotionalPrice,
        promotionStartsAt = p.PromotionStartsAt,
        promotionEndsAt = p.PromotionEndsAt,
        stockQuantity = p.StockQuantity, warehouseLocation = p.WarehouseLocation, imageUrl = p.ImageUrl ?? "",
        availableQuantity = p.StockQuantity,
        categoryType = p.Category!.ComponentType.ToString(), categoryName = p.Category!.Name,
        socket = p.TechnicalSpec != null ? p.TechnicalSpec.Socket : null,
        chipset = p.TechnicalSpec != null ? p.TechnicalSpec.Chipset : null,
        ramType = p.TechnicalSpec != null ? p.TechnicalSpec.RamType : null,
        ramSlots = p.TechnicalSpec != null ? p.TechnicalSpec.RamSlots : null,
        ramBusSpeed = p.TechnicalSpec != null ? p.TechnicalSpec.RamBusSpeed : null,
        tdpWattage = p.TechnicalSpec != null ? p.TechnicalSpec.TdpWattage : 0,
        recommendedPsu = p.TechnicalSpec != null ? p.TechnicalSpec.RecommendedPsu : 0,
        lengthMm = p.TechnicalSpec != null ? p.TechnicalSpec.LengthMm : null,
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
        .FirstOrDefaultAsync(p => p.Id == id && p.IsActive && p.Category != null && p.Category.IsActive);

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
        description = product.Description,
        price = GetEffectiveProductPrice(product, DateTimeOffset.UtcNow),
        listPrice = product.Price,
        promotionalPrice = product.PromotionalPrice,
        promotionStartsAt = product.PromotionStartsAt,
        promotionEndsAt = product.PromotionEndsAt,
        stockQuantity = product.StockQuantity,
        availableQuantity = product.StockQuantity,
        warehouseLocation = product.WarehouseLocation,
        imageUrl = product.ImageUrl ?? "",
        categoryType = product.Category!.ComponentType.ToString(),
        categoryName = product.Category!.Name,
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
    var categories = await context.Categories
        .AsNoTracking()
        .Where(category => category.IsActive)
        .OrderBy(category => category.DisplayOrder)
        .ThenBy(category => category.Name)
        .Select(c => new
    {
        id = c.Id,
        type = c.ComponentType.ToString(),
        name = c.Name,
        slug = c.Slug,
        description = c.Description,
        displayOrder = c.DisplayOrder
    }).ToListAsync();

    return Results.Ok(categories);
});

app.MapGet("/api/products/brands", async (AppDbContext context, CancellationToken cancellationToken) =>
{
    var brands = await context.Brands
        .AsNoTracking()
        .Where(brand => brand.IsActive)
        .OrderBy(brand => brand.DisplayOrder)
        .ThenBy(brand => brand.Name)
        .Select(brand => new
        {
            id = brand.Id,
            name = brand.Name,
            slug = brand.Slug,
            logoUrl = brand.LogoUrl,
            displayOrder = brand.DisplayOrder
        })
        .ToListAsync(cancellationToken);
    return Results.Ok(brands);
})
    .WithSummary("Danh sách thương hiệu đang hoạt động")
    .WithDescription("Cung cấp thương hiệu cho bộ lọc sản phẩm trên website.")
    .Produces(StatusCodes.Status200OK);

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
    var pricingTime = DateTimeOffset.UtcNow;
    var products = await context.Products
        .Include(product => product.Category)
        .Where(product => productIds.Contains(product.Id))
        .ToDictionaryAsync(product => product.Id, cancellationToken);

    decimal total = 0;
    var details = new List<OrderDetail>();
    foreach (var item in dto.Items)
    {
        if (!products.TryGetValue(item.ProductId, out var product)
            || !product.IsActive
            || product.Category is null
            || !product.Category.IsActive)
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

        var unitPrice = GetEffectiveProductPrice(product, pricingTime);
        total += unitPrice * item.Quantity;
        details.Add(new OrderDetail
        {
            ProductId = product.Id,
            Quantity = item.Quantity,
            UnitPrice = unitPrice
        });
    }

    var subtotal = total;
    var discountAmount = 0m;
    Voucher? appliedVoucher = null;
    if (!string.IsNullOrWhiteSpace(dto.VoucherCode))
    {
        var voucherCode = dto.VoucherCode.Trim().ToUpperInvariant();
        appliedVoucher = await context.Vouchers
            .FromSqlInterpolated($"SELECT * FROM \"Vouchers\" WHERE LOWER(\"Code\") = {voucherCode.ToLowerInvariant()} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);
        if (appliedVoucher is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Results.BadRequest(new { message = "Mã voucher không tồn tại." });
        }

        var voucherError = GetVoucherEligibilityError(appliedVoucher, subtotal, pricingTime);
        if (voucherError is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Results.BadRequest(new { message = voucherError });
        }

        discountAmount = CalculateVoucherDiscount(appliedVoucher, subtotal);
        appliedVoucher.UsageCount++;
        appliedVoucher.UpdatedAt = pricingTime;
        context.AuditLogs.Add(new AuditLog
        {
            Action = "USE_VOUCHER",
            Module = "PRICING",
            PerformedBy = dto.Email?.Trim(),
            IpAddress = null,
            Details = $"Voucher {appliedVoucher.Code} được áp dụng cho đơn hàng; giảm {discountAmount:N0} từ đơn {subtotal:N0}.",
            Timestamp = pricingTime.UtcDateTime
        });
    }

    total = subtotal - discountAmount;
    var order = new Order
    {
        OrderCode = $"ORD-{DateTime.UtcNow:yyyy}-{Guid.NewGuid():N}"[..21].ToUpperInvariant(),
        TotalAmount = total,
        VoucherCode = appliedVoucher?.Code,
        DiscountAmount = discountAmount,
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
        subtotal,
        discountAmount,
        voucherCode = appliedVoucher?.Code,
        shippingFee = 0m,
        paymentMethod = order.PaymentMethod,
        status = order.Status.ToString(),
        emailSent,
        emailWarning = emailSent ? null : "Đơn hàng đã được tạo nhưng chưa gửi được email xác nhận. Vui lòng kiểm tra cấu hình SMTP."
    });
});

app.MapGet("/api/orders/user/{userId:int}", async (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    int userId,
    CancellationToken cancellationToken) =>
{
    var customer = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (customer is null) return Results.Unauthorized();
    if (customer.Role != UserRole.Customer || customer.Id != userId)
        return Results.StatusCode(StatusCodes.Status403Forbidden);
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
            voucherCode = o.VoucherCode,
            discountAmount = o.DiscountAmount,
            actionRequests = o.ActionRequests.OrderByDescending(request => request.RequestedAt).Select(request => new
            {
                requestType = request.RequestType,
                reason = request.Reason,
                status = request.Status,
                requestedAt = request.RequestedAt,
                reviewNote = request.ReviewNote
            }).ToList(),
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
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    int userId,
    int orderId,
    CancellationToken cancellationToken) =>
{
    var customer = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (customer is null) return Results.Unauthorized();
    if (customer.Role != UserRole.Customer || customer.Id != userId)
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    var order = await context.Orders
        .AsNoTracking()
        .Where(o => o.Id == orderId && o.UserId == userId)
        .Select(o => new
        {
            id = o.Id,
            orderCode = o.OrderCode,
            totalAmount = o.TotalAmount,
            voucherCode = o.VoucherCode,
            discountAmount = o.DiscountAmount,
            actionRequests = o.ActionRequests.OrderByDescending(request => request.RequestedAt).Select(request => new
            {
                requestType = request.RequestType,
                reason = request.Reason,
                status = request.Status,
                requestedAt = request.RequestedAt,
                reviewNote = request.ReviewNote
            }).ToList(),
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

app.MapGet("/api/orders/lookup", async (
    AppDbContext context,
    string orderCode,
    string receiverPhone,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(orderCode) || string.IsNullOrWhiteSpace(receiverPhone))
        return Results.BadRequest(new { message = "Vui lòng nhập mã đơn hàng và số điện thoại người nhận." });

    var normalizedCode = orderCode.Trim().ToLower();
    var normalizedPhone = receiverPhone.Trim();
    var order = await context.Orders
        .AsNoTracking()
        .Where(current => current.OrderCode.ToLower() == normalizedCode
            && current.ReceiverPhone == normalizedPhone)
        .Select(current => new
        {
            orderCode = current.OrderCode,
            status = current.Status.ToString(),
            carrier = current.StatusHistory
                .Where(history => history.Carrier != null)
                .OrderByDescending(history => history.ChangedAt)
                .Select(history => history.Carrier)
                .FirstOrDefault(),
            trackingNumber = current.StatusHistory
                .Where(history => history.TrackingNumber != null)
                .OrderByDescending(history => history.ChangedAt)
                .Select(history => history.TrackingNumber)
                .FirstOrDefault(),
            statusHistory = current.StatusHistory
                .OrderBy(history => history.ChangedAt)
                .Select(history => new
                {
                    status = history.Status.ToString(),
                    changedAt = history.ChangedAt,
                    carrier = history.Carrier,
                    trackingNumber = history.TrackingNumber,
                    note = history.Note
                })
                .ToList()
        })
        .FirstOrDefaultAsync(cancellationToken);

    return order is null
        ? Results.NotFound(new { message = "Không tìm thấy đơn hàng hoặc thông tin xác minh không khớp." })
        : Results.Ok(order);
})
    .WithSummary("Tra cứu trạng thái đơn bằng mã đơn và số điện thoại")
    .WithDescription("Chỉ trả trạng thái và thông tin vận chuyển sau khi mã đơn khớp số điện thoại người nhận.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound);

app.MapGet("/api/orders/{id:int}", async (AppDbContext context, int id) =>
{
    var order = await context.Orders
        .Include(o => o.OrderDetails).ThenInclude(d => d.Product)
        .Include(o => o.User)
        .Include(o => o.StatusHistory)
        .FirstOrDefaultAsync(o => o.Id == id);
    if (order == null) return Results.NotFound(new { message = "Không tìm thấy đơn hàng." });
    return Results.Ok(new
    {
        id = order.Id,
        orderCode = order.OrderCode,
        totalAmount = order.TotalAmount,
        voucherCode = order.VoucherCode,
        discountAmount = order.DiscountAmount,
        status = order.Status.ToString(),
        shippingAddress = order.ShippingAddress,
        receiverPhone = order.ReceiverPhone,
        receiverName = order.ReceiverName,
        paymentMethod = order.PaymentMethod,
        isPaid = order.IsPaid,
        createdAt = order.CreatedAt,
        shippingFee = 0m,
        subtotal = order.OrderDetails.Sum(detail => detail.UnitPrice * detail.Quantity),
        carrier = order.StatusHistory.Where(history => history.Carrier != null).OrderByDescending(history => history.ChangedAt).Select(history => history.Carrier).FirstOrDefault(),
        trackingNumber = order.StatusHistory.Where(history => history.TrackingNumber != null).OrderByDescending(history => history.ChangedAt).Select(history => history.TrackingNumber).FirstOrDefault(),
        customer = order.User != null ? new { order.User.FullName, order.User.Email, order.User.PhoneNumber } : null,
        items = order.OrderDetails.Select(d => new
        {
            productId = d.ProductId,
            productName = d.Product!.Name,
            sku = d.Product.Sku,
            warehouseLocation = d.Product.WarehouseLocation,
            quantity = d.Quantity,
            unitPrice = d.UnitPrice,
            subtotal = d.UnitPrice * d.Quantity
        }).ToList()
    });
});

app.MapPost("/api/orders/{id:int}/documents/print", async (
    AppDbContext context,
    HttpContext httpContext,
    int id,
    PrintOrderDocumentRequest request,
    CancellationToken cancellationToken) =>
{
    if (request.DocumentType is not ("packing-slip" or "invoice"))
        return Results.BadRequest(new { message = "Loại chứng từ không hợp lệ." });
    if (string.IsNullOrWhiteSpace(request.ChangedByName) || request.ChangedByName.Trim().Length > 150)
        return Results.BadRequest(new { message = "Thiếu thông tin nhân viên xuất chứng từ." });

    var order = await context.Orders
        .AsNoTracking()
        .Where(current => current.Id == id)
        .Select(current => new { current.Id, current.OrderCode, current.Status })
        .FirstOrDefaultAsync(cancellationToken);
    if (order is null)
        return Results.NotFound(new { message = "Không tìm thấy đơn hàng." });
    if (order.Status is not (OrderStatus.Confirmed or OrderStatus.Shipping))
        return Results.Conflict(new { message = "Chỉ được xuất chứng từ khi đơn hàng đã xác nhận hoặc đang giao hàng." });

    var documentName = request.DocumentType == "packing-slip" ? "Phiếu đóng gói" : "Hóa đơn bán hàng";
    context.AuditLogs.Add(new AuditLog
    {
        Action = "PRINT_ORDER_DOCUMENT",
        Module = "ORDERS",
        PerformedBy = request.ChangedByName.Trim(),
        IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
        Details = $"Đã yêu cầu in {documentName} cho đơn {order.OrderCode} (ID: {order.Id}).",
        Timestamp = DateTime.UtcNow
    });
    await context.SaveChangesAsync(cancellationToken);

    return Results.Ok(new
    {
        message = $"{documentName} đã được ghi nhận để in.",
        orderCode = order.OrderCode,
        documentType = request.DocumentType,
        printedAt = DateTime.UtcNow
    });
})
    .WithSummary("Ghi nhận yêu cầu in chứng từ đơn hàng")
    .WithDescription("Chỉ ghi nhận in phiếu đóng gói hoặc hóa đơn cho đơn đã xác nhận hoặc đang giao; lưu nhân viên, thời điểm và địa chỉ IP vào AuditLog.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status409Conflict);

app.MapPost("/api/orders/{id:int}/requests", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    IHubContext<OrderNotificationsHub> orderNotifications,
    ILogger<Program> logger,
    int id,
    CreateOrderActionRequest request,
    CancellationToken cancellationToken) =>
{
    var customer = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (customer is null) return Results.Unauthorized();
    if (customer.Role != UserRole.Customer) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var requestType = request.RequestType?.Trim();
    if (!string.Equals(requestType, "Cancel", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(requestType, "Refund", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { message = "Chỉ hỗ trợ yêu cầu hủy đơn hoặc hoàn trả linh kiện." });
    if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
        return Results.BadRequest(new { message = "Vui lòng nhập lý do từ 1 đến 500 ký tự." });

    var order = await context.Orders
        .Include(current => current.ActionRequests)
        .FirstOrDefaultAsync(current => current.Id == id && current.UserId == customer.Id, cancellationToken);
    if (order is null) return Results.NotFound(new { message = "Không tìm thấy đơn hàng thuộc tài khoản của bạn." });
    var normalizedType = string.Equals(requestType, "Cancel", StringComparison.OrdinalIgnoreCase) ? "Cancel" : "Refund";
    var isEligible = normalizedType == "Cancel"
        ? order.Status is OrderStatus.Pending or OrderStatus.Confirmed
        : order.Status == OrderStatus.Completed;
    if (!isEligible)
        return Results.Conflict(new
        {
            message = normalizedType == "Cancel"
                ? "Chỉ có thể yêu cầu hủy đơn đang chờ xử lý hoặc đang đóng gói."
                : "Chỉ có thể yêu cầu hoàn trả linh kiện sau khi đơn đã giao thành công."
        });
    if (order.ActionRequests.Any(actionRequest => actionRequest.Status == "Pending"))
        return Results.Conflict(new { message = "Đơn hàng đã có yêu cầu đang chờ quản lý phê duyệt." });

    var actionRequest = new OrderActionRequest
    {
        OrderId = order.Id,
        RequestType = normalizedType,
        Reason = request.Reason.Trim(),
        Status = "Pending",
        RequestedAt = DateTimeOffset.UtcNow
    };
    context.OrderActionRequests.Add(actionRequest);
    try
    {
        await context.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException exception) when (
        exception.InnerException is Npgsql.PostgresException
        {
            SqlState: Npgsql.PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_OrderActionRequests_OrderId_Pending"
        })
    {
        app.Logger.LogInformation(exception, "A pending order action request already exists for order {OrderId}.", order.Id);
        return Results.Conflict(new { message = "Đơn hàng đã có yêu cầu xử lý đang chờ phê duyệt." });
    }
    try
    {
        await orderNotifications.Clients.All.SendAsync("OrderActionRequested", new
        {
            requestId = actionRequest.Id,
            orderId = order.Id,
            orderCode = order.OrderCode,
            requestType = actionRequest.RequestType,
            receivedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "Order action request {RequestId} was saved but management notifications could not be delivered.", actionRequest.Id);
    }
    return Results.Created($"/api/orders/{id}/requests/{actionRequest.Id}", new
    {
        actionRequest.Id,
        actionRequest.RequestType,
        actionRequest.Status,
        actionRequest.Reason,
        actionRequest.RequestedAt
    });
})
    .WithSummary("Yêu cầu hủy đơn hoặc hoàn trả linh kiện")
    .WithDescription("Khách hàng tạo yêu cầu cho đơn của mình; chỉ Manager/Admin có thể duyệt hoặc từ chối.")
    .Produces(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status409Conflict);

app.MapGet("/api/orders/{id:int}/requests/{requestId:int}", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    int id,
    int requestId,
    CancellationToken cancellationToken) =>
{
    var customer = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (customer is null) return Results.Unauthorized();
    if (customer.Role != UserRole.Customer) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var actionRequest = await context.OrderActionRequests
        .AsNoTracking()
        .Where(item => item.Id == requestId
            && item.OrderId == id
            && item.Order != null
            && item.Order.UserId == customer.Id)
        .Select(item => new
        {
            item.Id,
            item.OrderId,
            item.RequestType,
            item.Reason,
            item.Status,
            item.RequestedAt,
            item.ReviewNote,
            item.ReviewedAt
        })
        .FirstOrDefaultAsync(cancellationToken);
    return actionRequest is null
        ? Results.NotFound(new { message = "Không tìm thấy yêu cầu của bạn." })
        : Results.Ok(actionRequest);
})
    .WithSummary("Xem trạng thái yêu cầu hủy/hoàn")
    .WithDescription("Chỉ chủ sở hữu đơn hàng xem được yêu cầu của chính mình.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound);

app.MapGet("/api/admin/order-requests", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    string? status,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!string.IsNullOrWhiteSpace(status)
        && !new[] { "Pending", "Approved", "Rejected" }.Contains(status, StringComparer.OrdinalIgnoreCase))
        return Results.BadRequest(new { message = "Trạng thái yêu cầu không hợp lệ." });

    var query = context.OrderActionRequests
        .AsNoTracking()
        .Include(actionRequest => actionRequest.Order)
        .ThenInclude(order => order!.User)
        .AsQueryable();
    if (!string.IsNullOrWhiteSpace(status))
        query = query.Where(actionRequest => actionRequest.Status.ToLower() == status.Trim().ToLower());
    var requests = await query
        .OrderByDescending(actionRequest => actionRequest.RequestedAt)
        .Select(actionRequest => new
        {
            id = actionRequest.Id,
            orderId = actionRequest.OrderId,
            orderCode = actionRequest.Order!.OrderCode,
            orderStatus = actionRequest.Order.Status.ToString(),
            totalAmount = actionRequest.Order.TotalAmount,
            requestType = actionRequest.RequestType,
            reason = actionRequest.Reason,
            status = actionRequest.Status,
            requestedAt = actionRequest.RequestedAt,
            customerName = actionRequest.Order.User != null
                ? actionRequest.Order.User.FullName
                : actionRequest.Order.ReceiverName,
            customerEmail = actionRequest.Order.CustomerEmail
                ?? (actionRequest.Order.User != null ? actionRequest.Order.User.Email : null),
            reviewNote = actionRequest.ReviewNote,
            reviewedAt = actionRequest.ReviewedAt,
            reviewedBy = actionRequest.ReviewedByUser != null ? actionRequest.ReviewedByUser.Email : null
        })
        .ToListAsync(cancellationToken);
    return Results.Ok(requests);
})
    .WithSummary("Danh sách yêu cầu hủy/hoàn trả")
    .WithDescription("Chỉ Manager/Admin xem và xử lý các yêu cầu của khách hàng.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapPost("/api/admin/order-requests/{requestId:int}/review", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    IHubContext<OrderNotificationsHub> orderNotifications,
    OrderStatusEmailQueue statusEmailQueue,
    ILogger<Program> logger,
    int requestId,
    ReviewOrderActionRequest request,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if ((request.Note?.Trim().Length ?? 0) > 500)
        return Results.BadRequest(new { message = "Ghi chú phê duyệt không được vượt quá 500 ký tự." });

    await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    var actionRequest = await context.OrderActionRequests
        .FromSqlInterpolated($"SELECT * FROM \"OrderActionRequests\" WHERE \"Id\" = {requestId} FOR UPDATE")
        .FirstOrDefaultAsync(cancellationToken);
    if (actionRequest is null) return Results.NotFound(new { message = "Không tìm thấy yêu cầu xử lý đơn hàng." });
    if (!string.Equals(actionRequest.Status, "Pending", StringComparison.OrdinalIgnoreCase))
        return Results.Conflict(new { message = "Yêu cầu này đã được xử lý." });

    var order = await context.Orders
        .FromSqlInterpolated($"SELECT * FROM \"Orders\" WHERE \"Id\" = {actionRequest.OrderId} FOR UPDATE")
        .FirstOrDefaultAsync(cancellationToken);
    if (order is null) return Results.NotFound(new { message = "Không tìm thấy đơn hàng." });
    await context.Entry(order).Collection(current => current.OrderDetails).LoadAsync(cancellationToken);
    await context.Entry(order).Collection(current => current.StatusHistory).LoadAsync(cancellationToken);
    await context.Entry(order).Reference(current => current.User).LoadAsync(cancellationToken);

    var newStatus = actionRequest.RequestType switch
    {
        "Cancel" when order.Status is OrderStatus.Pending or OrderStatus.Confirmed => OrderStatus.Cancelled,
        "Refund" when order.Status == OrderStatus.Completed => OrderStatus.Refunded,
        _ => (OrderStatus?)null
    };
    if (newStatus is null)
        return Results.Conflict(new { message = "Trạng thái đơn không còn phù hợp để duyệt yêu cầu. Hãy tải lại dữ liệu." });

    var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
    var now = DateTimeOffset.UtcNow;
    if (request.Approve)
    {
        if ((actionRequest.RequestType == "Cancel" && order.Status == OrderStatus.Confirmed && order.InventoryReserved)
            || actionRequest.RequestType == "Refund")
        {
            foreach (var item in order.OrderDetails)
            {
                var restored = await context.Products
                    .Where(product => product.Id == item.ProductId && product.StockQuantity <= int.MaxValue - item.Quantity)
                    .ExecuteUpdateAsync(updates => updates
                        .SetProperty(product => product.StockQuantity, product => product.StockQuantity + item.Quantity),
                        cancellationToken);
                if (restored == 0)
                    return Results.Conflict(new { message = $"Không thể hoàn lại tồn kho linh kiện ID {item.ProductId}; yêu cầu chưa được xử lý." });
            }
            order.InventoryReserved = false;
        }

        var claimed = await context.Orders
            .Where(current => current.Id == order.Id && current.Status == order.Status)
            .ExecuteUpdateAsync(updates => updates.SetProperty(current => current.Status, newStatus.Value), cancellationToken);
        if (claimed == 0)
            return Results.Conflict(new { message = "Đơn hàng vừa được cập nhật. Yêu cầu chưa được xử lý." });
        order.Status = newStatus.Value;
        if (newStatus == OrderStatus.Refunded)
            order.IsPaid = false;
        order.StatusHistory.Add(new OrderStatusHistory
        {
            Status = newStatus.Value,
            ChangedAt = now.UtcDateTime,
            ChangedByName = manager.FullName,
            Note = note ?? (newStatus == OrderStatus.Cancelled
                ? $"Quản lý đã duyệt yêu cầu hủy đơn: {actionRequest.Reason}"
                : $"Quản lý đã duyệt hoàn trả linh kiện: {actionRequest.Reason}")
        });
    }

    actionRequest.Status = request.Approve ? "Approved" : "Rejected";
    actionRequest.ReviewedByUserId = manager.Id;
    actionRequest.ReviewedAt = now;
    actionRequest.ReviewNote = note;
    context.AuditLogs.Add(new AuditLog
    {
        Action = request.Approve ? "APPROVE_ORDER_ACTION_REQUEST" : "REJECT_ORDER_ACTION_REQUEST",
        Module = "ORDERS",
        PerformedBy = manager.Email,
        IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
        Details = System.Text.Json.JsonSerializer.Serialize(new
        {
            requestId = actionRequest.Id,
            orderId = order.Id,
            order.OrderCode,
            actionRequest.RequestType,
            actionRequest.Reason,
            decision = actionRequest.Status,
            resultingStatus = request.Approve ? newStatus!.Value.ToString() : order.Status.ToString(),
            note
        }),
        Timestamp = now.UtcDateTime
    });
    await context.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    if (request.Approve)
    {
        var recipient = order.CustomerEmail ?? order.User?.Email;
        var emailQueued = !string.IsNullOrWhiteSpace(recipient)
            && statusEmailQueue.TryQueue(recipient, order, GetOrderStatusLabel(newStatus!.Value));
        try
        {
            await orderNotifications.Clients.All.SendAsync("OrderStatusUpdated", new { receivedAt = now }, cancellationToken);
            await orderNotifications.Clients.All.SendAsync("InventoryUpdated", new
            {
                productIds = order.OrderDetails.Select(item => item.ProductId).Distinct().ToArray(),
                receivedAt = now
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Order action request {RequestId} was reviewed but notifications could not be delivered.", actionRequest.Id);
        }
        return Results.Ok(new
        {
            message = actionRequest.RequestType == "Cancel"
                ? "Đã duyệt hủy đơn và hoàn kho khả dụng."
                : "Đã duyệt hoàn trả linh kiện, hoàn kho và cập nhật đơn hàng.",
            status = order.Status.ToString(),
            emailQueued
        });
    }
    var customerRecipient = order.CustomerEmail ?? order.User?.Email;
    var rejectionEmailQueued = !string.IsNullOrWhiteSpace(customerRecipient)
        && statusEmailQueue.TryQueue(customerRecipient, order, "Yêu cầu xử lý đơn hàng bị từ chối");
    try
    {
        await orderNotifications.Clients.All.SendAsync("OrderStatusUpdated", new { receivedAt = now }, cancellationToken);
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "Order action request {RequestId} was rejected but notifications could not be delivered.", actionRequest.Id);
    }
    return Results.Ok(new
    {
        message = "Đã từ chối yêu cầu. Đơn hàng và tồn kho được giữ nguyên.",
        status = order.Status.ToString(),
        emailQueued = rejectionEmailQueued
    });
})
    .WithSummary("Phê duyệt hoặc từ chối yêu cầu xử lý đơn")
    .WithDescription("Chỉ Manager/Admin có quyền quyết định; phê duyệt hủy/hoàn trả cập nhật trạng thái, hoàn tồn kho phù hợp và gửi thông báo khách hàng.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status409Conflict);

app.MapGet("/api/orders", async (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    string? status,
    CancellationToken cancellationToken) =>
{
    var staffUser = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (staffUser is null) return Results.Unauthorized();
    if (staffUser.Role is not (UserRole.Staff or UserRole.Manager or UserRole.Admin))
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    var query = context.Orders.Include(o => o.User).Include(o => o.OrderDetails).Include(o => o.StatusHistory).AsNoTracking();
    if (!string.IsNullOrWhiteSpace(status))
    {
        if (!Enum.TryParse<OrderStatus>(status, true, out var requestedStatus) || !Enum.IsDefined(requestedStatus))
            return Results.BadRequest(new { message = "Trạng thái đơn hàng không hợp lệ." });
        query = query.Where(o => o.Status == requestedStatus);
    }

    var orders = await query.OrderByDescending(o => o.CreatedAt).Select(o => new
    {
        id = o.Id,
        orderCode = o.OrderCode,
        totalAmount = o.TotalAmount,
        voucherCode = o.VoucherCode,
        discountAmount = o.DiscountAmount,
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
            warehouseLocation = detail.Product.WarehouseLocation,
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
        }).ToList(),
        actionRequests = o.ActionRequests.OrderBy(request => request.RequestedAt).Select(request => new
        {
            requestType = request.RequestType,
            reason = request.Reason,
            status = request.Status,
            requestedAt = request.RequestedAt,
            reviewNote = request.ReviewNote
        }).ToList()
    }).ToListAsync(cancellationToken);
    return Results.Ok(orders);
});

app.MapPost("/api/orders/{id:int}/confirm", async (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<Program> logger,
    IHubContext<OrderNotificationsHub> orderNotifications,
    OrderStatusEmailQueue statusEmailQueue,
    int id,
    ConfirmOrderDto dto,
    CancellationToken cancellationToken) =>
{
    var staffUser = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (staffUser is null) return Results.Unauthorized();
    if (staffUser.Role is not (UserRole.Staff or UserRole.Manager or UserRole.Admin))
        return Results.StatusCode(StatusCodes.Status403Forbidden);
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
    context.AuditLogs.Add(new AuditLog
    {
    Action = "CONFIRM_ORDER",
    Module = "ORDERS",
    PerformedBy = staffUser.Email,
    IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
    Details = $"Xác nhận đơn {order.OrderCode}; đã đặt giữ tồn kho.",
    Timestamp = DateTime.UtcNow
    });
    await context.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    var recipient = order.CustomerEmail ?? order.User?.Email;
    var emailQueued = !string.IsNullOrWhiteSpace(recipient)
        && statusEmailQueue.TryQueue(recipient, order, "Đã xác nhận / Đang đóng gói");

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
        await orderNotifications.Clients.All.SendAsync("InventoryUpdated", new
        {
            productIds = order.OrderDetails.Select(item => item.ProductId).Distinct().ToArray(),
            receivedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "Order {OrderCode} was confirmed but warehouse notifications could not be delivered.", order.OrderCode);
    }

    return Results.Ok(new
    {
    message = "Đã xác nhận đơn hàng; tồn kho đã được kiểm tra và đặt trước.",
    status = order.Status.ToString(),
    emailQueued,
    emailWarning = string.IsNullOrWhiteSpace(recipient)
        ? "Đơn hàng đã xác nhận nhưng chưa có email khách hàng."
        : emailQueued ? null : "Đơn hàng đã xác nhận nhưng chưa xếp hàng gửi được email thông báo."
    });
})
    .WithSummary("Xác nhận đơn hàng chờ xử lý")
    .WithDescription("Kiểm tra và trừ tồn kho nguyên tử trước khi chuyển đơn sang Đã xác nhận/Đang đóng gói.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status409Conflict);

app.MapPut("/api/orders/{id:int}/status", async (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<Program> logger,
    IHubContext<OrderNotificationsHub> orderNotifications,
    OrderStatusEmailQueue statusEmailQueue,
    int id,
    UpdateOrderStatusDto dto,
    CancellationToken cancellationToken) =>
{
    var staffUser = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (staffUser is null) return Results.Unauthorized();
    if (staffUser.Role is not (UserRole.Staff or UserRole.Manager or UserRole.Admin))
        return Results.StatusCode(StatusCodes.Status403Forbidden);
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
    if (newStatus == OrderStatus.Cancelled)
        return Results.Conflict(new { message = "Hủy đơn phải được gửi thành yêu cầu và phê duyệt riêng bởi Manager/Admin." });
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
    context.AuditLogs.Add(new AuditLog
    {
        Action = "UPDATE_ORDER_STATUS",
        Module = "ORDERS",
        PerformedBy = staffUser.Email,
        IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
        Details = System.Text.Json.JsonSerializer.Serialize(new
        {
            orderId = order.Id,
            order.OrderCode,
            from = originalStatus.ToString(),
            to = newStatus.ToString(),
            carrier,
            trackingNumber,
            note,
            order.InventoryReserved
        }),
        Timestamp = DateTime.UtcNow
    });
    await context.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    var recipient = order.CustomerEmail ?? order.User?.Email;
    var statusLabel = GetOrderStatusLabel(newStatus);
    var emailQueued = !string.IsNullOrWhiteSpace(recipient)
        && statusEmailQueue.TryQueue(recipient, order, statusLabel);

    try
    {
        await orderNotifications.Clients.All.SendAsync("OrderStatusUpdated", new
        {
            receivedAt = DateTime.UtcNow
        }, cancellationToken);
        await orderNotifications.Clients.All.SendAsync("InventoryUpdated", new
        {
            productIds = order.OrderDetails.Select(item => item.ProductId).Distinct().ToArray(),
            receivedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "Order {OrderCode} was updated but realtime status notifications could not be delivered.", order.OrderCode);
    }

    return Results.Ok(new
    {
        message = "Cập nhật trạng thái thành công!",
        status = order.Status.ToString(),
        carrier,
        trackingNumber,
        emailQueued,
        emailWarning = string.IsNullOrWhiteSpace(recipient)
            ? "Đơn hàng đã cập nhật nhưng chưa có email khách hàng."
            : emailQueued ? null : "Đơn hàng đã cập nhật nhưng chưa xếp hàng gửi được email thông báo."
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
app.MapPost("/api/ai/build-recommendation", async (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    BuildPcAiRequest request,
    CancellationToken cancellationToken) =>
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
            && product.Category != null
            && product.Category.IsActive
            && selectedTypes.Contains(product.Category.ComponentType))
        .ToListAsync(cancellationToken);
    var listedPrices = catalog.ToDictionary(product => product.Id, product => product.Price);
    var pricingTime = DateTimeOffset.UtcNow;
    foreach (var product in catalog)
        product.Price = GetEffectiveProductPrice(product, pricingTime);
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
            listPrice = listedPrices[product.Id],
            promotionalPrice = product.PromotionalPrice,
            promotionStartsAt = product.PromotionStartsAt,
            promotionEndsAt = product.PromotionEndsAt,
            stockQuantity = product.StockQuantity,
            categoryType = type.ToString(),
            categoryName = product.Category!.Name,
            socket = product.TechnicalSpec?.Socket,
            ramType = product.TechnicalSpec?.RamType,
            tdpWattage = product.TechnicalSpec?.TdpWattage ?? 0,
            recommendedPsu = product.TechnicalSpec?.RecommendedPsu ?? 0
        };
    }).ToList();

    var actor = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    context.AuditLogs.Add(new AuditLog
    {
        Action = "AI_BUILD_RECOMMENDATION",
        Module = "AI",
        PerformedBy = actor?.Email ?? "Anonymous",
        PerformedByRole = actor?.Role.ToString(),
        IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
        UserAgent = GetRequestUserAgent(httpContext),
        Status = "Success",
        Details = System.Text.Json.JsonSerializer.Serialize(new
        {
            budget = request.Budget,
            purpose = request.Purpose,
            requestedComponents = selectedTypes.Select(type => type.ToString()),
            notes = request.Notes,
            selectedItems = items.Select(item => new { item.id, item.name, item.price }),
            totalPrice = bestBuild.TotalPrice,
        }),
        Timestamp = DateTime.UtcNow,
    });
    await context.SaveChangesAsync(cancellationToken);

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

app.MapPost("/api/ai/consult", async (
    IAiOrchestrator orchestrator,
    AiConsultDto dto,
    CancellationToken cancellationToken) =>
{
    var msg = dto.Message?.Trim() ?? "";
    if (msg.Length is 0 or > 4000)
        return Results.BadRequest(new { message = "Nội dung tư vấn phải có từ 1 đến 4000 ký tự." });

    try
    {
        var response = await orchestrator.GenerateCustomerReplyAsync(msg, cancellationToken);
        return Results.Ok(new { response, suggestedProducts = Array.Empty<int>() });
    }
    catch (AllProvidersFailedException exception)
    {
        return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (AiStreamInterruptedException exception)
    {
        return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/api/ai/upgrade-suggestions", async (AppDbContext context, string productIds) =>
{
    if (string.IsNullOrWhiteSpace(productIds)) return Results.BadRequest(new { message = "Cần productIds." });
    var ids = productIds.Split(',').Select(s => int.TryParse(s.Trim(), out var v) ? v : 0).Where(v => v > 0).ToList();
    var current = await context.Products.Include(p => p.Category).Include(p => p.TechnicalSpec).Where(p => ids.Contains(p.Id)).ToListAsync();
    var pricingTime = DateTimeOffset.UtcNow;
    foreach (var product in current)
        product.Price = GetEffectiveProductPrice(product, pricingTime);
    var suggestions = new List<object>();

    foreach (var p in current)
    {
        var catType = p.Category!.ComponentType;
        var candidates = await context.Products
            .Include(pr => pr.Category).Include(pr => pr.TechnicalSpec)
            .Where(pr => pr.Category != null && pr.Category.ComponentType == catType && pr.IsActive && pr.StockQuantity > 0)
            .OrderBy(pr => pr.Price).Take(100).ToListAsync();
        var upgrades = candidates
            .Select(product =>
            {
                product.Price = GetEffectiveProductPrice(product, pricingTime);
                return product;
            })
            .Where(product => product.Price > p.Price)
            .OrderBy(product => product.Price)
            .Take(2)
            .ToList();

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
app.MapGet("/api/staff/customers", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    string? search,
    CancellationToken cancellationToken) =>
{
    var staffUser = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (staffUser is null) return Results.Unauthorized();
    if (staffUser.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var query = context.Users.AsNoTracking().Where(u => u.Role == UserRole.Customer);
    var term = search?.Trim();
    if (string.IsNullOrWhiteSpace(term) || term.Length < 2)
        return Results.Ok(Array.Empty<object>());

    var normalizedEmail = term.ToLowerInvariant();
    var digits = new string(term.Where(char.IsDigit).ToArray());
    var customerCode = term.StartsWith("KH-", StringComparison.OrdinalIgnoreCase) ? term[3..] : term;
    var isCustomerId = int.TryParse(customerCode, out var customerId);
    query = query.Where(user =>
        (isCustomerId && user.Id == customerId)
        || user.Email == normalizedEmail
        || (digits.Length >= 4
            && user.PhoneNumber != null
            && user.PhoneNumber.EndsWith(digits)));

    var customerRecords = await query
        .OrderBy(user => user.FullName)
        .Take(20)
        .Select(user => new
        {
            id = user.Id,
            fullName = user.FullName,
            email = user.Email,
            phoneNumber = user.PhoneNumber,
            address = user.Address,
            createdAt = user.CreatedAt,
            orderCount = user.Orders.Count,
            savedBuildCount = user.PcBuilds.Count
        })
        .ToListAsync(cancellationToken);

    var customers = customerRecords.Select(user => new
    {
        user.id,
        customerCode = $"KH-{user.id:D6}",
        user.fullName,
        user.email,
        user.phoneNumber,
        user.address,
        user.createdAt,
        user.orderCount,
        user.savedBuildCount
    }).ToList();
    return Results.Ok(customers);
})
    .WithSummary("Tra cứu khách hàng")
    .WithDescription("Tìm theo email chính xác, mã khách KH-xxxxxx, ID hoặc số điện thoại đầy đủ/bốn số cuối. Trả tối đa 20 kết quả.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapPost("/api/staff/compatibility-check", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    CheckPcCompatibilityRequest request,
    CancellationToken cancellationToken) =>
{
    var staffUser = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (staffUser is null) return Results.Unauthorized();
    if (staffUser.Role is not (UserRole.Staff or UserRole.Manager or UserRole.Admin))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    if (request.ProductIds is null || request.ProductIds.Count is < 1 or > 20
        || request.ProductIds.Any(id => id <= 0)
        || request.ProductIds.Distinct().Count() != request.ProductIds.Count)
        return Results.BadRequest(new { message = "Chọn từ 1 đến 20 linh kiện khác nhau để kiểm tra." });

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var selectedProducts = await context.Products
        .AsNoTracking()
        .Include(product => product.Category)
        .Include(product => product.TechnicalSpec)
        .Where(product => request.ProductIds.Contains(product.Id) && product.IsActive)
        .ToListAsync(cancellationToken);

    if (selectedProducts.Count != request.ProductIds.Count)
        return Results.BadRequest(new { message = "Một hoặc nhiều linh kiện không tồn tại hoặc đã ngừng kinh doanh." });

    if (selectedProducts.GroupBy(product => product.Category!.ComponentType).Any(group => group.Count() > 1))
        return Results.BadRequest(new { message = "Chỉ chọn một linh kiện cho mỗi danh mục." });

    var pricingTime = DateTimeOffset.UtcNow;
    foreach (var product in selectedProducts)
        product.Price = GetEffectiveProductPrice(product, pricingTime);
    var selectedParts = selectedProducts.ToDictionary(product => product.Category!.ComponentType);
    var issues = CheckPcBuildCompatibility(selectedParts);
    var affectedTypes = issues.SelectMany(issue => issue.CategoryTypes)
        .Distinct()
        .ToHashSet();
    var suggestions = new List<(int ResolvedIssueCount, object Value)>();

    if (issues.Count > 0)
    {
        var candidateProducts = await context.Products
            .AsNoTracking()
            .Include(product => product.Category)
            .Include(product => product.TechnicalSpec)
            .Where(product => product.IsActive
                && product.StockQuantity > 0
                && affectedTypes.Contains(product.Category!.ComponentType)
                && !request.ProductIds.Contains(product.Id))
            .OrderBy(product => product.Category!.ComponentType)
            .Take(500)
            .ToListAsync(cancellationToken);
        foreach (var product in candidateProducts)
            product.Price = GetEffectiveProductPrice(product, pricingTime);

        foreach (var type in affectedTypes)
        {
            if (!selectedParts.TryGetValue(type, out var currentPart))
                continue;

            var compatibleAlternatives = candidateProducts
                .Where(product => product.Category!.ComponentType == type)
                .Take(60)
                .Select(product =>
                {
                    var candidateParts = new Dictionary<ComponentType, Product>(selectedParts)
                    {
                        [type] = product
                    };
                    var remainingIssues = CheckPcBuildCompatibility(candidateParts);
                    return new
                    {
                        Product = product,
                        RemainingIssues = remainingIssues,
                        ResolvedIssues = issues.Count - remainingIssues.Count
                    };
                })
                .Where(candidate => candidate.ResolvedIssues > 0)
                .OrderBy(candidate => candidate.RemainingIssues.Count)
                .ThenBy(candidate => Math.Abs(candidate.Product.Price - currentPart.Price))
                .Take(5);

            foreach (var candidate in compatibleAlternatives)
            {
                var suggestion = new
                {
                    replacesProductId = currentPart.Id,
                    replacesCategoryType = type.ToString(),
                    product = new
                    {
                        id = candidate.Product.Id,
                        name = candidate.Product.Name,
                        sku = candidate.Product.Sku,
                        brand = candidate.Product.Brand,
                        price = candidate.Product.Price,
                        stockQuantity = candidate.Product.StockQuantity,
                        categoryType = type.ToString(),
                        socket = candidate.Product.TechnicalSpec?.Socket,
                        ramType = candidate.Product.TechnicalSpec?.RamType,
                        tdpWattage = candidate.Product.TechnicalSpec?.TdpWattage,
                        formFactor = candidate.Product.TechnicalSpec?.FormFactor,
                        lengthMm = candidate.Product.TechnicalSpec?.LengthMm
                    },
                    priceDifference = candidate.Product.Price - currentPart.Price,
                    resolvedIssueCount = candidate.ResolvedIssues,
                    remainingIssues = candidate.RemainingIssues.Select(issue => issue.Message).ToList()
                };
                suggestions.Add((candidate.ResolvedIssues, suggestion));
            }
        }
    }

    stopwatch.Stop();
    var checkedItems = selectedParts.Values.Select(product => new
    {
        id = product.Id,
        name = product.Name,
        categoryType = product.Category!.ComponentType.ToString(),
        price = product.Price,
        stockQuantity = product.StockQuantity,
        socket = product.TechnicalSpec?.Socket,
        ramType = product.TechnicalSpec?.RamType,
        tdpWattage = product.TechnicalSpec?.TdpWattage,
        formFactor = product.TechnicalSpec?.FormFactor,
        lengthMm = product.TechnicalSpec?.LengthMm
    }).ToList();

    return Results.Ok(new
    {
        isCompatible = issues.Count == 0,
        checkedItems,
        issues,
        suggestedReplacements = suggestions
            .OrderByDescending(suggestion => suggestion.ResolvedIssueCount)
            .Select(suggestion => suggestion.Value)
            .Take(10),
        checkedAt = DateTimeOffset.UtcNow,
        elapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds
    });
})
    .WithSummary("Kiểm tra tương thích cấu hình PC")
    .WithDescription("Quét socket, chuẩn RAM, công suất nguồn, kích thước case/card/tản nhiệt; gợi ý tối đa 10 linh kiện thay thế còn hàng.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapGet("/api/staff/customers/{customerId:int}", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    int customerId,
    CancellationToken cancellationToken) =>
{
    var staffUser = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (staffUser is null) return Results.Unauthorized();
    if (staffUser.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var customerRecord = await context.Users
        .AsNoTracking()
        .Where(user => user.Id == customerId && user.Role == UserRole.Customer)
        .Select(user => new
        {
            id = user.Id,
            fullName = user.FullName,
            email = user.Email,
            phoneNumber = user.PhoneNumber,
            address = user.Address,
            createdAt = user.CreatedAt
        })
        .FirstOrDefaultAsync(cancellationToken);

    if (customerRecord is null)
        return Results.NotFound(new { message = "Không tìm thấy hồ sơ khách hàng." });

    var customerPhoneDigits = new string((customerRecord.phoneNumber ?? string.Empty)
        .Where(char.IsDigit)
        .ToArray());
    var phoneSuffix = customerPhoneDigits.Length >= 8
        ? customerPhoneDigits[^8..]
        : customerPhoneDigits;
    var orders = await context.Orders
        .AsNoTracking()
        .Where(order =>
            order.UserId == customerId
            || (customerRecord.email != null
                && order.CustomerEmail != null
                && order.CustomerEmail.ToLower() == customerRecord.email.ToLower())
            || (phoneSuffix.Length >= 4
                && order.ReceiverPhone
                    .Replace(" ", "")
                    .Replace("-", "")
                    .Replace("(", "")
                    .Replace(")", "")
                    .EndsWith(phoneSuffix)))
        .OrderByDescending(order => order.CreatedAt)
        .Select(order => new
        {
            id = order.Id,
            orderCode = order.OrderCode,
            totalAmount = order.TotalAmount,
            voucherCode = order.VoucherCode,
            discountAmount = order.DiscountAmount,
            status = order.Status.ToString(),
            createdAt = order.CreatedAt,
            receiverName = order.ReceiverName,
            receiverPhone = order.ReceiverPhone,
            shippingAddress = order.ShippingAddress,
            trackingNumber = order.StatusHistory
                .Where(history => history.TrackingNumber != null)
                .OrderByDescending(history => history.ChangedAt)
                .Select(history => history.TrackingNumber)
                .FirstOrDefault(),
            carrier = order.StatusHistory
                .Where(history => history.Carrier != null)
                .OrderByDescending(history => history.ChangedAt)
                .Select(history => history.Carrier)
                .FirstOrDefault(),
            items = order.OrderDetails.Select(detail => new
            {
                productName = detail.Product!.Name,
                detail.Quantity,
                detail.UnitPrice
            }).ToList(),
            statusHistory = order.StatusHistory
                .OrderBy(history => history.ChangedAt)
                .Select(history => new
                {
                    status = history.Status.ToString(),
                    changedAt = history.ChangedAt,
                    history.ChangedByName,
                    history.Note
                }).ToList()
        })
        .ToListAsync(cancellationToken);

    var customer = new
    {
        customerRecord.id,
        customerCode = $"KH-{customerRecord.id:D6}",
        customerRecord.fullName,
        customerRecord.email,
        customerRecord.phoneNumber,
        customerRecord.address,
        customerRecord.createdAt,
        totalSpent = orders
            .Where(order => order.status == OrderStatus.Completed.ToString())
            .Sum(order => order.totalAmount)
    };

    var builds = await context.PcBuilds
        .AsNoTracking()
        .Where(build => build.UserId == customerId)
        .OrderByDescending(build => build.CreatedAt)
        .Select(build => new
        {
            id = build.Id,
            buildName = build.BuildName,
            purpose = build.Purpose,
            totalPrice = build.TotalPrice,
            estimatedWattage = build.EstimatedWattage,
            isCompatible = build.IsCompatible,
            createdAt = build.CreatedAt,
            items = build.Items.Select(item => new
            {
                productName = item.Product!.Name,
                categoryName = item.Product.Category!.Name,
                categoryType = item.Product.Category.ComponentType.ToString(),
                item.Quantity,
                item.UnitPrice
            }).ToList()
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(new { customer, orders, builds });
})
    .WithSummary("Xem hồ sơ và lịch sử khách hàng")
    .WithDescription("Trả thông tin hồ sơ cơ bản, đơn hàng đã mua và các cấu hình PC đã lưu; không trả mật khẩu hoặc dữ liệu thanh toán.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound);

app.MapGet("/api/builds/user/{userId:int}/{buildId:int}", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    int userId,
    int buildId,
    CancellationToken cancellationToken) =>
{
    var authenticatedUser = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (authenticatedUser is null) return Results.Unauthorized();
    if (authenticatedUser.Id != userId || authenticatedUser.Role != UserRole.Customer)
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var build = await context.PcBuilds
        .AsNoTracking()
        .Where(item => item.Id == buildId && item.UserId == userId)
        .Select(item => new
        {
            id = item.Id,
            buildName = item.BuildName,
            purpose = item.Purpose,
            totalPrice = item.TotalPrice,
            estimatedWattage = item.EstimatedWattage,
            isCompatible = item.IsCompatible,
            createdAt = item.CreatedAt,
            items = item.Items.Select(buildItem => new
            {
                productId = buildItem.ProductId,
                productName = buildItem.Product!.Name,
                categoryName = buildItem.Product.Category!.Name,
                categoryType = buildItem.Product.Category.ComponentType.ToString(),
                buildItem.Quantity,
                buildItem.UnitPrice
            }).ToList()
        })
        .FirstOrDefaultAsync(cancellationToken);

    if (build is null)
        return Results.NotFound(new { message = "Không tìm thấy cấu hình PC." });
    return Results.Ok(build);
})
    .WithSummary("Xem cấu hình PC đã lưu")
    .WithDescription("Chỉ chủ sở hữu cấu hình đã đăng nhập mới có thể xem chi tiết.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound);

app.MapPost("/api/builds/user/{userId:int}", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    int userId,
    SavePcBuildRequest request,
    CancellationToken cancellationToken) =>
{
    var authenticatedUser = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (authenticatedUser is null) return Results.Unauthorized();
    if (authenticatedUser.Id != userId || authenticatedUser.Role != UserRole.Customer)
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var buildName = request.BuildName?.Trim();
    var purpose = string.IsNullOrWhiteSpace(request.Purpose) ? "general" : request.Purpose.Trim().ToLowerInvariant();
    var requestedItems = request.Items ?? [];
    if (string.IsNullOrWhiteSpace(buildName) || buildName.Length > 150)
        return Results.BadRequest(new { message = "Tên cấu hình không được để trống và tối đa 150 ký tự." });
    if (purpose is not ("gaming" or "graphics" or "office" or "streaming" or "general"))
        return Results.BadRequest(new { message = "Mục đích cấu hình PC không hợp lệ." });
    if (requestedItems.Count is < 1 or > 20
        || requestedItems.Any(item => item.ProductId <= 0 || item.Quantity is < 1 or > 10)
        || requestedItems.Select(item => item.ProductId).Distinct().Count() != requestedItems.Count)
        return Results.BadRequest(new { message = "Danh sách linh kiện không hợp lệ." });

    var userExists = await context.Users.AnyAsync(user =>
        user.Id == userId && user.Role == UserRole.Customer && user.IsActive, cancellationToken);
    if (!userExists)
        return Results.NotFound(new { message = "Không tìm thấy tài khoản khách hàng đang hoạt động." });

    var productIds = requestedItems.Select(item => item.ProductId).ToList();
    var products = await context.Products
        .AsNoTracking()
        .Include(product => product.TechnicalSpec)
        .Where(product => productIds.Contains(product.Id) && product.IsActive)
        .ToListAsync(cancellationToken);
    if (products.Count != productIds.Count)
        return Results.BadRequest(new { message = "Một hoặc nhiều linh kiện không còn khả dụng." });

    var quantityByProduct = requestedItems.ToDictionary(item => item.ProductId, item => item.Quantity);
    var build = new PcBuild
    {
        UserId = userId,
        BuildName = buildName,
        Purpose = purpose,
        CreatedAt = DateTime.UtcNow,
        EstimatedWattage = products.Sum(product =>
            (product.TechnicalSpec?.TdpWattage ?? 0) * quantityByProduct[product.Id]),
        IsCompatible = request.IsCompatible,
        Items = products.Select(product => new PcBuildItem
        {
            ProductId = product.Id,
            Quantity = quantityByProduct[product.Id],
            UnitPrice = product.Price
        }).ToList()
    };
    build.TotalPrice = products.Sum(product => product.Price * quantityByProduct[product.Id]);

    context.PcBuilds.Add(build);
    await context.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/builds/user/{userId}/{build.Id}", new
    {
        id = build.Id,
        buildName = build.BuildName,
        totalPrice = build.TotalPrice,
        createdAt = build.CreatedAt
    });
})
    .WithSummary("Lưu cấu hình PC của khách hàng")
    .WithDescription("Lưu cấu hình tự ráp hoặc cấu hình được AI tư vấn để hỗ trợ tra cứu lịch sử tư vấn.")
    .Produces(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound);

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
app.MapGet("/api/admin/audit-logs", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    DateTimeOffset? fromDate,
    DateTimeOffset? toDate,
    string? user,
    string? action,
    string? status,
    CancellationToken cancellationToken,
    int page = 1,
    int pageSize = 50) =>
{
    var administrator = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (administrator is null) return Results.Unauthorized();
    if (administrator.Role != UserRole.Admin) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
        return Results.BadRequest(new { message = "Thời gian bắt đầu không được lớn hơn thời gian kết thúc." });
    if (page < 1 || pageSize is < 1 or > 200 || page > int.MaxValue / pageSize)
        return Results.BadRequest(new { message = "Số trang phải lớn hơn 0 và kích thước trang từ 1 đến 200." });

    var query = FilterAuditLogs(context, fromDate, toDate, user, action, status);
    var totalCount = await query.CountAsync(cancellationToken);
    var rows = await query
        .OrderByDescending(log => log.Timestamp)
        .ThenByDescending(log => log.Id)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(log => new
        {
            log.Id,
            log.Timestamp,
            log.PerformedBy,
            log.PerformedByRole,
            log.Action,
            log.Module,
            log.IpAddress,
            log.UserAgent,
            log.Status,
            log.Details,
        })
        .ToListAsync(cancellationToken);
    var items = rows.Select(log => new AuditLogItemResponse(
        log.Id,
        new DateTimeOffset(DateTime.SpecifyKind(log.Timestamp, DateTimeKind.Utc)),
        log.PerformedBy,
        log.PerformedByRole,
        log.Action,
        log.Module,
        log.IpAddress,
        log.UserAgent,
        log.Status,
        log.Details)).ToList();

    return Results.Ok(new AuditLogPageResponse(items, page, pageSize, totalCount));
})
    .WithSummary("Tra cứu nhật ký hoạt động")
    .WithDescription("Chỉ Admin được xem. Lọc theo khoảng thời gian, tài khoản, loại hành động và trạng thái; kết quả mới nhất được sắp lên đầu.")
    .Produces<AuditLogPageResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapGet("/api/admin/audit-logs/export", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    DateTimeOffset? fromDate,
    DateTimeOffset? toDate,
    string? user,
    string? action,
    string? status,
    string format,
    CancellationToken cancellationToken) =>
{
    var administrator = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (administrator is null) return Results.Unauthorized();
    if (administrator.Role != UserRole.Admin) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
        return Results.BadRequest(new { message = "Thời gian bắt đầu không được lớn hơn thời gian kết thúc." });
    if (format is not ("csv" or "excel"))
        return Results.BadRequest(new { message = "Định dạng xuất chỉ được là csv hoặc excel." });

    var logs = await FilterAuditLogs(context, fromDate, toDate, user, action, status)
        .OrderByDescending(log => log.Timestamp)
        .ThenByDescending(log => log.Id)
        .Take(10_000)
        .Select(log => new
        {
            log.Timestamp,
            log.PerformedBy,
            log.PerformedByRole,
            log.Action,
            log.Module,
            log.IpAddress,
            log.Status,
            log.Details,
            log.UserAgent,
        })
        .ToListAsync(cancellationToken);

    var headers = new[] { "Thời gian", "Người thực hiện", "Vai trò", "Hành động", "Module", "Địa chỉ IP", "Trạng thái", "Chi tiết", "User-Agent" };
    var rows = logs.Select(log => new[]
    {
        new DateTimeOffset(DateTime.SpecifyKind(log.Timestamp, DateTimeKind.Utc)).ToString("O"),
        log.PerformedBy ?? string.Empty,
        log.PerformedByRole ?? string.Empty,
        log.Action ?? string.Empty,
        log.Module ?? string.Empty,
        log.IpAddress ?? string.Empty,
        log.Status,
        log.Details ?? string.Empty,
        log.UserAgent ?? string.Empty,
    }).ToList();

    if (format == "csv")
    {
        static string CsvCell(string value)
        {
            var firstCharacter = value.TrimStart().FirstOrDefault();
            var safeValue = firstCharacter is '=' or '+' or '-' or '@' ? $"'{value}" : value;
            return $"\"{safeValue.Replace("\"", "\"\"")}\"";
        }

        var csv = string.Join("\r\n", new[] { headers }.Concat(rows).Select(row => string.Join(",", row.Select(CsvCell))));
        var csvBytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return Results.File(csvBytes, "text/csv; charset=utf-8", $"audit-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
    }

    var workbook = new StringBuilder();
    workbook.Append("""<?xml version="1.0" encoding="UTF-8"?><?mso-application progid="Excel.Sheet"?>""");
    workbook.Append("""<Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet" xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet"><Worksheet ss:Name="Audit Logs"><Table>""");
    foreach (var row in new[] { headers }.Concat(rows))
    {
        workbook.Append("<Row>");
        foreach (var cell in row)
            workbook.Append("<Cell><Data ss:Type=\"String\">")
                .Append(System.Security.SecurityElement.Escape(cell) ?? string.Empty)
                .Append("</Data></Cell>");
        workbook.Append("</Row>");
    }
    workbook.Append("</Table></Worksheet></Workbook>");
    return Results.File(
        Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(workbook.ToString())).ToArray(),
        "application/vnd.ms-excel",
        $"audit-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xls");
})
    .WithSummary("Xuất nhật ký hoạt động")
    .WithDescription("Xuất dữ liệu đã lọc ở định dạng CSV hoặc Excel; tối đa 10.000 bản ghi mới nhất.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapGet("/api/admin/stats", async (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    DateTime? fromDate,
    DateTime? toDate,
    int? categoryId,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var startDate = fromDate.HasValue
        ? DateTime.SpecifyKind(fromDate.Value.Date, DateTimeKind.Utc)
        : DateTime.UtcNow.Date.AddDays(-29);
    var endDate = toDate.HasValue
        ? DateTime.SpecifyKind(toDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
        : DateTime.UtcNow;
    if (endDate < startDate)
        return Results.BadRequest(new { message = "Khoảng thời gian bắt đầu không được lớn hơn ngày kết thúc." });
    if (categoryId is <= 0)
        return Results.BadRequest(new { message = "Danh mục linh kiện không hợp lệ." });
    if (categoryId.HasValue && !await context.Categories.AnyAsync(category => category.Id == categoryId.Value, cancellationToken))
        return Results.BadRequest(new { message = "Không tìm thấy danh mục linh kiện." });

    var filteredOrders = context.Orders
        .AsNoTracking()
        .Where(order => order.CreatedAt >= startDate && order.CreatedAt <= endDate);
    var reportOrders = categoryId.HasValue
        ? filteredOrders.Where(order => order.OrderDetails.Any(detail =>
            detail.Product != null && detail.Product.CategoryId == categoryId.Value))
        : filteredOrders;
    var completedOrders = reportOrders.Where(order => order.Status == OrderStatus.Completed);
    var reportDetails = context.OrderDetails
        .AsNoTracking()
        .Where(detail => detail.Order != null
            && detail.Order.Status == OrderStatus.Completed
            && detail.Order.CreatedAt >= startDate
            && detail.Order.CreatedAt <= endDate);
    if (categoryId.HasValue)
        reportDetails = reportDetails.Where(detail => detail.Product != null && detail.Product.CategoryId == categoryId.Value);

    var orderSummary = await reportOrders
        .GroupBy(_ => 1)
        .Select(group => new
        {
            totalOrders = group.Count(),
            completedOrders = group.Count(order => order.Status == OrderStatus.Completed),
            pendingOrders = group.Count(order => order.Status == OrderStatus.Pending),
            cancelledOrders = group.Count(order => order.Status == OrderStatus.Cancelled || order.Status == OrderStatus.DeliveryFailed),
            revenue = group.Sum(order => order.Status == OrderStatus.Completed ? order.TotalAmount : 0m)
        })
        .FirstOrDefaultAsync(cancellationToken);
    var totalOrderCount = orderSummary?.totalOrders ?? 0;
    var completedOrderCount = orderSummary?.completedOrders ?? 0;
    var pendingOrders = orderSummary?.pendingOrders ?? 0;
    var cancelledOrders = orderSummary?.cancelledOrders ?? 0;
    var totalRevenue = categoryId.HasValue
        ? await reportDetails.SumAsync(detail => detail.UnitPrice * detail.Quantity, cancellationToken)
        : orderSummary?.revenue ?? 0m;
    var averageOrderValue = completedOrderCount == 0 ? 0m : totalRevenue / completedOrderCount;
    var totalComponentsSold = await reportDetails.SumAsync(detail => detail.Quantity, cancellationToken);

    var revenueByDayData = categoryId.HasValue
        ? await reportDetails
            .GroupBy(detail => detail.Order!.CreatedAt.Date)
            .Select(group => new
            {
                date = group.Key,
                revenue = group.Sum(detail => detail.UnitPrice * detail.Quantity),
                orders = group.Select(detail => detail.OrderId).Distinct().Count()
            })
            .OrderBy(item => item.date)
            .ToListAsync(cancellationToken)
        : await completedOrders
            .GroupBy(order => order.CreatedAt.Date)
            .Select(group => new
            {
                date = group.Key,
                revenue = group.Sum(order => order.TotalAmount),
                orders = group.Count()
            })
            .OrderBy(item => item.date)
            .ToListAsync(cancellationToken);
    var revenueByDay = revenueByDayData
        .Select(item => new { item.date, label = item.date.ToString("dd/MM"), item.revenue, item.orders })
        .ToList();
    var monthlyRevenue = revenueByDay
        .Select(item => new { item.date, item.revenue, count = item.orders })
        .ToList();

    var revenueByCategory = await reportDetails
        .GroupBy(detail => detail.Product != null && detail.Product.Category != null ? detail.Product.Category.Name : "Khác")
        .Select(group => new
        {
            category = group.Key,
            revenue = group.Sum(detail => detail.UnitPrice * detail.Quantity),
            orders = group.Select(detail => detail.OrderId).Distinct().Count()
        })
        .OrderByDescending(item => item.revenue)
        .Take(6)
        .ToListAsync(cancellationToken);

    var topSellingComponents = await reportDetails
        .Where(detail => detail.Product != null)
        .GroupBy(detail => new
        {
            detail.ProductId,
            productName = detail.Product!.Name,
            categoryName = detail.Product.Category != null ? detail.Product.Category.Name : "Khác"
        })
        .Select(group => new
        {
            productId = group.Key.ProductId,
            productName = group.Key.productName,
            categoryName = group.Key.categoryName,
            unitsSold = group.Sum(detail => detail.Quantity),
            revenue = group.Sum(detail => detail.UnitPrice * detail.Quantity)
        })
        .OrderByDescending(item => item.unitsSold)
        .ThenByDescending(item => item.revenue)
        .Take(10)
        .ToListAsync(cancellationToken);

    var revenueByPaymentMethod = categoryId.HasValue
        ? await reportDetails
            .GroupBy(detail => detail.Order!.PaymentMethod)
            .Select(group => new
            {
                paymentMethod = group.Key,
                revenue = group.Sum(detail => detail.UnitPrice * detail.Quantity),
                orders = group.Select(detail => detail.OrderId).Distinct().Count()
            })
            .OrderByDescending(item => item.revenue)
            .ToListAsync(cancellationToken)
        : await completedOrders
            .GroupBy(order => order.PaymentMethod)
            .Select(group => new
            {
                paymentMethod = group.Key,
                revenue = group.Sum(order => order.TotalAmount),
                orders = group.Count()
            })
            .OrderByDescending(item => item.revenue)
            .ToListAsync(cancellationToken);

    var buildQuery = context.PcBuilds.AsNoTracking()
        .Where(build => build.CreatedAt >= startDate && build.CreatedAt <= endDate);
    if (categoryId.HasValue)
        buildQuery = buildQuery.Where(build => build.Items.Any(item => item.Product != null && item.Product.CategoryId == categoryId.Value));
    var pcBuildTrends = await buildQuery
        .GroupBy(build => build.Purpose)
        .Select(group => new
        {
            purpose = group.Key,
            configurations = group.Count(),
            compatibleConfigurations = group.Count(build => build.IsCompatible),
            averageConfigurationPrice = group.Average(build => build.TotalPrice)
        })
        .OrderByDescending(item => item.configurations)
        .ToListAsync(cancellationToken);

    var periodLengthDays = Math.Max(1, (endDate.Date - startDate.Date).Days + 1);
    var previousPeriodStart = startDate.AddDays(-periodLengthDays);
    var previousPeriodEnd = startDate.AddDays(-1);
    var previousRevenue = categoryId.HasValue
        ? await context.OrderDetails
            .AsNoTracking()
            .Where(detail => detail.Order != null
                && detail.Order.Status == OrderStatus.Completed
                && detail.Order.CreatedAt >= previousPeriodStart
                && detail.Order.CreatedAt <= previousPeriodEnd
                && detail.Product != null
                && detail.Product.CategoryId == categoryId.Value)
            .SumAsync(detail => detail.UnitPrice * detail.Quantity, cancellationToken)
        : await context.Orders
            .AsNoTracking()
            .Where(order => order.Status == OrderStatus.Completed
                && order.CreatedAt >= previousPeriodStart
                && order.CreatedAt <= previousPeriodEnd)
            .SumAsync(order => order.TotalAmount, cancellationToken);
    var growthRate = previousRevenue == 0
        ? (totalRevenue > 0 ? 100m : 0m)
        : ((totalRevenue - previousRevenue) / previousRevenue) * 100m;

    return Results.Ok(new
    {
        totalRevenue,
        totalOrders = totalOrderCount,
        completedOrders = completedOrderCount,
        totalComponentsSold,
        cancelledOrders,
        pendingOrders,
        averageOrderValue,
        periodStart = startDate,
        periodEnd = endDate,
        growthRate,
        monthlyRevenue,
        revenueByDay,
        revenueByCategory,
        revenueByPaymentMethod,
        topSellingComponents,
        pcBuildTrends,
        categoryId
    });
})
    .WithSummary("Báo cáo và thống kê doanh thu")
    .WithDescription("Tổng hợp doanh thu, đơn hàng, linh kiện bán chạy và mục đích cấu hình PC theo thời gian, có thể lọc theo danh mục linh kiện. Chỉ Manager/Admin được truy cập.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapPost("/api/admin/reports/export", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    ReportExportRequest request,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (request.Format is not ("xls" or "pdf"))
        return Results.BadRequest(new { message = "Chỉ hỗ trợ xuất định dạng Excel hoặc PDF." });
    if (request.FromDate.HasValue && request.ToDate.HasValue && request.FromDate.Value.Date > request.ToDate.Value.Date)
        return Results.BadRequest(new { message = "Khoảng thời gian bắt đầu không được lớn hơn ngày kết thúc." });
    if (request.CategoryId is <= 0)
        return Results.BadRequest(new { message = "Danh mục linh kiện không hợp lệ." });

    var details = System.Text.Json.JsonSerializer.Serialize(new
    {
        format = request.Format,
        fromDate = request.FromDate?.Date,
        toDate = request.ToDate?.Date,
        request.CategoryId
    });
    context.AuditLogs.Add(new AuditLog
    {
        Action = "EXPORT_REVENUE_REPORT",
        Module = "REPORTS",
        PerformedBy = manager.Email,
        IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
        Details = details,
        Timestamp = DateTime.UtcNow
    });
    await context.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { message = "Đã ghi nhận thao tác xuất báo cáo vào nhật ký hệ thống." });
})
    .WithSummary("Ghi nhận thao tác xuất báo cáo doanh thu")
    .WithDescription("Chỉ Manager/Admin được ghi nhận xuất Excel/PDF; thông tin định dạng và bộ lọc được lưu trong AuditLog.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapGet("/api/admin/users", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var employeeUsers = await context.Users
        .AsNoTracking()
        .Where(user => user.Role != UserRole.Customer)
        .OrderBy(user => user.FullName)
        .Select(user => new
        {
            user.Id,
            user.FullName,
            user.Email,
            user.PhoneNumber,
            role = user.Role.ToString(),
            user.IsActive,
            user.CreatedAt
        })
        .ToListAsync(cancellationToken);

    var allStatusHistory = await context.OrderStatusHistories
        .AsNoTracking()
        .Where(history => !string.IsNullOrWhiteSpace(history.ChangedByName))
        .Select(history => new
        {
            history.OrderId,
            ChangedByName = history.ChangedByName!,
            history.Status,
            history.ChangedAt
        })
        .ToListAsync(cancellationToken);

    var allOrders = await context.Orders
        .AsNoTracking()
        .Select(order => new
        {
            order.Id,
            order.CreatedAt,
            order.Status
        })
        .ToListAsync(cancellationToken);

    var employees = employeeUsers.Select(employee =>
    {
        var employeeHistory = allStatusHistory
            .Where(history => string.Equals(history.ChangedByName, employee.FullName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var handledOrderIds = employeeHistory
            .Select(history => history.OrderId)
            .Distinct()
            .ToList();

        var totalHandledOrders = handledOrderIds.Count;
        var successfulOrders = allOrders.Count(order => handledOrderIds.Contains(order.Id) && order.Status == OrderStatus.Completed);
        var successRate = totalHandledOrders == 0 ? 0m : (successfulOrders * 100m) / totalHandledOrders;
        var avgPackagingMinutes = employeeHistory
            .Where(history => history.Status == OrderStatus.Confirmed)
            .GroupBy(history => history.OrderId)
            .Select(group =>
            {
                var order = allOrders.FirstOrDefault(item => item.Id == group.Key);
                var firstConfirmedAt = group.Min(history => history.ChangedAt);
                if (order is null) return null;
                return (double?)(firstConfirmedAt - order.CreatedAt).TotalMinutes;
            })
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty(0d)
            .Average();

        DateTime? lastHandledAt = employeeHistory.Count == 0
            ? null
            : employeeHistory.Max(history => history.ChangedAt);

        return new
        {
            id = employee.Id,
            fullName = employee.FullName,
            email = employee.Email,
            phoneNumber = employee.PhoneNumber,
            role = employee.role,
            isActive = employee.IsActive,
            createdAt = employee.CreatedAt,
            totalHandledOrders,
            successfulOrders,
            successRate = Math.Round(successRate, 1),
            avgPackagingMinutes = Math.Round(avgPackagingMinutes, 1),
            lastHandledAt
        };
    }).ToList();

    return Results.Ok(employees);
})
    .WithSummary("Danh sách nhân viên và hiệu suất làm việc")
    .WithDescription("Cho phép Manager/Admin xem báo cáo hiệu suất, trạng thái tài khoản và thống kê xử lý đơn của từng nhân viên")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapGet("/api/admin/accounts", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    string? search,
    string? role,
    string? status,
    CancellationToken cancellationToken) =>
{
    var administrator = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (administrator is null) return Results.Unauthorized();
    if (administrator.Role != UserRole.Admin && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!string.IsNullOrWhiteSpace(role)
        && (!Enum.TryParse<UserRole>(role, true, out var parsedRole) || !Enum.IsDefined(parsedRole)))
        return Results.BadRequest(new { message = "Vai trò lọc không hợp lệ." });
    if (!string.IsNullOrWhiteSpace(status)
        && !string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(status, "Locked", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { message = "Trạng thái lọc không hợp lệ." });

    var query = context.Users.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(search))
    {
        var term = search.Trim().ToLowerInvariant();
        query = query.Where(user => user.FullName.ToLower().Contains(term)
            || user.Email.ToLower().Contains(term)
            || (user.PhoneNumber != null && user.PhoneNumber.ToLower().Contains(term)));
    }
    if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<UserRole>(role, true, out var accountRole))
        query = query.Where(user => user.Role == accountRole);
    if (string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase))
        query = query.Where(user => user.IsActive);
    else if (string.Equals(status, "Locked", StringComparison.OrdinalIgnoreCase))
        query = query.Where(user => !user.IsActive);

    var accounts = await query
        .OrderBy(user => user.FullName)
        .ThenBy(user => user.Id)
        .Select(user => new
        {
            id = user.Id,
            fullName = user.FullName,
            email = user.Email,
            phoneNumber = user.PhoneNumber,
            role = user.Role.ToString(),
            isActive = user.IsActive,
            isProtectedAdmin = user.Email.ToLower() == "supportadmin@pcstore.com"
                || user.Email.ToLower() == "admin@pcstore.vn",
            createdAt = user.CreatedAt
        })
        .ToListAsync(cancellationToken);
    return Results.Ok(accounts);
})
    .WithSummary("Tìm kiếm và lọc tài khoản toàn hệ thống")
    .WithDescription("Chỉ Admin được xem danh sách tài khoản; hỗ trợ tìm theo họ tên, email, số điện thoại và lọc theo vai trò/trạng thái.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapGet("/api/admin/accounts/{id:int}", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    int id,
    CancellationToken cancellationToken) =>
{
    var administrator = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (administrator is null) return Results.Unauthorized();
    if (administrator.Role != UserRole.Admin && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var account = await context.Users
        .AsNoTracking()
        .Where(user => user.Id == id)
        .Select(user => new
        {
            id = user.Id,
            fullName = user.FullName,
            email = user.Email,
            phoneNumber = user.PhoneNumber,
            role = user.Role.ToString(),
            isActive = user.IsActive,
            createdAt = user.CreatedAt,
            updatedAt = user.UpdatedAt
        })
        .FirstOrDefaultAsync(cancellationToken);
    return account is null ? Results.NotFound(new { message = "Không tìm thấy tài khoản." }) : Results.Ok(account);
})
    .WithSummary("Xem chi tiết tài khoản")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound);

app.MapPost("/api/admin/accounts", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    IConfiguration configuration,
    ILogger<Program> logger,
    CreateStaffAccountRequest request,
    CancellationToken cancellationToken) =>
{
    var administrator = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (administrator is null) return Results.Unauthorized();
    if (administrator.Role != UserRole.Admin && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var fullName = request.FullName?.Trim() ?? string.Empty;
    var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
    var phoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
    if (fullName.Length is < 2 or > 100)
        return Results.BadRequest(new { message = "Họ tên phải từ 2 đến 100 ký tự." });
    if (email.Length is < 5 or > 150 || !MailAddress.TryCreate(email, out _))
        return Results.BadRequest(new { message = "Địa chỉ email không hợp lệ." });
    if (phoneNumber?.Length > 20)
        return Results.BadRequest(new { message = "Số điện thoại không được vượt quá 20 ký tự." });
    if (!Enum.TryParse<UserRole>(request.Role, true, out var role)
        || (role is not (UserRole.Staff or UserRole.Manager)
            && !(role == UserRole.Admin && IsProtectedAdminEmail(email))))
        return Results.BadRequest(new { message = "Chỉ được tạo Nhân viên/Quản lý; Admin chỉ được tạo cho supportadmin@pcstore.com." });
    if (IsProtectedAdminEmail(email) && role != UserRole.Admin)
        return Results.BadRequest(new { message = "Email supportadmin@pcstore.com chỉ được tạo với vai trò Admin bảo vệ." });
    if (await context.Users.AnyAsync(user => user.Email.ToLower() == email, cancellationToken))
        return Results.Conflict(new { message = "Email này đã được sử dụng." });
    if (phoneNumber is not null && await context.Users.AnyAsync(user => user.PhoneNumber == phoneNumber, cancellationToken))
        return Results.Conflict(new { message = "Số điện thoại này đã được sử dụng." });

    var temporaryPassword = GenerateTemporaryAccountPassword();
    var account = new User
    {
        FullName = fullName,
        Email = email,
        PhoneNumber = phoneNumber,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporaryPassword),
        Role = role,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
    await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    context.Users.Add(account);
    context.AuditLogs.Add(new AuditLog
    {
        Action = role == UserRole.Admin ? "CREATE_PROTECTED_ADMIN" : "CREATE_STAFF_ACCOUNT",
        Module = "ACCOUNTS",
        PerformedBy = administrator.Email,
        IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
        Details = System.Text.Json.JsonSerializer.Serialize(new { account.Email, account.FullName, role = account.Role.ToString() }),
        Timestamp = DateTime.UtcNow
    });
    try
    {
        await context.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException exception)
    {
        logger.LogWarning(exception, "Account creation failed due to a database constraint for {AccountEmail}.", email);
        await transaction.RollbackAsync(cancellationToken);
        return Results.Conflict(new { message = "Email hoặc số điện thoại đã được sử dụng." });
    }

    if (!await SendAccountCredentialsEmailAsync(
        configuration, logger, account.Email, account.FullName, temporaryPassword, false, cancellationToken))
    {
        await transaction.RollbackAsync(cancellationToken);
        return Results.Problem(
            title: "Không thể gửi email tài khoản",
            detail: "Tài khoản chưa được tạo. Hãy kiểm tra cấu hình SMTP rồi thử lại.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    await transaction.CommitAsync(cancellationToken);
    return Results.Created($"/api/admin/accounts/{account.Id}", new
    {
        id = account.Id,
        fullName = account.FullName,
        email = account.Email,
        phoneNumber = account.PhoneNumber,
        role = account.Role.ToString(),
        isActive = account.IsActive,
        isProtectedAdmin = IsProtectedAdminAccount(account.Email),
        createdAt = account.CreatedAt,
        message = "Đã tạo tài khoản và gửi thông tin đăng nhập tạm thời qua email."
    });
})
    .WithSummary("Tạo tài khoản nhân viên, quản lý hoặc Admin bảo vệ")
    .WithDescription("Admin tạo tài khoản hoạt động với mật khẩu ngẫu nhiên; chỉ lưu hash mật khẩu và gửi thông tin đăng nhập qua email. Admin chỉ được tạo cho supportadmin@pcstore.com; vai trò Admin và quyền cốt lõi của tài khoản này không thể thay đổi.")
    .Produces(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status409Conflict)
    .Produces(StatusCodes.Status503ServiceUnavailable);

app.MapPost("/api/admin/accounts/{id:int}/reset-password", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    IConfiguration configuration,
    ILogger<Program> logger,
    int id,
    CancellationToken cancellationToken) =>
{
    var administrator = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (administrator is null) return Results.Unauthorized();
    if (administrator.Role != UserRole.Admin && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (administrator.Id == id)
        return Results.Conflict(new { message = "Không thể đặt lại mật khẩu của Admin đang đăng nhập." });

    var account = await context.Users.FirstOrDefaultAsync(user => user.Id == id, cancellationToken);
    if (account is null) return Results.NotFound(new { message = "Không tìm thấy tài khoản." });
    if (IsProtectedAdminAccount(account.Email))
        return Results.Conflict(new { message = "Tài khoản Admin bảo vệ không thể bị đặt lại mật khẩu từ quản lý tài khoản." });
    var temporaryPassword = GenerateTemporaryAccountPassword();
    await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporaryPassword);
    account.PasswordResetTokenHash = null;
    account.PasswordResetExpiresAt = null;
    account.PasswordResetRequestedAt = null;
    account.UpdatedAt = DateTime.UtcNow;
    context.AuditLogs.Add(new AuditLog
    {
        Action = "RESET_ACCOUNT_PASSWORD",
        Module = "ACCOUNTS",
        PerformedBy = administrator.Email,
        IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
        Details = System.Text.Json.JsonSerializer.Serialize(new { account.Id, account.Email }),
        Timestamp = DateTime.UtcNow
    });
    await context.SaveChangesAsync(cancellationToken);
    if (!await SendAccountCredentialsEmailAsync(
        configuration, logger, account.Email, account.FullName, temporaryPassword, true, cancellationToken))
    {
        await transaction.RollbackAsync(cancellationToken);
        return Results.Problem(
            title: "Không thể gửi email đặt lại mật khẩu",
            detail: "Mật khẩu chưa được thay đổi. Hãy kiểm tra cấu hình SMTP rồi thử lại.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    await transaction.CommitAsync(cancellationToken);
    return Results.Ok(new { message = "Đã đặt lại mật khẩu và gửi mật khẩu tạm thời đến email tài khoản." });
})
    .WithSummary("Đặt lại mật khẩu tài khoản")
    .WithDescription("Tạo mật khẩu ngẫu nhiên mới, lưu hash và gửi qua email; không trả mật khẩu tạm thời trong response.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status409Conflict)
    .Produces(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/admin/role-permissions/{role}", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    string role,
    CancellationToken cancellationToken) =>
{
    var administrator = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (administrator is null) return Results.Unauthorized();
    if (administrator.Role != UserRole.Admin) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!Enum.TryParse<UserRole>(role, true, out var selectedRole) || !Enum.IsDefined(selectedRole))
        return Results.BadRequest(new { message = "Vai trò không hợp lệ." });

    var stored = await context.RolePermissions.AsNoTracking()
        .Where(permission => permission.Role == selectedRole)
        .ToDictionaryAsync(permission => $"{permission.Module}:{permission.Action}", cancellationToken);
    var permissions = RolePermissionCatalog.Modules
        .SelectMany(module => RolePermissionCatalog.Actions.Select(action =>
        {
            var key = $"{module.Key}:{action.Key}";
            return new RolePermissionResponse(
                module.Key,
                action.Key,
                selectedRole == UserRole.Admin
                    || (stored.TryGetValue(key, out var saved)
                        ? saved.IsAllowed
                        : RolePermissionCatalog.IsAllowedByDefault(selectedRole, module.Key, action.Key)));
        }))
        .ToList();

    return Results.Ok(new RolePermissionMatrixResponse(
        selectedRole.ToString(),
        selectedRole == UserRole.Admin,
        RolePermissionCatalog.Modules,
        RolePermissionCatalog.Actions,
        permissions));
})
    .WithSummary("Lấy ma trận phân quyền của vai trò")
    .Produces<RolePermissionMatrixResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapPut("/api/admin/role-permissions/{role}", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    string role,
    UpdateRolePermissionsRequest request,
    CancellationToken cancellationToken) =>
{
    var administrator = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (administrator is null) return Results.Unauthorized();
    if (administrator.Role != UserRole.Admin) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!Enum.TryParse<UserRole>(role, true, out var selectedRole) || !Enum.IsDefined(selectedRole)
        || !string.Equals(request.Role, selectedRole.ToString(), StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { message = "Vai trò trong đường dẫn và nội dung yêu cầu không khớp." });
    if (selectedRole == UserRole.Admin)
        return Results.BadRequest(new { message = "Không thể thay đổi quyền cốt lõi của Admin." });
    if (request.Permissions is null
        || request.Permissions.Count != RolePermissionCatalog.Modules.Count * RolePermissionCatalog.Actions.Count
        || request.Permissions.Any(permission => !RolePermissionCatalog.IsValid(permission.Module, permission.Action))
        || request.Permissions.Select(permission => $"{permission.Module}:{permission.Action}").Distinct().Count() != request.Permissions.Count)
        return Results.BadRequest(new { message = "Ma trận quyền không đầy đủ hoặc chứa quyền không hợp lệ." });

    var existing = await context.RolePermissions
        .Where(permission => permission.Role == selectedRole)
        .ToDictionaryAsync(permission => $"{permission.Module}:{permission.Action}", cancellationToken);
    var changes = new List<object>();
    var timestamp = DateTime.UtcNow;
    foreach (var grant in request.Permissions)
    {
        var key = $"{grant.Module}:{grant.Action}";
        if (!existing.TryGetValue(key, out var permission))
        {
            permission = new RolePermission
            {
                Role = selectedRole,
                Module = grant.Module,
                Action = grant.Action,
                IsAllowed = RolePermissionCatalog.IsAllowedByDefault(selectedRole, grant.Module, grant.Action),
            };
            context.RolePermissions.Add(permission);
            existing[key] = permission;
        }

        if (permission.IsAllowed == grant.IsAllowed) continue;
        changes.Add(new { module = grant.Module, action = grant.Action, from = permission.IsAllowed, to = grant.IsAllowed });
        permission.IsAllowed = grant.IsAllowed;
        permission.UpdatedAt = timestamp;
        permission.UpdatedBy = administrator.Email;
    }

    if (changes.Count == 0)
        return Results.Ok(new { message = "Không có thay đổi quyền.", changedCount = 0 });

    context.AuditLogs.Add(new AuditLog
    {
        Action = "UPDATE_ROLE_PERMISSIONS",
        Module = "PERMISSIONS",
        PerformedBy = administrator.Email,
        IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
        Details = System.Text.Json.JsonSerializer.Serialize(new
        {
            role = selectedRole.ToString(),
            changes,
            changedCount = changes.Count,
        }),
        Timestamp = timestamp,
    });
    await context.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { message = "Đã lưu phân quyền.", changedCount = changes.Count });
})
    .WithSummary("Cập nhật ma trận phân quyền vai trò")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapPut("/api/admin/users/{id:int}/role", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    int id,
    UpdateUserRoleDto dto,
    CancellationToken cancellationToken) =>
{
    var administrator = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (administrator is null) return Results.Unauthorized();
    if (administrator.Role != UserRole.Admin && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (administrator.Id == id)
        return Results.Conflict(new { message = "Không thể thay đổi vai trò của Admin đang đăng nhập." });
    if (!Enum.TryParse<UserRole>(dto.Role, true, out var role) || !Enum.IsDefined(role))
        return Results.BadRequest(new { message = "Vai trò không hợp lệ." });
    var user = await context.Users.FirstOrDefaultAsync(account => account.Id == id, cancellationToken);
    if (user is null) return Results.NotFound(new { message = "Không tìm thấy người dùng." });
    if (IsProtectedAdminAccount(user.Email))
        return Results.Conflict(new { message = "Không thể thay đổi vai trò của tài khoản Admin bảo vệ." });
    var previousRole = user.Role;
    if (previousRole == role) return Results.Ok(new { message = "Vai trò không thay đổi.", role = role.ToString() });
    user.Role = role;
    user.UpdatedAt = DateTime.UtcNow;
    context.AuditLogs.Add(new AuditLog
    {
        Action = "UPDATE_ACCOUNT_ROLE",
        Module = "ACCOUNTS",
        PerformedBy = administrator.Email,
        IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
        Details = System.Text.Json.JsonSerializer.Serialize(new { user.Id, user.Email, from = previousRole.ToString(), to = role.ToString() }),
        Timestamp = DateTime.UtcNow
    });
    await context.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { message = "Cập nhật vai trò thành công.", role = role.ToString() });
})
    .WithSummary("Cập nhật vai trò tài khoản")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status409Conflict);

app.MapPut("/api/admin/users/{id:int}/toggle-active", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    int id,
    CancellationToken cancellationToken) =>
{
    var administrator = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (administrator is null) return Results.Unauthorized();
    if (administrator.Role != UserRole.Admin && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (administrator.Id == id)
        return Results.Conflict(new { message = "Không thể khóa hoặc mở khóa Admin đang đăng nhập." });
    var user = await context.Users.FirstOrDefaultAsync(account => account.Id == id, cancellationToken);
    if (user is null) return Results.NotFound(new { message = "Không tìm thấy người dùng." });
    if (IsProtectedAdminAccount(user.Email))
        return Results.Conflict(new { message = "Không thể khóa hoặc thay đổi trạng thái tài khoản Admin bảo vệ." });
    user.IsActive = !user.IsActive;
    user.UpdatedAt = DateTime.UtcNow;
    context.AuditLogs.Add(new AuditLog
    {
        Action = user.IsActive ? "UNLOCK_ACCOUNT" : "LOCK_ACCOUNT",
        Module = "ACCOUNTS",
        PerformedBy = administrator.Email,
        IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
        Details = System.Text.Json.JsonSerializer.Serialize(new { user.Id, user.Email, user.IsActive }),
        Timestamp = DateTime.UtcNow
    });
    await context.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { message = user.IsActive ? "Đã mở khóa tài khoản." : "Đã khóa tài khoản.", isActive = user.IsActive });
})
    .WithSummary("Khóa hoặc mở khóa tài khoản")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status409Conflict);

app.MapGet("/api/admin/categories", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var categories = await context.Categories
        .AsNoTracking()
        .OrderBy(category => category.DisplayOrder)
        .ThenBy(category => category.Name)
        .Select(category => new
        {
            id = category.Id,
            name = category.Name,
            slug = category.Slug,
            type = category.ComponentType.ToString(),
            description = category.Description,
            displayOrder = category.DisplayOrder,
            isActive = category.IsActive,
            productCount = category.Products.Count
        })
        .ToListAsync(cancellationToken);
    return Results.Ok(categories);
})
    .WithSummary("Quản lý danh mục linh kiện")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapPost("/api/admin/categories", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    CreateCategoryRequest request,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var name = request.Name?.Trim() ?? string.Empty;
    if (name.Length is < 1 or > 100 || request.DisplayOrder < 0)
        return Results.BadRequest(new { message = "Tên danh mục bắt buộc tối đa 100 ký tự và thứ tự hiển thị phải là số nguyên không âm." });
    if (!Enum.TryParse<ComponentType>(request.ComponentType, true, out var componentType)
        || !Enum.IsDefined(componentType))
        return Results.BadRequest(new { message = "Loại linh kiện không hợp lệ." });
    if (await context.Categories.AnyAsync(category => category.Name.ToLower() == name.ToLower(), cancellationToken))
        return Results.Conflict(new { message = "Tên danh mục đã tồn tại." });

    var slug = await CreateUniqueCategorySlugAsync(context, name, null, cancellationToken);
    var category = new Category
    {
        Name = name,
        Slug = slug,
        ComponentType = componentType,
        Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
        DisplayOrder = request.DisplayOrder,
        IsActive = true
    };
    context.Categories.Add(category);
    context.AuditLogs.Add(CreateCatalogAuditLog(
        httpContext,
        manager,
        "CREATE_CATEGORY",
        $"Tạo danh mục {category.Name} (slug: {category.Slug}, loại: {category.ComponentType}, thứ tự: {category.DisplayOrder})."));

    try
    {
        await context.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException exception)
    {
        app.Logger.LogWarning(exception, "Unique constraint rejected category creation for {CategoryName}.", name);
        return Results.Conflict(new { message = "Tên danh mục hoặc slug đã tồn tại. Vui lòng làm mới và thử lại." });
    }

    return Results.Created($"/api/admin/categories/{category.Id}", new
    {
        id = category.Id,
        name = category.Name,
        slug = category.Slug,
        type = category.ComponentType.ToString(),
        category.Description,
        displayOrder = category.DisplayOrder,
        isActive = category.IsActive
    });
})
    .WithSummary("Thêm danh mục linh kiện")
    .WithDescription("Tạo slug SEO tự động; tên và slug phải duy nhất.")
    .Produces(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status409Conflict);

app.MapPut("/api/admin/categories/{id:int}", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    int id,
    UpdateCategoryRequest request,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var category = await context.Categories.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (category is null) return Results.NotFound(new { message = "Không tìm thấy danh mục." });

    var name = request.Name?.Trim() ?? string.Empty;
    if (name.Length is < 1 or > 100 || request.DisplayOrder < 0)
        return Results.BadRequest(new { message = "Tên danh mục bắt buộc tối đa 100 ký tự và thứ tự hiển thị phải là số nguyên không âm." });
    if (!Enum.TryParse<ComponentType>(request.ComponentType, true, out var componentType)
        || !Enum.IsDefined(componentType))
        return Results.BadRequest(new { message = "Loại linh kiện không hợp lệ." });
    if (await context.Categories.AnyAsync(item => item.Id != id && item.Name.ToLower() == name.ToLower(), cancellationToken))
        return Results.Conflict(new { message = "Tên danh mục đã tồn tại." });

    var oldName = category.Name;
    var oldSlug = category.Slug;
    var slug = await CreateUniqueCategorySlugAsync(context, name, id, cancellationToken);
    category.Name = name;
    category.Slug = slug;
    category.ComponentType = componentType;
    category.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
    category.DisplayOrder = request.DisplayOrder;
    context.AuditLogs.Add(CreateCatalogAuditLog(
        httpContext,
        manager,
        "UPDATE_CATEGORY",
        $"Cập nhật danh mục #{id}: {oldName}/{oldSlug} -> {category.Name}/{category.Slug}; loại {category.ComponentType}; thứ tự {category.DisplayOrder}."));

    try
    {
        await context.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException exception)
    {
        app.Logger.LogWarning(exception, "Unique constraint rejected category update for {CategoryId}.", id);
        return Results.Conflict(new { message = "Tên danh mục hoặc slug đã tồn tại. Vui lòng làm mới và thử lại." });
    }

    return Results.Ok(new
    {
        message = "Cập nhật danh mục thành công.",
        id = category.Id,
        name = category.Name,
        slug = category.Slug,
        type = category.ComponentType.ToString(),
        category.Description,
        displayOrder = category.DisplayOrder,
        isActive = category.IsActive
    });
})
    .WithSummary("Cập nhật danh mục linh kiện")
    .WithDescription("Sinh lại slug SEO từ tên; danh mục có sản phẩm liên kết chỉ được ẩn, không xóa.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status409Conflict);

app.MapPatch("/api/admin/categories/{id:int}/active", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    int id,
    SetCatalogActiveRequest request,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var category = await context.Categories.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (category is null) return Results.NotFound(new { message = "Không tìm thấy danh mục." });
    category.IsActive = request.IsActive;
    context.AuditLogs.Add(CreateCatalogAuditLog(
        httpContext,
        manager,
        request.IsActive ? "ACTIVATE_CATEGORY" : "HIDE_CATEGORY",
        $"{(request.IsActive ? "Kích hoạt" : "Ẩn")} danh mục #{id} {category.Name}."));
    await context.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { id = category.Id, isActive = category.IsActive });
})
    .WithSummary("Ẩn hoặc kích hoạt danh mục")
    .WithDescription("Thay đổi trạng thái thay vì xóa danh mục để bảo toàn dữ liệu sản phẩm đã liên kết.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound);

app.MapGet("/api/admin/brands", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var brands = await context.Brands
        .AsNoTracking()
        .OrderBy(brand => brand.DisplayOrder)
        .ThenBy(brand => brand.Name)
        .Select(brand => new
        {
            id = brand.Id,
            name = brand.Name,
            slug = brand.Slug,
            logoUrl = brand.LogoUrl,
            description = brand.Description,
            displayOrder = brand.DisplayOrder,
            isActive = brand.IsActive,
            productCount = context.Products.Count(product => product.Brand != null && product.Brand.ToLower() == brand.Name.ToLower())
        })
        .ToListAsync(cancellationToken);
    return Results.Ok(brands);
})
    .WithSummary("Quản lý thương hiệu")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapPost("/api/admin/brands", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    CreateBrandRequest request,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var name = request.Name?.Trim() ?? string.Empty;
    if (name.Length is < 1 or > 100 || request.DisplayOrder < 0)
        return Results.BadRequest(new { message = "Tên thương hiệu bắt buộc tối đa 100 ký tự và thứ tự hiển thị phải là số nguyên không âm." });
    if (!IsValidOptionalHttpsUrl(request.LogoUrl))
        return Results.BadRequest(new { message = "Logo phải là URL HTTP/HTTPS hợp lệ." });
    if (await context.Brands.AnyAsync(brand => brand.Name.ToLower() == name.ToLower(), cancellationToken))
        return Results.Conflict(new { message = "Tên thương hiệu đã tồn tại." });

    var slug = await CreateUniqueBrandSlugAsync(context, name, null, cancellationToken);
    var brand = new Brand
    {
        Name = name,
        Slug = slug,
        LogoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl.Trim(),
        Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
        DisplayOrder = request.DisplayOrder,
        IsActive = true
    };
    context.Brands.Add(brand);
    context.AuditLogs.Add(CreateCatalogAuditLog(
        httpContext,
        manager,
        "CREATE_BRAND",
        $"Tạo thương hiệu {brand.Name} (slug: {brand.Slug}, thứ tự: {brand.DisplayOrder})."));

    try
    {
        await context.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException exception)
    {
        app.Logger.LogWarning(exception, "Unique constraint rejected brand creation for {BrandName}.", name);
        return Results.Conflict(new { message = "Tên thương hiệu hoặc slug đã tồn tại. Vui lòng làm mới và thử lại." });
    }
    return Results.Created($"/api/admin/brands/{brand.Id}", new
    {
        id = brand.Id,
        name = brand.Name,
        slug = brand.Slug,
        logoUrl = brand.LogoUrl,
        description = brand.Description,
        displayOrder = brand.DisplayOrder,
        isActive = brand.IsActive
    });
})
    .WithSummary("Thêm thương hiệu")
    .WithDescription("Tạo slug SEO tự động; tên và slug thương hiệu phải duy nhất.")
    .Produces(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status409Conflict);

app.MapPut("/api/admin/brands/{id:int}", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    int id,
    UpdateBrandRequest request,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var brand = await context.Brands.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (brand is null) return Results.NotFound(new { message = "Không tìm thấy thương hiệu." });
    var name = request.Name?.Trim() ?? string.Empty;
    if (name.Length is < 1 or > 100 || request.DisplayOrder < 0)
        return Results.BadRequest(new { message = "Tên thương hiệu bắt buộc tối đa 100 ký tự và thứ tự hiển thị phải là số nguyên không âm." });
    if (!IsValidOptionalHttpsUrl(request.LogoUrl))
        return Results.BadRequest(new { message = "Logo phải là URL HTTP/HTTPS hợp lệ." });
    if (await context.Brands.AnyAsync(item => item.Id != id && item.Name.ToLower() == name.ToLower(), cancellationToken))
        return Results.Conflict(new { message = "Tên thương hiệu đã tồn tại." });

    var oldName = brand.Name;
    var oldSlug = brand.Slug;
    var productsToRename = await context.Products
        .Where(product => product.Brand != null && product.Brand.ToLower() == oldName.ToLower())
        .ToListAsync(cancellationToken);
    foreach (var product in productsToRename)
        product.Brand = name;

    brand.Name = name;
    brand.Slug = await CreateUniqueBrandSlugAsync(context, name, id, cancellationToken);
    brand.LogoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl.Trim();
    brand.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
    brand.DisplayOrder = request.DisplayOrder;
    context.AuditLogs.Add(CreateCatalogAuditLog(
        httpContext,
        manager,
        "UPDATE_BRAND",
        $"Cập nhật thương hiệu #{id}: {oldName}/{oldSlug} -> {brand.Name}/{brand.Slug}; thứ tự {brand.DisplayOrder}."));

    try
    {
        await context.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException exception)
    {
        app.Logger.LogWarning(exception, "Unique constraint rejected brand update for {BrandId}.", id);
        return Results.Conflict(new { message = "Tên thương hiệu hoặc slug đã tồn tại. Vui lòng làm mới và thử lại." });
    }
    return Results.Ok(new
    {
        message = "Cập nhật thương hiệu thành công.",
        id = brand.Id,
        name = brand.Name,
        slug = brand.Slug,
        logoUrl = brand.LogoUrl,
        description = brand.Description,
        displayOrder = brand.DisplayOrder,
        isActive = brand.IsActive
    });
})
    .WithSummary("Cập nhật thương hiệu")
    .WithDescription("Sinh lại slug SEO từ tên thương hiệu.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status409Conflict);

app.MapPatch("/api/admin/brands/{id:int}/active", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    int id,
    SetCatalogActiveRequest request,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var brand = await context.Brands.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (brand is null) return Results.NotFound(new { message = "Không tìm thấy thương hiệu." });
    brand.IsActive = request.IsActive;
    context.AuditLogs.Add(CreateCatalogAuditLog(
        httpContext,
        manager,
        request.IsActive ? "ACTIVATE_BRAND" : "HIDE_BRAND",
        $"{(request.IsActive ? "Kích hoạt" : "Ẩn")} thương hiệu #{id} {brand.Name}."));
    await context.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { id = brand.Id, isActive = brand.IsActive });
})
    .WithSummary("Ẩn hoặc kích hoạt thương hiệu")
    .WithDescription("Ẩn thương hiệu khỏi bộ lọc mà không xóa sản phẩm hoặc dữ liệu liên kết.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound);

app.MapGet("/api/admin/products", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var products = await context.Products
        .AsNoTracking()
        .Include(product => product.Category)
        .Include(product => product.TechnicalSpec)
        .OrderByDescending(product => product.Id)
        .Select(product => new
        {
            id = product.Id,
            name = product.Name,
            sku = product.Sku,
            brand = product.Brand,
            description = product.Description,
            price = product.Price,
            promotionalPrice = product.PromotionalPrice,
            promotionStartsAt = product.PromotionStartsAt,
            promotionEndsAt = product.PromotionEndsAt,
            stockQuantity = product.StockQuantity,
            availableQuantity = product.StockQuantity,
            lowStockThreshold = product.LowStockThreshold,
            warehouseLocation = product.WarehouseLocation,
            imageUrl = product.ImageUrl,
            categoryId = product.CategoryId,
            categoryType = product.Category != null ? product.Category.ComponentType.ToString() : null,
            categoryName = product.Category != null ? product.Category.Name : null,
            isActive = product.IsActive,
            socket = product.TechnicalSpec != null ? product.TechnicalSpec.Socket : null,
            chipset = product.TechnicalSpec != null ? product.TechnicalSpec.Chipset : null,
            formFactor = product.TechnicalSpec != null ? product.TechnicalSpec.FormFactor : null,
            ramType = product.TechnicalSpec != null ? product.TechnicalSpec.RamType : null,
            ramBusSpeed = product.TechnicalSpec != null ? product.TechnicalSpec.RamBusSpeed : null,
            ramSlots = product.TechnicalSpec != null ? product.TechnicalSpec.RamSlots : null,
            tdpWattage = product.TechnicalSpec != null ? product.TechnicalSpec.TdpWattage : null,
            recommendedPsu = product.TechnicalSpec != null ? product.TechnicalSpec.RecommendedPsu : null,
            lengthMm = product.TechnicalSpec != null ? product.TechnicalSpec.LengthMm : null,
            additionalSpecsJson = product.TechnicalSpec != null ? product.TechnicalSpec.AdditionalSpecsJson : null
        })
        .ToListAsync(cancellationToken);
    return Results.Ok(products);
})
    .WithSummary("Danh sách sản phẩm trong trang quản trị")
    .WithDescription("Bao gồm sản phẩm đang hoạt động và đã ẩn cùng thông tin thông số kỹ thuật.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapPost("/api/admin/products", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    IHubContext<OrderNotificationsHub> inventoryNotifications,
    ILogger<Program> logger,
    CreateProductRequest dto,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var validationError = await ValidateProductInputAsync(context, dto, null, cancellationToken);
    if (validationError is not null) return Results.BadRequest(new { message = validationError });

    var category = await context.Categories.FirstAsync(item => item.Id == dto.CategoryId, cancellationToken);
    var product = new Product
    {
        Name = dto.Name.Trim(),
        Sku = dto.Sku.Trim(),
        Brand = string.IsNullOrWhiteSpace(dto.Brand) ? null : dto.Brand.Trim(),
        Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
        Price = dto.Price,
        PromotionalPrice = dto.PromotionalPrice,
        PromotionStartsAt = dto.PromotionStartsAt,
        PromotionEndsAt = dto.PromotionEndsAt,
        StockQuantity = dto.StockQuantity,
        CategoryId = dto.CategoryId,
        ImageUrl = string.IsNullOrWhiteSpace(dto.ImageUrl) ? null : dto.ImageUrl.Trim(),
        IsActive = true,
        TechnicalSpec = CreateTechnicalSpec(dto)
    };
    context.Products.Add(product);
    context.AuditLogs.Add(CreateCatalogAuditLog(
        httpContext,
        manager,
        "CREATE_PRODUCT",
        CreateProductAuditDetails(null, dto, category.Name)));

    try
    {
        await context.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException exception)
    {
        app.Logger.LogWarning(exception, "Database rejected creation of product with SKU {Sku}.", product.Sku);
        return Results.Conflict(new { message = "SKU đã tồn tại hoặc dữ liệu sản phẩm không hợp lệ." });
    }

    await NotifyInventoryChangedAsync(inventoryNotifications, logger, product.Id, cancellationToken);
    return Results.Created($"/api/admin/products/{product.Id}", new { message = "Thêm sản phẩm thành công!", id = product.Id });
})
    .WithSummary("Thêm sản phẩm và thông số kỹ thuật")
    .WithDescription("Chỉ Manager/Admin được tạo sản phẩm; ảnh tải lên tối đa 5 MB.")
    .Produces(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status409Conflict);

app.MapPut("/api/admin/products/{id:int}", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    IHubContext<OrderNotificationsHub> inventoryNotifications,
    ILogger<Program> logger,
    int id,
    CreateProductRequest dto,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);

    await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    var lockedProduct = await context.Products
        .FromSqlInterpolated($"SELECT * FROM \"Products\" WHERE \"Id\" = {id} FOR UPDATE")
        .FirstOrDefaultAsync(cancellationToken);
    if (lockedProduct is null) return Results.NotFound(new { message = "Không tìm thấy sản phẩm." });
    var product = await context.Products
        .Include(item => item.TechnicalSpec)
        .Include(item => item.Category)
        .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (product is null) return Results.NotFound(new { message = "Không tìm thấy sản phẩm." });

    var validationError = await ValidateProductInputAsync(context, dto, id, cancellationToken);
    if (validationError is not null) return Results.BadRequest(new { message = validationError });
    var category = await context.Categories.FirstAsync(item => item.Id == dto.CategoryId, cancellationToken);
    var auditDetails = CreateProductAuditDetails(product, dto, category.Name);
    var previousStockQuantity = product.StockQuantity;
    var heldQuantity = previousStockQuantity != dto.StockQuantity
        ? await GetHeldInventoryQuantityAsync(context, product.Id, cancellationToken)
        : 0;
    product.Name = dto.Name.Trim();
    product.Sku = dto.Sku.Trim();
    product.Brand = string.IsNullOrWhiteSpace(dto.Brand) ? null : dto.Brand.Trim();
    product.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
    product.Price = dto.Price;
    product.PromotionalPrice = dto.PromotionalPrice;
    product.PromotionStartsAt = dto.PromotionStartsAt;
    product.PromotionEndsAt = dto.PromotionEndsAt;
    product.StockQuantity = dto.StockQuantity;
    product.CategoryId = dto.CategoryId;
    product.ImageUrl = string.IsNullOrWhiteSpace(dto.ImageUrl) ? null : dto.ImageUrl.Trim();
    product.TechnicalSpec = UpdateTechnicalSpec(product.TechnicalSpec, dto);
    context.AuditLogs.Add(CreateCatalogAuditLog(
        httpContext,
        manager,
        "UPDATE_PRODUCT",
        $"Cập nhật sản phẩm #{id}: {auditDetails}"));
    if (previousStockQuantity != dto.StockQuantity)
    {
        context.AuditLogs.Add(CreateInventoryAuditLog(
            httpContext,
            manager,
            "UPDATE_STOCK_FROM_PRODUCT",
            $"SKU {product.Sku}: tồn khả dụng {previousStockQuantity} -> {dto.StockQuantity}; đang giữ {heldQuantity}."));
    }

    try
    {
        await context.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException exception)
    {
        app.Logger.LogWarning(exception, "Database rejected update of product {ProductId} with SKU {Sku}.", id, product.Sku);
        return Results.Conflict(new { message = "SKU đã tồn tại hoặc dữ liệu sản phẩm không hợp lệ." });
    }
    await transaction.CommitAsync(cancellationToken);
    if (previousStockQuantity != dto.StockQuantity)
        await NotifyInventoryChangedAsync(inventoryNotifications, logger, product.Id, cancellationToken);
    return Results.Ok(new { message = "Cập nhật sản phẩm thành công!" });
})
    .WithSummary("Cập nhật sản phẩm và thông số kỹ thuật")
    .WithDescription("Lưu các trường thông tin, ảnh và thông số tương ứng với danh mục; ghi nhận thay đổi vào Audit Log.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status409Conflict);

app.MapPut("/api/admin/products/{id:int}/stock", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    IHubContext<OrderNotificationsHub> inventoryNotifications,
    ILogger<Program> logger,
    int id,
    UpdateStockDto dto,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (dto.StockQuantity < 0) return Results.BadRequest(new { message = "Tồn kho không được là số âm." });
    await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    var product = await context.Products
        .FromSqlInterpolated($"SELECT * FROM \"Products\" WHERE \"Id\" = {id} FOR UPDATE")
        .FirstOrDefaultAsync(cancellationToken);
    if (product is null) return Results.NotFound(new { message = "Không tìm thấy sản phẩm." });
    var previousStockQuantity = product.StockQuantity;
    var heldQuantity = await GetHeldInventoryQuantityAsync(context, product.Id, cancellationToken);
    product.StockQuantity = dto.StockQuantity;
    context.AuditLogs.Add(CreateInventoryAuditLog(httpContext, manager, "UPDATE_PRODUCT_STOCK",
        $"Cập nhật tồn khả dụng SKU {product.Sku}: {previousStockQuantity} -> {product.StockQuantity}; đang giữ {heldQuantity}."));
    await context.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    await NotifyInventoryChangedAsync(inventoryNotifications, logger, product.Id, cancellationToken);
    return Results.Ok(new { message = "Cập nhật tồn kho thành công!", stockQuantity = product.StockQuantity });
});

app.MapGet("/api/admin/inventory", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    string? searchTerm,
    int? categoryId,
    string? stockStatus,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (categoryId is <= 0)
        return Results.BadRequest(new { message = "Danh mục không hợp lệ." });
    if (!string.IsNullOrWhiteSpace(stockStatus)
        && !new[] { "Available", "LowStock", "OutOfStock" }.Contains(stockStatus, StringComparer.OrdinalIgnoreCase))
        return Results.BadRequest(new { message = "Trạng thái tồn kho không hợp lệ." });

    var normalizedSearch = searchTerm?.Trim().ToLowerInvariant();
    var inventoryItems = await context.Products
        .AsNoTracking()
        .Include(product => product.Category)
        .Where(product => !categoryId.HasValue || product.CategoryId == categoryId.Value)
        .Where(product => string.IsNullOrEmpty(normalizedSearch)
            || product.Name.ToLower().Contains(normalizedSearch)
            || product.Sku.ToLower().Contains(normalizedSearch))
        .OrderBy(product => product.Name)
        .Select(product => new
        {
            product.Id,
            product.Name,
            product.Sku,
            product.CategoryId,
            CategoryName = product.Category!.Name,
            product.StockQuantity,
            product.LowStockThreshold,
            product.WarehouseLocation
        })
        .ToListAsync(cancellationToken);

    var productIds = inventoryItems.Select(product => product.Id).ToList();
    var heldQuantities = await context.OrderDetails
        .AsNoTracking()
        .Where(detail => detail.Order != null
            && productIds.Contains(detail.ProductId)
            && detail.Order.InventoryReserved
            && (detail.Order.Status == OrderStatus.Confirmed || detail.Order.Status == OrderStatus.Shipping))
        .GroupBy(detail => detail.ProductId)
        .Select(group => new { ProductId = group.Key, Quantity = group.Sum(detail => detail.Quantity) })
        .ToDictionaryAsync(item => item.ProductId, item => item.Quantity, cancellationToken);

    var result = inventoryItems.Select(product =>
    {
        var heldQuantity = heldQuantities.GetValueOrDefault(product.Id);
        var availableQuantity = product.StockQuantity;
        var status = availableQuantity <= 0
            ? "OutOfStock"
            : availableQuantity <= product.LowStockThreshold ? "LowStock" : "Available";
        return new
        {
            id = product.Id,
            name = product.Name,
            sku = product.Sku,
            categoryId = product.CategoryId,
            categoryName = product.CategoryName,
            actualStock = availableQuantity + heldQuantity,
            heldQuantity,
            availableQuantity,
            lowStockThreshold = product.LowStockThreshold,
            status,
            warehouseLocation = product.WarehouseLocation
        };
    });
    if (!string.IsNullOrWhiteSpace(stockStatus))
        result = result.Where(item => string.Equals(item.status, stockStatus, StringComparison.OrdinalIgnoreCase));
    return Results.Ok(result.ToList());
})
    .WithSummary("Danh sách tồn kho")
    .WithDescription("Trả tồn thực tế, số lượng đang giữ cho đơn đã xác nhận, số lượng khả dụng và cảnh báo sắp hết hàng.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapPost("/api/admin/inventory/{id:int}/receive", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    IHubContext<OrderNotificationsHub> inventoryNotifications,
    ILogger<Program> logger,
    int id,
    ReceiveInventoryRequest request,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (request.Quantity <= 0 || (request.UnitCost.HasValue && request.UnitCost.Value < 0)
        || (request.Supplier?.Trim().Length ?? 0) > 200
        || (request.Note?.Trim().Length ?? 0) > 500)
        return Results.BadRequest(new { message = "Số lượng nhập phải lớn hơn 0; giá nhập, nhà cung cấp hoặc ghi chú không hợp lệ." });

    await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    var product = await context.Products
        .FromSqlInterpolated($"SELECT * FROM \"Products\" WHERE \"Id\" = {id} FOR UPDATE")
        .FirstOrDefaultAsync(cancellationToken);
    if (product is null) return Results.NotFound(new { message = "Không tìm thấy linh kiện." });
    if (request.Quantity > int.MaxValue - product.StockQuantity)
        return Results.BadRequest(new { message = "Số lượng nhập vượt giới hạn tồn kho cho phép." });

    var beforeAvailable = product.StockQuantity;
    var heldQuantity = await GetHeldInventoryQuantityAsync(context, id, cancellationToken);
    product.StockQuantity += request.Quantity;
    context.AuditLogs.Add(CreateInventoryAuditLog(
        httpContext,
        manager,
        "RECEIVE_STOCK",
        System.Text.Json.JsonSerializer.Serialize(new
        {
            productId = product.Id,
            product.Sku,
            quantityReceived = request.Quantity,
            availableBefore = beforeAvailable,
            availableAfter = product.StockQuantity,
            heldQuantity,
            unitCost = request.UnitCost,
            supplier = request.Supplier?.Trim(),
            note = request.Note?.Trim()
        })));
    await context.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    await NotifyInventoryChangedAsync(inventoryNotifications, logger, product.Id, cancellationToken);
    return Results.Ok(new
    {
        message = "Nhập kho linh kiện thành công.",
        productId = product.Id,
        actualStock = product.StockQuantity + heldQuantity,
        heldQuantity,
        availableQuantity = product.StockQuantity
    });
})
    .WithSummary("Nhập thêm hàng vào kho")
    .WithDescription("Cộng số lượng hàng nhập vào tồn khả dụng và ghi nhà cung cấp, giá nhập, lý do vào Audit Log.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound);

app.MapPost("/api/admin/inventory/{id:int}/adjust", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    IHubContext<OrderNotificationsHub> inventoryNotifications,
    ILogger<Program> logger,
    int id,
    AdjustInventoryRequest request,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (request.ActualStock < 0 || request.ActualStock > 2_000_000_000
        || string.IsNullOrWhiteSpace(request.Note)
        || request.Note.Trim().Length > 500)
        return Results.BadRequest(new { message = "Tồn thực tế phải từ 0 đến 2 tỷ và cần ghi rõ lý do điều chỉnh." });

    await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    var product = await context.Products
        .FromSqlInterpolated($"SELECT * FROM \"Products\" WHERE \"Id\" = {id} FOR UPDATE")
        .FirstOrDefaultAsync(cancellationToken);
    if (product is null) return Results.NotFound(new { message = "Không tìm thấy linh kiện." });
    var heldQuantity = await GetHeldInventoryQuantityAsync(context, id, cancellationToken);
    if (request.ActualStock < heldQuantity)
        return Results.Conflict(new
        {
            message = $"Tồn thực tế không thể thấp hơn số lượng đang giữ ({heldQuantity}).",
            heldQuantity
        });

    var actualBefore = product.StockQuantity + heldQuantity;
    var availableBefore = product.StockQuantity;
    product.StockQuantity = request.ActualStock - heldQuantity;
    context.AuditLogs.Add(CreateInventoryAuditLog(
        httpContext,
        manager,
        "ADJUST_STOCK",
        System.Text.Json.JsonSerializer.Serialize(new
        {
            productId = product.Id,
            product.Sku,
            actualBefore,
            actualAfter = request.ActualStock,
            availableBefore,
            availableAfter = product.StockQuantity,
            heldQuantity,
            note = request.Note.Trim()
        })));
    await context.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    await NotifyInventoryChangedAsync(inventoryNotifications, logger, product.Id, cancellationToken);
    return Results.Ok(new
    {
        message = "Điều chỉnh tồn kho thành công.",
        actualStock = request.ActualStock,
        heldQuantity,
        availableQuantity = product.StockQuantity
    });
})
    .WithSummary("Điều chỉnh kiểm kê tồn kho")
    .WithDescription("Đặt tồn thực tế theo kết quả kiểm kê; số hàng đã giữ cho đơn được bảo toàn và tồn khả dụng được tính lại.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status409Conflict);

app.MapPatch("/api/admin/inventory/{id:int}/threshold", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    IHubContext<OrderNotificationsHub> inventoryNotifications,
    ILogger<Program> logger,
    int id,
    UpdateLowStockThresholdRequest request,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (request.Threshold is < 0 or > 1_000_000)
        return Results.BadRequest(new { message = "Ngưỡng cảnh báo phải từ 0 đến 1.000.000." });

    var product = await context.Products.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (product is null) return Results.NotFound(new { message = "Không tìm thấy linh kiện." });
    var previousThreshold = product.LowStockThreshold;
    product.LowStockThreshold = request.Threshold;
    context.AuditLogs.Add(CreateInventoryAuditLog(
        httpContext,
        manager,
        "UPDATE_LOW_STOCK_THRESHOLD",
        $"SKU {product.Sku}: ngưỡng cảnh báo {previousThreshold} -> {product.LowStockThreshold}."));
    await context.SaveChangesAsync(cancellationToken);
    await NotifyInventoryChangedAsync(inventoryNotifications, logger, product.Id, cancellationToken);
    return Results.Ok(new { productId = product.Id, lowStockThreshold = product.LowStockThreshold });
})
    .WithSummary("Cập nhật ngưỡng cảnh báo sắp hết hàng")
    .WithDescription("Lưu ngưỡng cảnh báo riêng cho linh kiện và ghi nhận thao tác vào Audit Log.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound);

app.MapPut("/api/admin/products/{id:int}/warehouse-location", async (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    int id,
    UpdateWarehouseLocationRequest request,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var location = request.WarehouseLocation?.Trim();
    if ((location?.Length ?? 0) > 100)
        return Results.BadRequest(new { message = "Vị trí kho không được vượt quá 100 ký tự." });

    var product = await context.Products.FirstOrDefaultAsync(current => current.Id == id, cancellationToken);
    if (product is null)
        return Results.NotFound(new { message = "Không tìm thấy sản phẩm." });

    product.WarehouseLocation = string.IsNullOrWhiteSpace(location) ? null : location;
    context.AuditLogs.Add(CreateCatalogAuditLog(httpContext, manager, "UPDATE_PRODUCT_LOCATION",
        $"Cập nhật vị trí kho SKU {product.Sku}: {product.WarehouseLocation ?? "(trống)"}."));
    await context.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { message = "Đã cập nhật vị trí kho.", warehouseLocation = product.WarehouseLocation });
})
    .WithSummary("Cập nhật vị trí lưu kho của sản phẩm")
    .WithDescription("Lưu mã kệ/vị trí để hiển thị trên phiếu đóng gói và xuất kho.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound);

app.MapPut("/api/admin/products/{id:int}/price", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    int id,
    UpdatePriceDto dto,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (dto.Price <= 0) return Results.BadRequest(new { message = "Giá sản phẩm phải lớn hơn 0." });
    var product = await context.Products.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (product is null) return Results.NotFound(new { message = "Không tìm thấy sản phẩm." });
    var previousPrice = product.Price;
    if (product.PromotionalPrice.HasValue && product.PromotionalPrice.Value > dto.Price)
        return Results.BadRequest(new { message = "Giá niêm yết mới không được thấp hơn giá khuyến mãi đang cấu hình." });
    product.Price = dto.Price;
    context.AuditLogs.Add(CreateCatalogAuditLog(httpContext, manager, "UPDATE_PRODUCT_PRICE",
        System.Text.Json.JsonSerializer.Serialize(new
        {
            productId = product.Id,
            sku = product.Sku,
            product.Name,
            oldValue = new { price = previousPrice },
            newValue = new { price = product.Price },
        })));
    await context.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { message = "Cập nhật giá thành công!", price = product.Price });
});

app.MapDelete("/api/admin/products/{id:int}", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    int id,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var product = await context.Products.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (product is null) return Results.NotFound(new { message = "Không tìm thấy sản phẩm." });
    var previousValue = new { product.IsActive, product.Name, product.Sku };
    product.IsActive = false;
    context.AuditLogs.Add(CreateCatalogAuditLog(httpContext, manager, "HIDE_PRODUCT",
        System.Text.Json.JsonSerializer.Serialize(new
        {
            productId = id,
            oldValue = previousValue,
            newValue = new { product.IsActive, product.Name, product.Sku },
        })));
    await context.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { message = "Đã ẩn sản phẩm." });
});

app.MapPatch("/api/admin/products/{id:int}/active", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    int id,
    SetCatalogActiveRequest request,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var product = await context.Products.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (product is null) return Results.NotFound(new { message = "Không tìm thấy sản phẩm." });
    product.IsActive = request.IsActive;
    context.AuditLogs.Add(CreateCatalogAuditLog(
        httpContext,
        manager,
        request.IsActive ? "ACTIVATE_PRODUCT" : "HIDE_PRODUCT",
        $"{(request.IsActive ? "Kích hoạt" : "Ẩn")} sản phẩm #{id}, SKU {product.Sku}, tên {product.Name}."));
    await context.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { id = product.Id, isActive = product.IsActive });
})
    .WithSummary("Ẩn hoặc kích hoạt sản phẩm")
    .WithDescription("Thay đổi trạng thái sản phẩm mà không xóa sản phẩm hoặc lịch sử giao dịch.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound);

app.MapGet("/api/admin/vouchers", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var vouchers = await context.Vouchers
        .AsNoTracking()
        .OrderByDescending(voucher => voucher.CreatedAt)
        .Select(voucher => new
        {
            id = voucher.Id,
            code = voucher.Code,
            discountType = voucher.DiscountType,
            discountValue = voucher.DiscountValue,
            minimumOrderAmount = voucher.MinimumOrderAmount,
            maximumDiscountAmount = voucher.MaximumDiscountAmount,
            usageLimit = voucher.UsageLimit,
            usageCount = voucher.UsageCount,
            startsAt = voucher.StartsAt,
            endsAt = voucher.EndsAt,
            isActive = voucher.IsActive,
            createdAt = voucher.CreatedAt
        })
        .ToListAsync(cancellationToken);
    return Results.Ok(vouchers);
})
    .WithSummary("Danh sách mã voucher")
    .WithDescription("Quản lý khuyến mãi voucher; thời gian hiệu lực được kiểm tra tự động.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapPost("/api/admin/vouchers", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    CreateVoucherRequest request,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var validationError = await ValidateVoucherRequestAsync(context, request, null, cancellationToken);
    if (validationError is not null) return Results.BadRequest(new { message = validationError });
    var normalizedCode = request.Code.Trim().ToUpperInvariant();
    if (await context.Vouchers.AnyAsync(voucher => voucher.Code.ToLower() == normalizedCode.ToLower(), cancellationToken))
        return Results.Conflict(new { message = "Mã voucher đã tồn tại." });

    var voucher = new Voucher
    {
        Code = normalizedCode,
        DiscountType = NormalizeVoucherDiscountType(request.DiscountType),
        DiscountValue = request.DiscountValue,
        MinimumOrderAmount = request.MinimumOrderAmount,
        MaximumDiscountAmount = request.MaximumDiscountAmount,
        UsageLimit = request.UsageLimit,
        StartsAt = request.StartsAt,
        EndsAt = request.EndsAt,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
    context.Vouchers.Add(voucher);
    context.AuditLogs.Add(CreateCatalogAuditLog(
        httpContext,
        manager,
        "CREATE_VOUCHER",
        $"Tạo voucher {voucher.Code}: {voucher.DiscountType} {voucher.DiscountValue}; đơn tối thiểu {voucher.MinimumOrderAmount}; giới hạn {voucher.UsageLimit}; hiệu lực {voucher.StartsAt:O} - {voucher.EndsAt:O}."));
    try
    {
        await context.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException exception)
    {
        app.Logger.LogWarning(exception, "Database rejected voucher code {VoucherCode}.", normalizedCode);
        return Results.Conflict(new { message = "Mã voucher đã tồn tại." });
    }
    return Results.Created($"/api/admin/vouchers/{voucher.Id}", new { voucher.Id, voucher.Code });
})
    .WithSummary("Tạo mã voucher")
    .WithDescription("Tạo voucher giảm theo phần trăm hoặc số tiền cố định với giới hạn lượt và thời gian.")
    .Produces(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status409Conflict);

app.MapPut("/api/admin/vouchers/{id:int}", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    int id,
    CreateVoucherRequest request,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var voucher = await context.Vouchers.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (voucher is null) return Results.NotFound(new { message = "Không tìm thấy voucher." });
    var validationError = await ValidateVoucherRequestAsync(context, request, id, cancellationToken);
    if (validationError is not null) return Results.BadRequest(new { message = validationError });
    var normalizedCode = request.Code.Trim().ToUpperInvariant();
    if (await context.Vouchers.AnyAsync(item => item.Id != id && item.Code.ToLower() == normalizedCode.ToLower(), cancellationToken))
        return Results.Conflict(new { message = "Mã voucher đã tồn tại." });

    var oldState = new
    {
        voucher.Code,
        voucher.DiscountType,
        voucher.DiscountValue,
        voucher.MinimumOrderAmount,
        voucher.MaximumDiscountAmount,
        voucher.UsageLimit,
        voucher.StartsAt,
        voucher.EndsAt
    };
    voucher.Code = normalizedCode;
    voucher.DiscountType = NormalizeVoucherDiscountType(request.DiscountType);
    voucher.DiscountValue = request.DiscountValue;
    voucher.MinimumOrderAmount = request.MinimumOrderAmount;
    voucher.MaximumDiscountAmount = request.MaximumDiscountAmount;
    voucher.UsageLimit = request.UsageLimit;
    voucher.StartsAt = request.StartsAt;
    voucher.EndsAt = request.EndsAt;
    voucher.UpdatedAt = DateTimeOffset.UtcNow;
    var newState = new
    {
        voucher.Code,
        voucher.DiscountType,
        voucher.DiscountValue,
        voucher.MinimumOrderAmount,
        voucher.MaximumDiscountAmount,
        voucher.UsageLimit,
        voucher.StartsAt,
        voucher.EndsAt
    };
    context.AuditLogs.Add(CreateCatalogAuditLog(
        httpContext,
        manager,
        "UPDATE_VOUCHER",
        System.Text.Json.JsonSerializer.Serialize(new { before = oldState, after = newState })));
    try
    {
        await context.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException exception)
    {
        app.Logger.LogWarning(exception, "Database rejected voucher update for {VoucherId}.", id);
        return Results.Conflict(new { message = "Mã voucher đã tồn tại." });
    }
    return Results.Ok(new { message = "Cập nhật voucher thành công." });
})
    .WithSummary("Cập nhật voucher")
    .WithDescription("Cập nhật điều kiện, giới hạn sử dụng, mã và thời gian của voucher.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status409Conflict);

app.MapPatch("/api/admin/vouchers/{id:int}/active", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    int id,
    SetCatalogActiveRequest request,
    CancellationToken cancellationToken) =>
{
    var manager = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (manager is null) return Results.Unauthorized();
    if (manager.Role is not (UserRole.Manager or UserRole.Admin) && !httpContext.Items.ContainsKey("RolePermissionAuthorized")) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var voucher = await context.Vouchers.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (voucher is null) return Results.NotFound(new { message = "Không tìm thấy voucher." });

    voucher.IsActive = request.IsActive;
    voucher.UpdatedAt = DateTimeOffset.UtcNow;
    context.AuditLogs.Add(CreateCatalogAuditLog(
        httpContext,
        manager,
        request.IsActive ? "ACTIVATE_VOUCHER" : "DEACTIVATE_VOUCHER",
        $"{(request.IsActive ? "Kích hoạt" : "Ngừng")} voucher {voucher.Code}."));
    await context.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { id = voucher.Id, isActive = voucher.IsActive });
})
    .WithSummary("Kích hoạt hoặc ngừng voucher")
    .WithDescription("Tắt voucher thay vì xóa để bảo toàn lịch sử đã sử dụng.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound);

app.MapPost("/api/vouchers/validate", async Task<IResult> (
    AppDbContext context,
    ValidateVoucherRequest request,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Code)
        || request.OrderSubtotal <= 0
        || request.OrderSubtotal > 1_000_000_000m)
        return Results.BadRequest(new { message = "Nhập mã voucher hợp lệ và giá trị đơn hàng lớn hơn 0." });
    var voucher = await context.Vouchers
        .AsNoTracking()
        .FirstOrDefaultAsync(item => item.Code.ToLower() == request.Code.Trim().ToLower(), cancellationToken);
    if (voucher is null)
        return Results.BadRequest(new { message = "Mã voucher không tồn tại hoặc đã ngừng hoạt động." });
    var error = GetVoucherEligibilityError(voucher, request.OrderSubtotal, DateTimeOffset.UtcNow);
    if (error is not null) return Results.BadRequest(new { message = error });
    var discount = CalculateVoucherDiscount(voucher, request.OrderSubtotal);
    return Results.Ok(new
    {
        code = voucher.Code,
        discountAmount = discount,
        orderSubtotal = request.OrderSubtotal,
        totalAmount = request.OrderSubtotal - discount,
        endsAt = voucher.EndsAt
    });
})
    .WithSummary("Kiểm tra mã voucher")
    .WithDescription("Xác thực thời gian, mức đơn hàng tối thiểu, giới hạn lượt và số tiền giảm trước khi thanh toán.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest);

app.MapGet("/api/admin/system-configuration", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    DatabaseConnectionProvider databaseConnectionProvider,
    CancellationToken cancellationToken) =>
{
    var administrator = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (administrator is null) return Results.Unauthorized();
    if (administrator.Role != UserRole.Admin) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var values = await context.SystemSettings.AsNoTracking()
        .Where(setting => setting.Key.StartsWith("system."))
        .ToDictionaryAsync(setting => setting.Key, setting => setting.Value, cancellationToken);
    var protector = dataProtectionProvider.CreateProtector("PCSTORE.API.SystemConfiguration.v1");
    var currentConnectionString = databaseConnectionProvider.ConnectionString;
    var pendingRestart = false;
    var passwordConfigured = false;
    if (values.TryGetValue("system.database.connection", out var encryptedConnection)
        && !string.IsNullOrWhiteSpace(encryptedConnection))
    {
        currentConnectionString = protector.Unprotect(encryptedConnection);
        pendingRestart = !string.Equals(
            new NpgsqlConnectionStringBuilder(currentConnectionString).ConnectionString,
            new NpgsqlConnectionStringBuilder(databaseConnectionProvider.ConnectionString).ConnectionString,
            StringComparison.Ordinal);
    }

    var database = new NpgsqlConnectionStringBuilder(currentConnectionString);
    passwordConfigured = !string.IsNullOrWhiteSpace(database.Password);
    var aiKeyConfigured = values.TryGetValue("system.ai.api-key", out var encryptedAiKey)
        && !string.IsNullOrWhiteSpace(encryptedAiKey)
        && !string.IsNullOrWhiteSpace(protector.Unprotect(encryptedAiKey));
    decimal ReadDecimal(string key, decimal fallback) =>
        values.TryGetValue(key, out var raw) && decimal.TryParse(
            raw,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
                ? value
                : fallback;
    int ReadInt(string key, int fallback) =>
        values.TryGetValue(key, out var raw) && int.TryParse(raw, out var value) ? value : fallback;

    return Results.Ok(new SystemConfigurationResponse(
        database.Host ?? string.Empty,
        database.Port,
        database.Database ?? string.Empty,
        database.Username ?? string.Empty,
        passwordConfigured,
        pendingRestart,
        aiKeyConfigured,
        values.GetValueOrDefault("system.ai.model", "gemini-3.8-flash"),
        ReadDecimal("system.ai.temperature", 0.7m),
        ReadInt("system.ai.max-tokens", 1024),
        ReadInt("system.rag.top-k", 5),
        ReadDecimal("system.rag.threshold", 0.65m)));
})
    .WithSummary("Lấy cấu hình database và AI đã che thông tin bí mật")
    .WithDescription("Chỉ Admin được xem host, database, model và tham số; mật khẩu và API Key không bao giờ được trả về.")
    .Produces<SystemConfigurationResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapPost("/api/admin/system-configuration/test-database", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    DatabaseConnectionProvider databaseConnectionProvider,
    DatabaseConfigurationRequest request,
    CancellationToken cancellationToken) =>
{
    var administrator = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (administrator is null) return Results.Unauthorized();
    if (administrator.Role != UserRole.Admin) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var validationError = ValidateDatabaseConfiguration(request);
    if (validationError is not null) return Results.BadRequest(new { message = validationError });

    try
    {
        var databaseRequest = request;
        if (string.IsNullOrEmpty(databaseRequest.Password))
        {
            var protectedConnection = await context.SystemSettings.AsNoTracking()
                .Where(setting => setting.Key == "system.database.connection")
                .Select(setting => setting.Value)
                .FirstOrDefaultAsync(cancellationToken);
            var currentConnectionString = string.IsNullOrWhiteSpace(protectedConnection)
                ? databaseConnectionProvider.ConnectionString
                : dataProtectionProvider.CreateProtector("PCSTORE.API.SystemConfiguration.v1").Unprotect(protectedConnection);
            if (!string.IsNullOrWhiteSpace(currentConnectionString))
            {
                var current = new NpgsqlConnectionStringBuilder(currentConnectionString);
                if (string.Equals(current.Host, request.Host, StringComparison.OrdinalIgnoreCase)
                    && current.Port == request.Port
                    && string.Equals(current.Database, request.Database, StringComparison.Ordinal)
                    && string.Equals(current.Username, request.Username, StringComparison.Ordinal))
                    databaseRequest = request with { Password = current.Password };
            }
        }
        var connectionString = CreateDatabaseConnectionString(databaseRequest);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await using var connection = new NpgsqlConnection(connectionString);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        await connection.OpenAsync(timeout.Token);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(timeout.Token);
        stopwatch.Stop();
        return Results.Ok(new ConfigurationHealthResponse(
            "Connected",
            stopwatch.Elapsed.TotalMilliseconds,
            "Kết nối PostgreSQL thành công."));
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Ok(new ConfigurationHealthResponse("Timeout", null, "Máy chủ database không phản hồi trong 8 giây."));
    }
    catch (NpgsqlException exception)
    {
        httpContext.RequestServices.GetRequiredService<ILogger<Program>>()
            .LogWarning(exception, "Database configuration health check failed.");
        return Results.Ok(new ConfigurationHealthResponse("Disconnected", null, "Không thể kết nối database. Hãy kiểm tra host, port, database và thông tin xác thực."));
    }
})
    .WithSummary("Kiểm tra kết nối PostgreSQL")
    .WithDescription("Mở kết nối ngắn hạn và chạy SELECT 1; không lưu cấu hình. Có thể để trống mật khẩu để tái sử dụng mật khẩu đã lưu cho cùng một endpoint database.")
    .Produces<ConfigurationHealthResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapPost("/api/admin/system-configuration/test-ai", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    IHttpClientFactory httpClientFactory,
    AiConfigurationRequest request,
    CancellationToken cancellationToken) =>
{
    var administrator = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (administrator is null) return Results.Unauthorized();
    if (administrator.Role != UserRole.Admin) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var validationError = ValidateAiConfiguration(request);
    if (validationError is not null) return Results.BadRequest(new { message = validationError });
    var values = await context.SystemSettings.AsNoTracking()
        .Where(setting => setting.Key.StartsWith("system.ai."))
        .ToDictionaryAsync(setting => setting.Key, setting => setting.Value, cancellationToken);
    var apiKey = request.ApiKey?.Trim();
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        if (!values.TryGetValue("system.ai.api-key", out var protectedApiKey)
            || string.IsNullOrWhiteSpace(protectedApiKey))
            return Results.Ok(new ConfigurationHealthResponse("NotConfigured", null, "Nhập API Key để kiểm tra kết nối."));
        apiKey = dataProtectionProvider.CreateProtector("PCSTORE.API.SystemConfiguration.v1").Unprotect(protectedApiKey);
    }
    var model = request.Model.Trim();
    var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}";
    using var message = new HttpRequestMessage(HttpMethod.Get, url);
    message.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(10));
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        using var response = await httpClientFactory.CreateClient().SendAsync(message, timeout.Token);
        stopwatch.Stop();
        if (response.IsSuccessStatusCode)
            return Results.Ok(new ConfigurationHealthResponse("Connected", stopwatch.Elapsed.TotalMilliseconds, "Kết nối API AI thành công."));
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return Results.Ok(new ConfigurationHealthResponse("InvalidApiKey", stopwatch.Elapsed.TotalMilliseconds, "API Key bị từ chối. Hãy kiểm tra lại key đã lưu."));
        if (response.StatusCode == HttpStatusCode.NotFound)
            return Results.Ok(new ConfigurationHealthResponse("ModelNotFound", stopwatch.Elapsed.TotalMilliseconds, "Không tìm thấy model đã chọn."));
        if ((int)response.StatusCode == 429)
            return Results.Ok(new ConfigurationHealthResponse("RateLimited", stopwatch.Elapsed.TotalMilliseconds, "Nhà cung cấp AI đang giới hạn yêu cầu."));
        return Results.Ok(new ConfigurationHealthResponse("ProviderError", stopwatch.Elapsed.TotalMilliseconds, $"Nhà cung cấp AI trả về HTTP {(int)response.StatusCode}."));
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Ok(new ConfigurationHealthResponse("Timeout", null, "API AI không phản hồi trong 10 giây."));
    }
    catch (HttpRequestException exception)
    {
        httpContext.RequestServices.GetRequiredService<ILogger<Program>>()
            .LogWarning(exception, "AI provider health check failed.");
        return Results.Ok(new ConfigurationHealthResponse("Disconnected", null, "Không kết nối được tới nhà cung cấp AI."));
    }
})
    .WithSummary("Kiểm tra kết nối API AI")
    .WithDescription("Kiểm tra model với API Key nhập tạm thời hoặc key đã mã hóa trong cấu hình; không lưu key nhập tạm.")
    .Produces<ConfigurationHealthResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapPut("/api/admin/system-configuration", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    DatabaseConnectionProvider databaseConnectionProvider,
    UpdateSystemConfigurationRequest request,
    CancellationToken cancellationToken) =>
{
    var administrator = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (administrator is null) return Results.Unauthorized();
    if (administrator.Role != UserRole.Admin) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var databaseError = ValidateDatabaseConfiguration(request.Database);
    if (databaseError is not null) return Results.BadRequest(new { message = databaseError });
    var aiError = ValidateAiConfiguration(request.Ai);
    if (aiError is not null) return Results.BadRequest(new { message = aiError });

    var protector = dataProtectionProvider.CreateProtector("PCSTORE.API.SystemConfiguration.v1");
    var settings = await context.SystemSettings
        .Where(setting => setting.Key.StartsWith("system."))
        .ToDictionaryAsync(setting => setting.Key, cancellationToken);
    var previousConnectionString = settings.TryGetValue("system.database.connection", out var savedDatabase)
        ? protector.Unprotect(savedDatabase.Value)
        : databaseConnectionProvider.ConnectionString;
    var databasePassword = request.Database.Password;
    if (string.IsNullOrEmpty(databasePassword) && !string.IsNullOrEmpty(previousConnectionString))
        databasePassword = new NpgsqlConnectionStringBuilder(previousConnectionString).Password;

    var connectionString = CreateDatabaseConnectionString(request.Database with { Password = databasePassword });
    try
    {
        await using var connection = new NpgsqlConnection(connectionString);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        await connection.OpenAsync(timeout.Token);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(timeout.Token);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.BadRequest(new { message = "Không lưu được: kết nối database mới hết thời gian chờ." });
    }
    catch (NpgsqlException exception)
    {
        httpContext.RequestServices.GetRequiredService<ILogger<Program>>()
            .LogWarning(exception, "Database connection configuration was rejected.");
        return Results.BadRequest(new { message = "Không lưu được: không thể kết nối tới database với thông tin đã nhập." });
    }

    var previousAiConfiguration = new
    {
        model = settings.GetValueOrDefault("system.ai.model")?.Value ?? "gemini-3.8-flash",
        temperature = settings.GetValueOrDefault("system.ai.temperature")?.Value ?? "0.7",
        maxTokens = settings.GetValueOrDefault("system.ai.max-tokens")?.Value ?? "1024",
        ragTopK = settings.GetValueOrDefault("system.rag.top-k")?.Value ?? "5",
        ragThreshold = settings.GetValueOrDefault("system.rag.threshold")?.Value ?? "0.65",
    };
    void SetSetting(string key, string value, string description)
    {
        if (settings.TryGetValue(key, out var setting))
        {
            setting.Value = value;
            setting.Description = description;
        }
        else
        {
            setting = new SystemSetting { Key = key, Value = value, Description = description };
            context.SystemSettings.Add(setting);
            settings[key] = setting;
        }
    }

    SetSetting("system.database.connection", protector.Protect(connectionString), "Encrypted PostgreSQL connection string.");
    if (!string.IsNullOrWhiteSpace(request.Ai.ApiKey))
        SetSetting("system.ai.api-key", protector.Protect(request.Ai.ApiKey.Trim()), "Encrypted AI provider API key.");
    SetSetting("system.ai.model", request.Ai.Model.Trim(), "AI provider model.");
    SetSetting("system.ai.temperature", request.Ai.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture), "AI sampling temperature.");
    SetSetting("system.ai.max-tokens", request.Ai.MaxTokens.ToString(System.Globalization.CultureInfo.InvariantCulture), "AI maximum output tokens.");
    SetSetting("system.rag.top-k", request.Ai.RagTopK.ToString(System.Globalization.CultureInfo.InvariantCulture), "RAG retrieval top-K.");
    SetSetting("system.rag.threshold", request.Ai.RagThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture), "RAG relevance threshold.");

    var newDatabase = new NpgsqlConnectionStringBuilder(connectionString);
    var oldDatabase = new NpgsqlConnectionStringBuilder(previousConnectionString);
    var restartRequired = !string.Equals(oldDatabase.ConnectionString, newDatabase.ConnectionString, StringComparison.Ordinal);
    context.AuditLogs.Add(new AuditLog
    {
        Action = "UPDATE_SYSTEM_CONFIGURATION",
        Module = "CONFIG",
        PerformedBy = administrator.Email,
        PerformedByRole = administrator.Role.ToString(),
        IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
        UserAgent = GetRequestUserAgent(httpContext),
        Details = System.Text.Json.JsonSerializer.Serialize(new
        {
            database = new { host = newDatabase.Host, port = newDatabase.Port, database = newDatabase.Database, username = newDatabase.Username, restartRequired },
            ai = new
            {
                before = previousAiConfiguration,
                after = new { model = request.Ai.Model, temperature = request.Ai.Temperature, maxTokens = request.Ai.MaxTokens, ragTopK = request.Ai.RagTopK, ragThreshold = request.Ai.RagThreshold },
                apiKeyReplaced = !string.IsNullOrWhiteSpace(request.Ai.ApiKey),
            },
        }),
        Timestamp = DateTime.UtcNow,
    });
    await context.SaveChangesAsync(cancellationToken);

    return Results.Ok(new
    {
        message = restartRequired
            ? "Đã lưu tham số AI. Thay đổi database đã lưu an toàn và sẽ được áp dụng sau khi khởi động lại API."
            : "Đã lưu cấu hình hệ thống và AI thành công.",
        databaseConnectionChangePendingRestart = restartRequired,
        aiConfigurationAppliedImmediately = true,
    });
})
    .WithSummary("Lưu cấu hình PostgreSQL, AI và RAG")
    .WithDescription("Chỉ Admin được cập nhật. API Key và chuỗi kết nối được mã hóa bằng ASP.NET Core Data Protection, không trả lại giá trị bí mật.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapGet("/api/admin/settings", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    CancellationToken cancellationToken) =>
{
    var administrator = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (administrator is null) return Results.Unauthorized();
    if (administrator.Role != UserRole.Admin) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var settings = await context.SystemSettings.AsNoTracking()
        .Where(setting => !setting.Key.StartsWith("system."))
        .Select(setting => new { setting.Key, setting.Value, setting.Description })
        .ToListAsync(cancellationToken);
    return Results.Ok(settings);
});

app.MapPut("/api/admin/settings", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    List<UpdateSettingDto> dtos,
    CancellationToken cancellationToken) =>
{
    var administrator = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (administrator is null) return Results.Unauthorized();
    if (administrator.Role != UserRole.Admin) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (dtos.Count > 100 || dtos.Any(dto => string.IsNullOrWhiteSpace(dto.Key) || dto.Key.Length > 100 || dto.Value?.Length > 4000))
        return Results.BadRequest(new { message = "Danh sách hoặc giá trị cấu hình không hợp lệ." });
    foreach (var dto in dtos)
    {
        var setting = await context.SystemSettings.FirstOrDefaultAsync(item => item.Key == dto.Key, cancellationToken);
        if (setting is not null && !setting.Key.StartsWith("system.", StringComparison.Ordinal))
            setting.Value = dto.Value ?? "";
    }
    await context.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { message = "Cập nhật cấu hình hệ thống thành công!" });
});

app.MapGet("/api/admin/backups", (DatabaseBackupService backupService) =>
{
    return Results.Ok(new { items = backupService.GetBackups() });
})
    .WithSummary("Liệt kê các bản sao lưu cơ sở dữ liệu")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

app.MapPost("/api/admin/backups", async Task<IResult> (
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    DatabaseBackupService backupService,
    CancellationToken cancellationToken) =>
{
    var administrator = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (administrator is null) return Results.Unauthorized();
    if (administrator.Role != UserRole.Admin) return Results.StatusCode(StatusCodes.Status403Forbidden);

    try
    {
        var backup = await backupService.CreateBackupAsync(cancellationToken);
        var setting = await context.SystemSettings.FirstOrDefaultAsync(item => item.Key == "backup_last", cancellationToken);
        if (setting is not null)
        {
            setting.Value = backup.CreatedAtUtc.ToString("O");
        }
        context.AuditLogs.Add(new AuditLog
        {
            Action = "CREATE_DATABASE_BACKUP",
            Module = "BACKUP",
            PerformedBy = administrator.Email,
            PerformedByRole = administrator.Role.ToString(),
            IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = GetRequestUserAgent(httpContext),
            Details = System.Text.Json.JsonSerializer.Serialize(new { backup.Id, backup.CreatedAtUtc, backup.SizeBytes }),
            Timestamp = DateTime.UtcNow,
        });
        return Results.Ok(new { message = "Đã tạo bản sao lưu đầy đủ.", backup });
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, detail: exception.Message);
    }
})
    .WithSummary("Tạo bản sao lưu đầy đủ cơ sở dữ liệu")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status503ServiceUnavailable);

app.MapPost("/api/admin/backups/{backupId}/restore", async Task<IResult> (
    string backupId,
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    DatabaseBackupService backupService,
    CancellationToken cancellationToken) =>
{
    var administrator = await GetActiveAuthenticatedUserAsync(context, httpContext, dataProtectionProvider, cancellationToken);
    if (administrator is null) return Results.Unauthorized();
    if (administrator.Role != UserRole.Admin) return Results.StatusCode(StatusCodes.Status403Forbidden);

    try
    {
        var (restoredBackup, safetyBackup) = await backupService.RestoreAsync(backupId, cancellationToken);
        context.AuditLogs.Add(new AuditLog
        {
            Action = "RESTORE_DATABASE_BACKUP",
            Module = "BACKUP",
            PerformedBy = administrator.Email,
            PerformedByRole = administrator.Role.ToString(),
            IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = GetRequestUserAgent(httpContext),
            Details = System.Text.Json.JsonSerializer.Serialize(new
            {
                restoredBackup = restoredBackup.Id,
                safetyBackup = safetyBackup.Id,
            }),
            Timestamp = DateTime.UtcNow,
        });
        return Results.Ok(new
        {
            message = "Đã phục hồi dữ liệu. Bản sao lưu an toàn trước phục hồi cũng đã được tạo.",
            restoredBackup,
            safetyBackup,
        });
    }
    catch (FileNotFoundException exception)
    {
        return Results.NotFound(new { message = exception.Message });
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { message = exception.Message });
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, detail: exception.Message);
    }
})
    .WithSummary("Phục hồi cơ sở dữ liệu từ bản sao lưu")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status500InternalServerError);

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

static async Task InitializeCategoryAndBrandCatalogAsync(AppDbContext context)
{
    var categories = await context.Categories
        .OrderBy(category => category.Id)
        .ToListAsync();
    var usedCategorySlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var category in categories)
    {
        var slug = GenerateSeoSlug(category.Name);
        if (!usedCategorySlugs.Add(slug))
        {
            slug = $"{slug}-{category.Id}";
            usedCategorySlugs.Add(slug);
        }
        category.Slug = slug;
    }

    var existingBrandNames = (await context.Brands
        .Select(brand => brand.Name.ToLower())
        .ToListAsync())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var productBrandNames = await context.Products
        .AsNoTracking()
        .Where(product => product.Brand != null && product.Brand.Trim() != "")
        .Select(product => product.Brand!.Trim())
        .Distinct()
        .OrderBy(name => name)
        .ToListAsync();
    var usedBrandSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var brand in await context.Brands.OrderBy(item => item.Id).ToListAsync())
    {
        var baseSlug = GenerateSeoSlug(brand.Name);
        var slug = baseSlug;
        if (!usedBrandSlugs.Add(slug))
        {
            slug = $"{baseSlug}-{brand.Id}";
            usedBrandSlugs.Add(slug);
        }
        brand.Slug = slug;
    }

    var nextBrandDisplayOrder = (await context.Brands.Select(brand => (int?)brand.DisplayOrder).MaxAsync()) ?? 0;
    foreach (var name in productBrandNames)
    {
        if (!existingBrandNames.Add(name))
            continue;

        nextBrandDisplayOrder += 10;
        var slug = GenerateSeoSlug(name);
        if (!usedBrandSlugs.Add(slug))
        {
            var suffix = 2;
            var candidate = $"{slug}-{suffix}";
            while (!usedBrandSlugs.Add(candidate))
            {
                suffix++;
                candidate = $"{slug}-{suffix}";
            }
            slug = candidate;
        }
        context.Brands.Add(new Brand
        {
            Name = name,
            Slug = slug,
            DisplayOrder = nextBrandDisplayOrder,
            IsActive = true
        });
    }

    await context.SaveChangesAsync();
    await context.Database.ExecuteSqlRawAsync(@"
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Categories_Name_Lower"" ON ""Categories"" (LOWER(""Name""));
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Categories_Slug_Lower"" ON ""Categories"" (LOWER(""Slug""));
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Brands_Name_Lower"" ON ""Brands"" (LOWER(""Name""));
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Brands_Slug_Lower"" ON ""Brands"" (LOWER(""Slug""));
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Vouchers_Code_Lower"" ON ""Vouchers"" (LOWER(""Code""));
    ");
    var duplicateProductSkusExist = await context.Products
        .GroupBy(product => product.Sku.ToLower())
        .AnyAsync(group => group.Count() > 1);
    if (duplicateProductSkusExist)
    {
        Console.WriteLine("--> [DATABASE WARNING] SKU bị trùng; bỏ qua unique index để không làm gián đoạn khởi động. API vẫn kiểm tra SKU khi thêm/sửa.");
    }
    else
    {
        await context.Database.ExecuteSqlRawAsync(
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Products_Sku_Lower"" ON ""Products"" (LOWER(""Sku""));");
    }
}

static string GenerateSeoSlug(string value)
{
    var normalized = value.Trim().ToLowerInvariant()
        .Replace('đ', 'd')
        .Normalize(System.Text.NormalizationForm.FormD);
    var slug = new System.Text.StringBuilder(normalized.Length);
    var previousWasSeparator = false;
    foreach (var character in normalized)
    {
        var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character);
        if (category is System.Globalization.UnicodeCategory.NonSpacingMark
            or System.Globalization.UnicodeCategory.SpacingCombiningMark
            or System.Globalization.UnicodeCategory.EnclosingMark)
            continue;

        if (char.IsLetterOrDigit(character))
        {
            slug.Append(character);
            previousWasSeparator = false;
        }
        else if (!previousWasSeparator && slug.Length > 0)
        {
            slug.Append('-');
            previousWasSeparator = true;
        }
    }

    var result = slug.ToString().Trim('-');
    return result.Length > 0 ? result : "item";
}

static async Task<string> CreateUniqueCategorySlugAsync(
    AppDbContext context,
    string name,
    int? excludeId,
    CancellationToken cancellationToken)
{
    var baseSlug = GenerateSeoSlug(name);
    var slug = baseSlug;
    var suffix = 2;
    while (await context.Categories.AnyAsync(
        category => category.Id != excludeId && category.Slug.ToLower() == slug.ToLower(),
        cancellationToken))
    {
        slug = $"{baseSlug}-{suffix++}";
    }
    return slug;
}

static async Task<string> CreateUniqueBrandSlugAsync(
    AppDbContext context,
    string name,
    int? excludeId,
    CancellationToken cancellationToken)
{
    var baseSlug = GenerateSeoSlug(name);
    var slug = baseSlug;
    var suffix = 2;
    while (await context.Brands.AnyAsync(
        brand => brand.Id != excludeId && brand.Slug.ToLower() == slug.ToLower(),
        cancellationToken))
    {
        slug = $"{baseSlug}-{suffix++}";
    }
    return slug;
}

static AuditLog CreateCatalogAuditLog(
    HttpContext httpContext,
    User performedBy,
    string action,
    string details) => new()
{
    Action = action,
    Module = "CATALOG",
    PerformedBy = performedBy.Email,
    IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
    Details = details,
    Timestamp = DateTime.UtcNow
};

static AuditLog CreateInventoryAuditLog(
    HttpContext httpContext,
    User performedBy,
    string action,
    string details) => new()
{
    Action = action,
    Module = "INVENTORY",
    PerformedBy = performedBy.Email,
    IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
    Details = details,
    Timestamp = DateTime.UtcNow
};

static async Task<int> GetHeldInventoryQuantityAsync(
    AppDbContext context,
    int productId,
    CancellationToken cancellationToken)
{
    return await context.OrderDetails
        .AsNoTracking()
        .Where(detail => detail.Order != null
            && detail.ProductId == productId
            && detail.Order.InventoryReserved
            && (detail.Order.Status == OrderStatus.Confirmed || detail.Order.Status == OrderStatus.Shipping))
        .SumAsync(detail => (int?)detail.Quantity, cancellationToken)
        ?? 0;
}

static async Task NotifyInventoryChangedAsync(
    IHubContext<OrderNotificationsHub> notifications,
    ILogger<Program> logger,
    int productId,
    CancellationToken cancellationToken)
{
    try
    {
        await notifications.Clients.All.SendAsync("InventoryUpdated", new
        {
            productId,
            receivedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "Inventory for product {ProductId} was saved but realtime notifications could not be delivered.", productId);
    }
}

static string CreateProductAuditDetails(Product? existingProduct, CreateProductRequest request, string categoryName)
{
    var before = existingProduct is null ? null : new
    {
        existingProduct.Name,
        existingProduct.Sku,
        existingProduct.Brand,
        existingProduct.Description,
        existingProduct.Price,
        existingProduct.PromotionalPrice,
        existingProduct.PromotionStartsAt,
        existingProduct.PromotionEndsAt,
        existingProduct.StockQuantity,
        existingProduct.CategoryId,
        hasImage = existingProduct.ImageUrl is not null,
        socket = existingProduct.TechnicalSpec?.Socket,
        chipset = existingProduct.TechnicalSpec?.Chipset,
        formFactor = existingProduct.TechnicalSpec?.FormFactor,
        ramType = existingProduct.TechnicalSpec?.RamType,
        ramBusSpeed = existingProduct.TechnicalSpec?.RamBusSpeed,
        ramSlots = existingProduct.TechnicalSpec?.RamSlots,
        tdpWattage = existingProduct.TechnicalSpec?.TdpWattage,
        recommendedPsu = existingProduct.TechnicalSpec?.RecommendedPsu,
        lengthMm = existingProduct.TechnicalSpec?.LengthMm,
        additionalSpecsJson = existingProduct.TechnicalSpec?.AdditionalSpecsJson
    };
    var after = new
    {
        request.Name,
        request.Sku,
        request.Brand,
        request.Description,
        request.Price,
        request.PromotionalPrice,
        request.PromotionStartsAt,
        request.PromotionEndsAt,
        request.StockQuantity,
        request.CategoryId,
        categoryName,
        hasImage = !string.IsNullOrWhiteSpace(request.ImageUrl),
        request.Socket,
        request.Chipset,
        request.FormFactor,
        request.RamType,
        request.RamBusSpeed,
        request.RamSlots,
        request.TdpWattage,
        request.RecommendedPsu,
        request.LengthMm,
        request.AdditionalSpecs
    };

    return System.Text.Json.JsonSerializer.Serialize(new { before, after });
}

static bool IsValidOptionalHttpsUrl(string? value) =>
    string.IsNullOrWhiteSpace(value)
    || (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http");

static async Task<string?> ValidateProductInputAsync(
    AppDbContext context,
    CreateProductRequest request,
    int? excludeProductId,
    CancellationToken cancellationToken)
{
    var name = request.Name?.Trim() ?? string.Empty;
    var sku = request.Sku?.Trim() ?? string.Empty;
    if (name.Length is < 1 or > 250)
        return "Tên sản phẩm bắt buộc và không vượt quá 250 ký tự.";
    if (sku.Length is < 1 or > 100)
        return "SKU bắt buộc và không vượt quá 100 ký tự.";
    if (request.Price <= 0 || request.Price > 1_000_000_000)
        return "Giá sản phẩm phải lớn hơn 0 và không vượt quá 1 tỷ đồng.";
    if (request.PromotionalPrice.HasValue
        && (request.PromotionalPrice.Value <= 0 || request.PromotionalPrice.Value > request.Price))
        return "Giá khuyến mãi phải lớn hơn 0 và không được vượt quá giá niêm yết.";
    if (request.PromotionalPrice.HasValue
        && (!request.PromotionStartsAt.HasValue || !request.PromotionEndsAt.HasValue))
        return "Vui lòng nhập thời gian bắt đầu và kết thúc chương trình giá khuyến mãi.";
    if (request.PromotionStartsAt.HasValue != request.PromotionEndsAt.HasValue)
        return "Thời gian bắt đầu và kết thúc khuyến mãi phải được nhập cùng nhau.";
    if (request.PromotionStartsAt.HasValue
        && request.PromotionEndsAt <= request.PromotionStartsAt)
        return "Thời gian kết thúc khuyến mãi phải sau thời gian bắt đầu.";
    if (request.StockQuantity < 0)
        return "Tồn kho không được là số âm.";
    if (request.CategoryId <= 0
        || !await context.Categories.AnyAsync(
            category => category.Id == request.CategoryId && category.IsActive,
            cancellationToken))
        return "Vui lòng chọn danh mục đang hoạt động.";
    if (request.Description?.Length > 2000)
        return "Mô tả sản phẩm không được vượt quá 2.000 ký tự.";
    if (request.Brand?.Trim().Length > 100)
        return "Thương hiệu không được vượt quá 100 ký tự.";
    if (await context.Products.AnyAsync(
        product => product.Id != excludeProductId && product.Sku.ToLower() == sku.ToLower(),
        cancellationToken))
        return "SKU này đã được sử dụng cho sản phẩm khác.";
    if (!string.IsNullOrWhiteSpace(request.Brand)
        && !await context.Brands.AnyAsync(
            brand => brand.Name.ToLower() == request.Brand.Trim().ToLower(),
            cancellationToken))
        return "Thương hiệu chưa được khai báo trong danh mục thương hiệu.";
    if (request.Socket?.Length > 100
        || request.Chipset?.Length > 100
        || request.FormFactor?.Length > 100
        || request.RamType?.Length > 100)
        return "Một trong các thông số kỹ thuật vượt quá 100 ký tự.";
    if (request.RamBusSpeed < 0 || request.RamSlots < 0 || request.TdpWattage < 0
        || request.RecommendedPsu < 0 || request.LengthMm < 0)
        return "Thông số kỹ thuật dạng số không được là số âm.";
    if (request.AdditionalSpecs is { Count: > 30 }
        || request.AdditionalSpecs?.Any(pair =>
            string.IsNullOrWhiteSpace(pair.Key)
            || pair.Key.Trim().Length > 80
            || pair.Value?.Length > 500) == true)
        return "Tối đa 30 thông số bổ sung; tên tối đa 80 ký tự và giá trị tối đa 500 ký tự.";
    if (request.AdditionalSpecs is { Count: > 0 })
    {
        try
        {
            if (System.Text.Json.JsonSerializer.Serialize(request.AdditionalSpecs).Length > 10_000)
                return "Tổng dữ liệu thông số bổ sung không được vượt quá 10 KB.";
        }
        catch (System.Text.Json.JsonException)
        {
            return "Thông số bổ sung không đúng định dạng.";
        }
    }

    if (!string.IsNullOrWhiteSpace(request.ImageUrl))
    {
        const string dataPrefix = "data:image/";
        if (request.ImageUrl.StartsWith(dataPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var separator = request.ImageUrl.IndexOf(',');
            var header = separator > 0 ? request.ImageUrl[..separator] : string.Empty;
            if (!(header.Equals("data:image/jpeg;base64", StringComparison.OrdinalIgnoreCase)
                || header.Equals("data:image/png;base64", StringComparison.OrdinalIgnoreCase)
                || header.Equals("data:image/webp;base64", StringComparison.OrdinalIgnoreCase)))
                return "Ảnh chỉ hỗ trợ định dạng JPG, PNG hoặc WEBP.";
            if (separator < 0)
                return "Dữ liệu ảnh không hợp lệ.";
            var base64Image = request.ImageUrl[(separator + 1)..];
            if (base64Image.Length > 4 * ((5 * 1024 * 1024 + 2) / 3))
                return "Kích thước ảnh không được vượt quá 5 MB.";
            try
            {
                var imageBytes = Convert.FromBase64String(base64Image);
                if (imageBytes.Length is 0 or > 5 * 1024 * 1024)
                    return "Kích thước ảnh phải lớn hơn 0 và tối đa 5 MB.";
                var isValidImage = header.ToLowerInvariant() switch
                {
                    "data:image/jpeg;base64" => imageBytes.Length >= 3
                        && imageBytes[0] == 0xFF && imageBytes[1] == 0xD8 && imageBytes[2] == 0xFF,
                    "data:image/png;base64" => imageBytes.Length >= 8
                        && imageBytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
                    "data:image/webp;base64" => imageBytes.Length >= 12
                        && imageBytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
                        && imageBytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
                    _ => false
                };
                if (!isValidImage)
                    return "Nội dung tệp không khớp với định dạng ảnh JPG, PNG hoặc WEBP đã chọn.";
            }
            catch (FormatException)
            {
                return "Dữ liệu ảnh không đúng định dạng Base64.";
            }
        }
        else if (request.ImageUrl.Length > 2048
            || !Uri.TryCreate(request.ImageUrl.Trim(), UriKind.Absolute, out var imageUri)
            || imageUri.Scheme is not ("http" or "https"))
        {
            return "Ảnh phải là JPG, PNG, WEBP tối đa 5 MB hoặc URL HTTP/HTTPS hợp lệ.";
        }
    }

    var categoryType = await context.Categories
        .Where(category => category.Id == request.CategoryId)
        .Select(category => category.ComponentType)
        .FirstAsync(cancellationToken);
    var missingRequiredSpec = categoryType switch
    {
        ComponentType.CPU => string.IsNullOrWhiteSpace(request.Socket),
        ComponentType.Mainboard => string.IsNullOrWhiteSpace(request.Socket)
            || string.IsNullOrWhiteSpace(request.Chipset)
            || string.IsNullOrWhiteSpace(request.FormFactor)
            || string.IsNullOrWhiteSpace(request.RamType),
        ComponentType.RAM => string.IsNullOrWhiteSpace(request.RamType),
        ComponentType.Cooler => string.IsNullOrWhiteSpace(request.Socket),
        ComponentType.Case => string.IsNullOrWhiteSpace(request.FormFactor),
        _ => false
    };
    return missingRequiredSpec
        ? $"Vui lòng nhập đủ thông số bắt buộc cho danh mục {categoryType}."
        : null;
}

static async Task<string?> ValidateVoucherRequestAsync(
    AppDbContext context,
    CreateVoucherRequest request,
    int? excludeVoucherId,
    CancellationToken cancellationToken)
{
    var code = request.Code?.Trim() ?? string.Empty;
    if (code.Length is < 3 or > 50
        || !code.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
        return "Mã voucher phải dài từ 3 đến 50 ký tự và chỉ gồm chữ Latin, số hoặc dấu gạch ngang.";

    var discountType = request.DiscountType?.Trim();
    if (!string.Equals(discountType, "Percentage", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(discountType, "FixedAmount", StringComparison.OrdinalIgnoreCase))
        return "Loại giảm giá chỉ được là Percentage hoặc FixedAmount.";
    if (request.DiscountValue <= 0
        || (string.Equals(discountType, "Percentage", StringComparison.OrdinalIgnoreCase) && request.DiscountValue > 100))
        return "Phần trăm giảm phải từ 0 đến 100; số tiền giảm phải lớn hơn 0.";
    if (request.MinimumOrderAmount < 0)
        return "Giá trị đơn hàng tối thiểu không được âm.";
    if (request.MaximumDiscountAmount.HasValue && request.MaximumDiscountAmount <= 0)
        return "Mức giảm tối đa phải lớn hơn 0.";
    if (string.Equals(discountType, "FixedAmount", StringComparison.OrdinalIgnoreCase)
        && request.MaximumDiscountAmount.HasValue)
        return "Chỉ voucher giảm theo phần trăm mới có thể đặt mức giảm tối đa.";
    if (request.UsageLimit <= 0)
        return "Số lượt sử dụng tối đa phải lớn hơn 0.";
    if (request.StartsAt >= request.EndsAt)
        return "Thời gian kết thúc phải sau thời gian bắt đầu.";

    if (excludeVoucherId.HasValue)
    {
        var currentUsageCount = await context.Vouchers
            .Where(voucher => voucher.Id == excludeVoucherId.Value)
            .Select(voucher => (int?)voucher.UsageCount)
            .FirstOrDefaultAsync(cancellationToken);
        if (currentUsageCount.HasValue && request.UsageLimit < currentUsageCount.Value)
            return $"Giới hạn lượt không thể thấp hơn số lượt đã sử dụng ({currentUsageCount.Value}).";
    }
    return null;
}

static decimal CalculateVoucherDiscount(Voucher voucher, decimal subtotal)
{
    var discount = string.Equals(voucher.DiscountType, "Percentage", StringComparison.OrdinalIgnoreCase)
        ? subtotal * voucher.DiscountValue / 100m
        : voucher.DiscountValue;
    if (voucher.MaximumDiscountAmount.HasValue)
        discount = Math.Min(discount, voucher.MaximumDiscountAmount.Value);
    return Math.Min(Math.Max(0m, discount), subtotal);
}

static string NormalizeVoucherDiscountType(string? value) =>
    string.Equals(value?.Trim(), "Percentage", StringComparison.OrdinalIgnoreCase)
        ? "Percentage"
        : "FixedAmount";

static string? GetVoucherEligibilityError(Voucher voucher, decimal subtotal, DateTimeOffset now)
{
    if (!voucher.IsActive)
        return "Voucher đã ngừng hoạt động.";
    if (now < voucher.StartsAt)
        return $"Voucher sẽ bắt đầu có hiệu lực lúc {voucher.StartsAt:dd/MM/yyyy HH:mm zzz}.";
    if (now >= voucher.EndsAt)
        return "Voucher đã hết hạn.";
    if (voucher.UsageCount >= voucher.UsageLimit)
        return "Voucher đã hết lượt sử dụng.";
    if (subtotal < voucher.MinimumOrderAmount)
        return $"Đơn hàng cần đạt tối thiểu {voucher.MinimumOrderAmount:N0} đ để sử dụng voucher.";
    if (CalculateVoucherDiscount(voucher, subtotal) <= 0)
        return "Voucher không tạo ra khoản giảm giá hợp lệ cho đơn hàng này.";
    return null;
}

static TechnicalSpec CreateTechnicalSpec(CreateProductRequest request) => new()
{
    Socket = NullIfBlank(request.Socket),
    Chipset = NullIfBlank(request.Chipset),
    FormFactor = NullIfBlank(request.FormFactor),
    RamType = NullIfBlank(request.RamType),
    RamBusSpeed = request.RamBusSpeed,
    RamSlots = request.RamSlots,
    TdpWattage = request.TdpWattage,
    RecommendedPsu = request.RecommendedPsu,
    LengthMm = request.LengthMm,
    AdditionalSpecsJson = SerializeAdditionalSpecs(request.AdditionalSpecs)
};

static TechnicalSpec UpdateTechnicalSpec(TechnicalSpec? existing, CreateProductRequest request)
{
    var technicalSpec = existing ?? new TechnicalSpec();
    technicalSpec.Socket = NullIfBlank(request.Socket);
    technicalSpec.Chipset = NullIfBlank(request.Chipset);
    technicalSpec.FormFactor = NullIfBlank(request.FormFactor);
    technicalSpec.RamType = NullIfBlank(request.RamType);
    technicalSpec.RamBusSpeed = request.RamBusSpeed;
    technicalSpec.RamSlots = request.RamSlots;
    technicalSpec.TdpWattage = request.TdpWattage;
    technicalSpec.RecommendedPsu = request.RecommendedPsu;
    technicalSpec.LengthMm = request.LengthMm;
    technicalSpec.AdditionalSpecsJson = SerializeAdditionalSpecs(request.AdditionalSpecs);
    return technicalSpec;
}

static string? NullIfBlank(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : value.Trim();

static bool IsPromotionActive(
    decimal? promotionalPrice,
    DateTimeOffset? startsAt,
    DateTimeOffset? endsAt,
    DateTimeOffset now) =>
    promotionalPrice.HasValue
    && startsAt.HasValue
    && endsAt.HasValue
    && startsAt.Value <= now
    && now < endsAt.Value;

static decimal GetEffectiveProductPrice(Product product, DateTimeOffset now) =>
    IsPromotionActive(product.PromotionalPrice, product.PromotionStartsAt, product.PromotionEndsAt, now)
        ? product.PromotionalPrice!.Value
        : product.Price;

static string? SerializeAdditionalSpecs(Dictionary<string, string>? specs) =>
    specs is { Count: > 0 }
        ? System.Text.Json.JsonSerializer.Serialize(specs)
        : null;

static List<PcCompatibilityIssue> CheckPcBuildCompatibility(
    IReadOnlyDictionary<ComponentType, Product> parts)
{
    parts.TryGetValue(ComponentType.CPU, out var cpu);
    parts.TryGetValue(ComponentType.Mainboard, out var mainboard);
    parts.TryGetValue(ComponentType.RAM, out var ram);
    parts.TryGetValue(ComponentType.GPU, out var gpu);
    parts.TryGetValue(ComponentType.PSU, out var psu);
    parts.TryGetValue(ComponentType.Case, out var pcCase);
    parts.TryGetValue(ComponentType.Cooler, out var cooler);

    var issues = new List<PcCompatibilityIssue>();
    if (!string.IsNullOrWhiteSpace(cpu?.TechnicalSpec?.Socket)
        && !string.IsNullOrWhiteSpace(mainboard?.TechnicalSpec?.Socket)
        && !string.Equals(
            cpu.TechnicalSpec.Socket.Trim(),
            mainboard.TechnicalSpec.Socket.Trim(),
            StringComparison.OrdinalIgnoreCase))
    {
        issues.Add(new(
            "critical",
            "socket",
            $"Socket CPU ({cpu.TechnicalSpec.Socket}) không khớp với mainboard ({mainboard.TechnicalSpec.Socket}).",
            [ComponentType.CPU, ComponentType.Mainboard]));
    }

    if (!string.IsNullOrWhiteSpace(mainboard?.TechnicalSpec?.RamType)
        && !string.IsNullOrWhiteSpace(ram?.TechnicalSpec?.RamType))
    {
        var supportedRamTypes = mainboard.TechnicalSpec.RamType
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!supportedRamTypes.Contains(ram.TechnicalSpec.RamType.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(new(
                "critical",
                "ram-type",
                $"Mainboard hỗ trợ RAM {mainboard.TechnicalSpec.RamType}, nhưng RAM đã chọn là {ram.TechnicalSpec.RamType}.",
                [ComponentType.Mainboard, ComponentType.RAM]));
        }
    }

    var estimatedPower = (cpu?.TechnicalSpec?.TdpWattage ?? 0)
        + (gpu?.TechnicalSpec?.TdpWattage ?? 0)
        + (ram?.TechnicalSpec?.TdpWattage ?? 10)
        + 50;
    var psuCapacity = psu?.TechnicalSpec?.TdpWattage
        ?? psu?.TechnicalSpec?.RecommendedPsu
        ?? 0;
    if (psuCapacity > 0 && estimatedPower > psuCapacity)
    {
        issues.Add(new(
            "critical",
            "power",
            $"Công suất ước tính ({estimatedPower}W) vượt khả năng nguồn ({psuCapacity}W).",
            [ComponentType.PSU, ComponentType.CPU, ComponentType.GPU]));
    }

    if (mainboard is not null && pcCase is not null
        && !string.IsNullOrWhiteSpace(mainboard.TechnicalSpec?.FormFactor)
        && !string.IsNullOrWhiteSpace(pcCase.TechnicalSpec?.FormFactor)
        && !CaseSupportsMainboard(pcCase, mainboard))
    {
        issues.Add(new(
            "critical",
            "case-form-factor",
            $"Kích thước mainboard ({mainboard.TechnicalSpec.FormFactor}) có thể không vừa case ({pcCase.TechnicalSpec.FormFactor}).",
            [ComponentType.Mainboard, ComponentType.Case]));
    }

    var maxGpuLength = ReadAdditionalSpecInt(pcCase, "maxGpuLengthMm");
    if (gpu?.TechnicalSpec?.LengthMm is int gpuLength
        && maxGpuLength is int gpuLimit
        && gpuLength > gpuLimit)
    {
        issues.Add(new(
            "critical",
            "gpu-length",
            $"Card đồ họa dài {gpuLength}mm, vượt giới hạn case {gpuLimit}mm.",
            [ComponentType.GPU, ComponentType.Case]));
    }

    var coolerSocketSupport = cooler?.TechnicalSpec?.Socket;
    if (!string.IsNullOrWhiteSpace(cpu?.TechnicalSpec?.Socket)
        && !string.IsNullOrWhiteSpace(coolerSocketSupport)
        && !coolerSocketSupport.Split('/', ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(cpu.TechnicalSpec.Socket.Trim(), StringComparer.OrdinalIgnoreCase))
    {
        issues.Add(new(
            "critical",
            "cooler-socket",
            $"Tản nhiệt không khai báo hỗ trợ socket {cpu.TechnicalSpec.Socket} của CPU.",
            [ComponentType.CPU, ComponentType.Cooler]));
    }

    var coolerHeight = ReadAdditionalSpecInt(cooler, "heightMm");
    var maxCoolerHeight = ReadAdditionalSpecInt(pcCase, "maxCpuCoolerHeightMm");
    if (coolerHeight is int coolerSize
        && maxCoolerHeight is int coolerLimit
        && coolerSize > coolerLimit)
    {
        issues.Add(new(
            "critical",
            "cooler-height",
            $"Tản nhiệt cao {coolerSize}mm, vượt giới hạn case {coolerLimit}mm.",
            [ComponentType.Cooler, ComponentType.Case]));
    }

    return issues;
}

static bool CaseSupportsMainboard(Product pcCase, Product mainboard)
{
    var caseForm = pcCase.TechnicalSpec?.FormFactor?.ToLowerInvariant() ?? string.Empty;
    var boardForm = mainboard.TechnicalSpec?.FormFactor?.ToLowerInvariant() ?? string.Empty;
    if (caseForm.Length == 0 || boardForm.Length == 0)
        return true;

    if (caseForm.Contains("tower"))
    {
        if (caseForm.Contains("mini") && !caseForm.Contains("mid"))
            return boardForm.Contains("itx");
        if (caseForm.Contains("micro"))
            return boardForm.Contains("itx") || boardForm.Contains("micro") || boardForm.Contains("matx");
        if (caseForm.Contains("mid"))
            return boardForm.Contains("itx") || boardForm.Contains("micro") || boardForm.Contains("matx") || boardForm == "atx";
        if (caseForm.Contains("full") || caseForm.Contains("super"))
            return true;
    }

    if (caseForm.Contains("eatx"))
        return boardForm.Contains("eatx") || boardForm == "atx" || boardForm.Contains("micro") || boardForm.Contains("itx");
    if (caseForm.Contains("atx"))
        return boardForm == "atx" || boardForm.Contains("micro") || boardForm.Contains("itx");
    if (caseForm.Contains("micro") || caseForm.Contains("matx"))
        return boardForm.Contains("micro") || boardForm.Contains("matx") || boardForm.Contains("itx");
    if (caseForm.Contains("itx"))
        return boardForm.Contains("itx");

    return true;
}

static int? ReadAdditionalSpecInt(Product? product, string propertyName)
{
    var json = product?.TechnicalSpec?.AdditionalSpecsJson;
    if (string.IsNullOrWhiteSpace(json))
        return null;

    try
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(propertyName, out var property)
            && property.TryGetInt32(out var value)
                ? value
                : null;
    }
    catch (System.Text.Json.JsonException)
    {
        return null;
    }
}

static string? ValidateDatabaseConfiguration(DatabaseConfigurationRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Host) || request.Host.Length > 255)
        return "Host database là bắt buộc và tối đa 255 ký tự.";
    if (request.Port is < 1 or > 65535)
        return "Port database phải nằm trong khoảng 1 đến 65535.";
    if (string.IsNullOrWhiteSpace(request.Database) || request.Database.Length > 63)
        return "Tên database là bắt buộc và tối đa 63 ký tự.";
    if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 128)
        return "Username database là bắt buộc và tối đa 128 ký tự.";
    if (request.Password?.Length > 1024)
        return "Mật khẩu database vượt quá độ dài cho phép.";
    try
    {
        _ = CreateDatabaseConnectionString(request);
        return null;
    }
    catch (ArgumentException)
    {
        return "Thông tin kết nối database không hợp lệ.";
    }
}

static string? ValidateAiConfiguration(AiConfigurationRequest request)
{
    if (request.ApiKey?.Length > 2048)
        return "API Key vượt quá độ dài cho phép.";
    if (string.IsNullOrWhiteSpace(request.Model)
        || request.Model.Length > 100
        || !request.Model.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
        return "Tên model chỉ được chứa chữ, số, dấu chấm, gạch ngang hoặc gạch dưới.";
    if (request.Temperature is < 0 or > 2)
        return "Temperature phải nằm trong khoảng 0 đến 2.";
    if (request.MaxTokens is < 1 or > 8192)
        return "Giới hạn token phải nằm trong khoảng 1 đến 8192.";
    if (request.RagTopK is < 1 or > 100)
        return "RAG Top-K phải nằm trong khoảng 1 đến 100.";
    if (request.RagThreshold is < 0 or > 1)
        return "RAG Threshold phải nằm trong khoảng 0 đến 1.";
    return null;
}

static string CreateDatabaseConnectionString(DatabaseConfigurationRequest request)
{
    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = request.Host.Trim(),
        Port = request.Port,
        Database = request.Database.Trim(),
        Username = request.Username.Trim(),
        Password = request.Password ?? string.Empty,
        Timeout = 8,
        CommandTimeout = 8,
        ApplicationName = "PCSTORE",
    };
    return builder.ConnectionString;
}

static string? GetRequestUserAgent(HttpContext httpContext)
{
    var userAgent = httpContext.Request.Headers.UserAgent.ToString();
    return userAgent.Length <= 1000 ? userAgent : userAgent[..1000];
}

static IQueryable<AuditLog> FilterAuditLogs(
    AppDbContext context,
    DateTimeOffset? fromDate,
    DateTimeOffset? toDate,
    string? user,
    string? action,
    string? status)
{
    var query = context.AuditLogs.AsNoTracking();
    if (fromDate.HasValue)
    {
        var start = fromDate.Value.UtcDateTime;
        query = query.Where(log => log.Timestamp >= start);
    }
    if (toDate.HasValue)
    {
        var end = toDate.Value.UtcDateTime;
        query = query.Where(log => log.Timestamp <= end);
    }
    if (!string.IsNullOrWhiteSpace(user))
    {
        var normalizedUser = user.Trim().ToLowerInvariant();
        query = query.Where(log => log.PerformedBy != null && log.PerformedBy.ToLower().Contains(normalizedUser));
    }
    if (!string.IsNullOrWhiteSpace(action))
    {
        var normalizedAction = action.Trim().ToLowerInvariant();
        query = query.Where(log =>
            (log.Action != null && log.Action.ToLower().Contains(normalizedAction))
            || (log.Module != null && log.Module.ToLower().Contains(normalizedAction)));
    }
    if (!string.IsNullOrWhiteSpace(status))
    {
        var normalizedStatus = status.Trim();
        query = query.Where(log => log.Status.ToLower() == normalizedStatus.ToLower());
    }
    return query;
}

static (string Module, string Action)? GetRolePermissionRequirement(string path, string method)
{
    var normalizedPath = path.TrimEnd('/');
    if (normalizedPath.StartsWith("/api/admin/audit-logs", StringComparison.OrdinalIgnoreCase))
        return ("auditlogs", "view");
    if (normalizedPath.StartsWith("/api/admin/role-permissions", StringComparison.OrdinalIgnoreCase))
        return ( "permissions", method.Equals("GET", StringComparison.OrdinalIgnoreCase) ? "view" : "update" );

    if (normalizedPath.StartsWith("/api/admin/order-requests", StringComparison.OrdinalIgnoreCase))
        return ("orders", normalizedPath.EndsWith("/review", StringComparison.OrdinalIgnoreCase) ? "approve" : "view");

    if (normalizedPath.StartsWith("/api/admin/accounts", StringComparison.OrdinalIgnoreCase))
        return ("accounts", method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            ? "view"
            : normalizedPath.Contains("/reset-password", StringComparison.OrdinalIgnoreCase) ? "update" : "create");

    if (normalizedPath.StartsWith("/api/admin/users", StringComparison.OrdinalIgnoreCase))
        return normalizedPath.EndsWith("/role", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.EndsWith("/toggle-active", StringComparison.OrdinalIgnoreCase)
                ? ("accounts", "update")
                : ("employees", "view");

    if (normalizedPath.StartsWith("/api/admin/stats", StringComparison.OrdinalIgnoreCase))
        return ("reports", "view");
    if (normalizedPath.StartsWith("/api/admin/reports/export", StringComparison.OrdinalIgnoreCase))
        return ("reports", "create");
    if (normalizedPath.StartsWith("/api/admin/inventory", StringComparison.OrdinalIgnoreCase))
        return ("inventory", method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            ? "view"
            : normalizedPath.EndsWith("/adjust", StringComparison.OrdinalIgnoreCase)
                || normalizedPath.EndsWith("/threshold", StringComparison.OrdinalIgnoreCase) ? "update" : "create");
    if (normalizedPath.StartsWith("/api/admin/categories", StringComparison.OrdinalIgnoreCase)
        || normalizedPath.StartsWith("/api/admin/brands", StringComparison.OrdinalIgnoreCase)
        || normalizedPath.StartsWith("/api/admin/products", StringComparison.OrdinalIgnoreCase))
        return ("catalog", GetCrudAction(normalizedPath, method));
    if (normalizedPath.StartsWith("/api/admin/vouchers", StringComparison.OrdinalIgnoreCase))
        return ("promotions", GetCrudAction(normalizedPath, method));
    if (normalizedPath.StartsWith("/api/admin/settings", StringComparison.OrdinalIgnoreCase))
        return ("settings", method.Equals("GET", StringComparison.OrdinalIgnoreCase) ? "view" : "update");
        if (normalizedPath.StartsWith("/api/admin/system-configuration", StringComparison.OrdinalIgnoreCase))
            return ("settings", method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                ? "view"
                : normalizedPath.EndsWith("/test-database", StringComparison.OrdinalIgnoreCase)
                    || normalizedPath.EndsWith("/test-ai", StringComparison.OrdinalIgnoreCase) ? "view" : "update");
    if (normalizedPath.StartsWith("/api/admin/backups", StringComparison.OrdinalIgnoreCase))
    {
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase))
            return ("backup", "view");
        if (normalizedPath.EndsWith("/restore", StringComparison.OrdinalIgnoreCase))
            return ("backup", "approve");
        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            return ("backup", "create");
    }

    if (normalizedPath.Equals("/api/orders", StringComparison.OrdinalIgnoreCase)
        && method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        return ("orders", "view");
    if (normalizedPath.StartsWith("/api/orders/", StringComparison.OrdinalIgnoreCase)
        && (normalizedPath.EndsWith("/confirm", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.EndsWith("/status", StringComparison.OrdinalIgnoreCase)))
        return ("orders", "update");
    if (normalizedPath.StartsWith("/api/staff/customers", StringComparison.OrdinalIgnoreCase))
        return ("customers", "view");
    if (normalizedPath.Equals("/api/staff/compatibility-check", StringComparison.OrdinalIgnoreCase))
        return ("pcbuilder", "create");

    var orderSegments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
    if (orderSegments.Length == 3
        && orderSegments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
        && orderSegments[1].Equals("orders", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(orderSegments[2], out _)
        && method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        return ("orders", "view");
    if (normalizedPath.EndsWith("/documents/print", StringComparison.OrdinalIgnoreCase)
        && method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        return ("orders", "view");

    return null;
}

static string GetCrudAction(string path, string method)
{
    if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)) return "view";
    if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)) return "create";
    if (method.Equals("DELETE", StringComparison.OrdinalIgnoreCase)) return "delete";
    if (path.EndsWith("/active", StringComparison.OrdinalIgnoreCase)) return "update";
    return "update";
}

static bool IsProtectedAdminEmail(string email) =>
    string.Equals(email, "supportadmin@pcstore.com", StringComparison.OrdinalIgnoreCase)
    || string.Equals(email, "admin@pcstore.vn", StringComparison.OrdinalIgnoreCase);

static bool IsProtectedAdminAccount(string email) => IsProtectedAdminEmail(email);

static async Task InitializeRolePermissionsAsync(AppDbContext context)
{
    var existing = await context.RolePermissions
        .Select(permission => new { permission.Role, permission.Module, permission.Action })
        .ToListAsync();
    var existingKeys = existing
        .Select(permission => $"{permission.Role}:{permission.Module}:{permission.Action}")
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var role in Enum.GetValues<UserRole>())
    {
        foreach (var module in RolePermissionCatalog.Modules)
        {
            foreach (var action in RolePermissionCatalog.Actions)
            {
                var key = $"{role}:{module.Key}:{action.Key}";
                if (!existingKeys.Add(key)) continue;
                context.RolePermissions.Add(new RolePermission
                {
                    Role = role,
                    Module = module.Key,
                    Action = action.Key,
                    IsAllowed = RolePermissionCatalog.IsAllowedByDefault(role, module.Key, action.Key),
                });
            }
        }
    }

    await context.SaveChangesAsync();
}

static async Task<User?> GetActiveAuthenticatedUserAsync(
    AppDbContext context,
    HttpContext httpContext,
    IDataProtectionProvider dataProtectionProvider,
    CancellationToken cancellationToken)
{
    var authorization = httpContext.Request.Headers.Authorization.ToString();
    if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return null;

    var token = authorization["Bearer ".Length..].Trim();
    if (string.IsNullOrWhiteSpace(token))
        return null;

    string protectedPayload;
    try
    {
        protectedPayload = dataProtectionProvider
            .CreateProtector("PCSTORE.API.AccessToken.v1")
            .Unprotect(token);
    }
    catch (CryptographicException)
    {
        return null;
    }

    var tokenParts = protectedPayload.Split('|', 2);
    if (tokenParts.Length != 2
        || !int.TryParse(tokenParts[0], out var userId)
        || !long.TryParse(tokenParts[1], out var expiresAt)
        || expiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        return null;

    return await context.Users
        .AsNoTracking()
        .FirstOrDefaultAsync(user => user.Id == userId && user.IsActive, cancellationToken);
}

static string GenerateTemporaryAccountPassword()
{
    var bytes = RandomNumberGenerator.GetBytes(18);
    return Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}

static async Task<bool> SendAccountCredentialsEmailAsync(
    IConfiguration configuration,
    ILogger logger,
    string recipient,
    string fullName,
    string temporaryPassword,
    bool isPasswordReset,
    CancellationToken cancellationToken)
{
    var host = configuration["Email:SmtpHost"];
    var fromAddress = configuration["Email:FromAddress"];
    if (string.IsNullOrWhiteSpace(host)
        || !int.TryParse(configuration["Email:SmtpPort"], out var port)
        || port is <= 0 or > 65535
        || !MailAddress.TryCreate(fromAddress, out var sender)
        || !MailAddress.TryCreate(recipient, out _))
    {
        logger.LogWarning("Account email for {AccountEmail} was not sent because SMTP settings or recipient are invalid.", recipient);
        return false;
    }

    var username = configuration["Email:SmtpUsername"];
    var password = configuration["Email:SmtpPassword"];
    if (string.IsNullOrWhiteSpace(username) != string.IsNullOrWhiteSpace(password))
    {
        logger.LogWarning("Account email for {AccountEmail} was not sent because SMTP credentials are incomplete.", recipient);
        return false;
    }

    try
    {
        var displayName = configuration["Email:FromName"] ?? "PC STORE";
        var subject = isPasswordReset
            ? "Thông tin đặt lại mật khẩu tài khoản PC STORE"
            : "Thông tin tài khoản PC STORE";
        using var message = new MailMessage
        {
            From = new MailAddress(sender!.Address, displayName),
            Subject = subject,
            Body = $"""
                Xin chào {fullName},

                {(isPasswordReset ? "Mật khẩu tài khoản của bạn vừa được đặt lại." : "Tài khoản PC STORE của bạn đã được tạo.")}

                Email đăng nhập: {recipient}
                Mật khẩu tạm thời: {temporaryPassword}

                Vui lòng đăng nhập và bảo mật thông tin này. Nếu bạn không thực hiện yêu cầu này, hãy liên hệ quản trị viên.
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
        logger.LogError(exception, "Could not send account credentials email to {AccountEmail}.", recipient);
        return false;
    }
    catch (FormatException exception)
    {
        logger.LogError(exception, "Could not send account credentials email because an email address is invalid.");
        return false;
    }
    catch (InvalidOperationException exception)
    {
        logger.LogError(exception, "Could not send account credentials email to {AccountEmail}.", recipient);
        return false;
    }
}

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

/// <summary>Customer request to cancel an eligible order or return its components.</summary>
public sealed record CreateOrderActionRequest
{
    /// <summary>Requested action: Cancel or Refund.</summary>
    public required string RequestType { get; init; }

    /// <summary>Customer's reason for requesting the action.</summary>
    public required string Reason { get; init; }
}

/// <summary>Manager/Admin decision for a pending customer order action request.</summary>
public sealed record ReviewOrderActionRequest
{
    /// <summary>Whether to approve the request.</summary>
    public bool Approve { get; init; }

    /// <summary>Optional decision note sent to the customer.</summary>
    public string? Note { get; init; }
}

/// <summary>Records an authorized order document print request.</summary>
public sealed record PrintOrderDocumentRequest
{
    public required string DocumentType { get; init; }
    public required string ChangedByName { get; init; }
}

/// <summary>Updates or clears a product's warehouse shelf location.</summary>
public sealed record UpdateWarehouseLocationRequest
{
    public string? WarehouseLocation { get; init; }
}

/// <summary>Records a supplier delivery added to available product stock.</summary>
public sealed record ReceiveInventoryRequest
{
    /// <summary>Number of units received.</summary>
    public int Quantity { get; init; }

    /// <summary>Optional unit purchase price for the received stock.</summary>
    public decimal? UnitCost { get; init; }

    /// <summary>Optional supplier name.</summary>
    public string? Supplier { get; init; }

    /// <summary>Optional receiving note.</summary>
    public string? Note { get; init; }
}

/// <summary>Sets the physical inventory count after stocktaking.</summary>
public sealed record AdjustInventoryRequest
{
    /// <summary>Counted physical quantity including items held for confirmed orders.</summary>
    public int ActualStock { get; init; }

    /// <summary>Required reason for the inventory adjustment.</summary>
    public required string Note { get; init; }
}

/// <summary>Sets the low-stock warning threshold for a product.</summary>
public sealed record UpdateLowStockThresholdRequest
{
    /// <summary>Available-stock count at or below which the product is low stock.</summary>
    public int Threshold { get; init; }
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
    public string? VoucherCode { get; set; }
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

/// <summary>Payload for creating a product category.</summary>
public sealed record CreateCategoryRequest
{
    /// <summary>Unique display name for the category.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Product component type associated with the category.</summary>
    public string ComponentType { get; init; } = string.Empty;

    /// <summary>Optional category description.</summary>
    public string? Description { get; init; }

    /// <summary>Non-negative order used to sort the category in the storefront.</summary>
    public int DisplayOrder { get; init; }
}

/// <summary>Payload for updating a product category.</summary>
public sealed record UpdateCategoryRequest
{
    /// <summary>Unique display name for the category.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Product component type associated with the category.</summary>
    public string ComponentType { get; init; } = string.Empty;

    /// <summary>Optional category description.</summary>
    public string? Description { get; init; }

    /// <summary>Non-negative order used to sort the category in the storefront.</summary>
    public int DisplayOrder { get; init; }
}

/// <summary>Payload for creating a product brand.</summary>
public sealed record CreateBrandRequest
{
    /// <summary>Unique display name for the brand.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional HTTP or HTTPS logo URL.</summary>
    public string? LogoUrl { get; init; }

    /// <summary>Optional brand description.</summary>
    public string? Description { get; init; }

    /// <summary>Non-negative order used to sort the brand in the storefront.</summary>
    public int DisplayOrder { get; init; }
}

/// <summary>Payload for updating a product brand.</summary>
public sealed record UpdateBrandRequest
{
    /// <summary>Unique display name for the brand.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional HTTP or HTTPS logo URL.</summary>
    public string? LogoUrl { get; init; }

    /// <summary>Optional brand description.</summary>
    public string? Description { get; init; }

    /// <summary>Non-negative order used to sort the brand in the storefront.</summary>
    public int DisplayOrder { get; init; }
}

/// <summary>Payload for hiding or reactivating a category or brand.</summary>
public sealed record SetCatalogActiveRequest(bool IsActive);

/// <summary>Payload for creating or updating a product and its technical specifications.</summary>
public sealed record CreateProductRequest
{
    /// <summary>Product display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Unique stock keeping unit.</summary>
    public string Sku { get; init; } = string.Empty;

    /// <summary>Optional managed brand name.</summary>
    public string? Brand { get; init; }

    /// <summary>Optional short product description.</summary>
    public string? Description { get; init; }

    /// <summary>Sale price in Vietnamese dong.</summary>
    public decimal Price { get; init; }

    /// <summary>Optional promotional price, which cannot exceed the listed price.</summary>
    public decimal? PromotionalPrice { get; init; }

    /// <summary>Start of the promotional pricing period.</summary>
    public DateTimeOffset? PromotionStartsAt { get; init; }

    /// <summary>End of the promotional pricing period.</summary>
    public DateTimeOffset? PromotionEndsAt { get; init; }

    /// <summary>Available inventory quantity.</summary>
    public int StockQuantity { get; init; }

    /// <summary>Product category identifier.</summary>
    public int CategoryId { get; init; }

    /// <summary>Optional HTTP(S) image URL or supported image data URL up to 5 MB.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>Optional CPU or board socket.</summary>
    public string? Socket { get; init; }

    /// <summary>Optional RAM standard.</summary>
    public string? RamType { get; init; }

    /// <summary>Optional motherboard chipset.</summary>
    public string? Chipset { get; init; }

    /// <summary>Optional physical form factor.</summary>
    public string? FormFactor { get; init; }

    /// <summary>Optional memory bus speed in MHz.</summary>
    public int? RamBusSpeed { get; init; }

    /// <summary>Optional number of memory slots.</summary>
    public int? RamSlots { get; init; }

    /// <summary>Optional device power or thermal design wattage.</summary>
    public int? TdpWattage { get; init; }

    /// <summary>Optional recommended power supply wattage.</summary>
    public int? RecommendedPsu { get; init; }

    /// <summary>Optional component length in millimeters.</summary>
    public int? LengthMm { get; init; }

    /// <summary>Optional category-specific specifications.</summary>
    public Dictionary<string, string>? AdditionalSpecs { get; init; }
}

/// <summary>Payload for creating or updating a voucher campaign.</summary>
public sealed record CreateVoucherRequest
{
    /// <summary>Unique code, containing letters, digits, and hyphens.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Discount type: Percentage or FixedAmount.</summary>
    public string DiscountType { get; init; } = string.Empty;

    /// <summary>Discount percentage or fixed VND amount.</summary>
    public decimal DiscountValue { get; init; }

    /// <summary>Minimum eligible product subtotal in Vietnamese dong.</summary>
    public decimal MinimumOrderAmount { get; init; }

    /// <summary>Maximum discount amount for percentage vouchers.</summary>
    public decimal? MaximumDiscountAmount { get; init; }

    /// <summary>Maximum successful redemptions.</summary>
    public int UsageLimit { get; init; }

    /// <summary>Start of the voucher active period.</summary>
    public DateTimeOffset StartsAt { get; init; }

    /// <summary>End of the voucher active period (exclusive).</summary>
    public DateTimeOffset EndsAt { get; init; }
}

/// <summary>Payload to validate a voucher against a current cart subtotal.</summary>
public sealed record ValidateVoucherRequest
{
    /// <summary>Voucher code to check.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Cart subtotal before voucher discount.</summary>
    public decimal OrderSubtotal { get; init; }
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

/// <summary>Payload containing the PC configuration saved by its owner.</summary>
public sealed record SavePcBuildRequest
{
    /// <summary>Display name of the saved configuration.</summary>
    public string? BuildName { get; init; }

    /// <summary>Intended use of the PC configuration.</summary>
    public string? Purpose { get; init; }

    /// <summary>Selected product identifiers and quantities.</summary>
    public List<SavePcBuildItemRequest>? Items { get; init; }

    /// <summary>Whether the builder's compatibility checks passed.</summary>
    public bool IsCompatible { get; init; }
}

/// <summary>Filters recorded when a manager exports a revenue report.</summary>
public sealed record ReportExportRequest
{
    /// <summary>Requested file format: xls or pdf.</summary>
    public string? Format { get; init; }

    /// <summary>Inclusive start date for the report.</summary>
    public DateTime? FromDate { get; init; }

    /// <summary>Inclusive end date for the report.</summary>
    public DateTime? ToDate { get; init; }

    /// <summary>Optional product category filter.</summary>
    public int? CategoryId { get; init; }
}

/// <summary>Information required to create an employee or manager account.</summary>
public sealed record CreateStaffAccountRequest
{
    /// <summary>Account holder's full name.</summary>
    public string? FullName { get; init; }

    /// <summary>Unique sign-in email address.</summary>
    public string? Email { get; init; }

    /// <summary>Optional contact phone number.</summary>
    public string? PhoneNumber { get; init; }

    /// <summary>Must be Staff or Manager, except the reserved protected support Admin email.</summary>
    public string? Role { get; init; }
}

/// <summary>A selected product in a saved PC configuration.</summary>
public sealed record SavePcBuildItemRequest
{
    /// <summary>Product identifier.</summary>
    public int ProductId { get; init; }

    /// <summary>Number of units selected.</summary>
    public int Quantity { get; init; }
}

/// <summary>Selected product identifiers submitted for compatibility analysis.</summary>
public sealed record CheckPcCompatibilityRequest
{
    /// <summary>Unique product identifiers, with at most one product per component category.</summary>
    public required List<int> ProductIds { get; init; }
}

/// <summary>A compatibility rule violation found in a selected PC configuration.</summary>
public sealed record PcCompatibilityIssue(
    string Severity,
    string Code,
    string Message,
    IReadOnlyList<ComponentType> CategoryTypes);
