using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PCStore.API.Services.Ai;
using PCStore.API.Services.Chat;
using PCStore.Core.Ai;
using FaqEntity = PCStore.Core.Entities.Faq;
using PCStore.Infrastructure.Data;

namespace PCStore.API.Services.Faq;

/// <summary>API Admin quản lý FAQ. Chỉ tài khoản Admin.</summary>
public static class AdminContentEndpoints
{
    public static void MapAdminContentEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/csrf-token", IssueCsrfAsync);
        var faqs = app.MapGroup("/api/admin/faqs");
        faqs.MapGet("", ListFaqsAsync);
        faqs.MapGet("/export", ExportFaqsAsync);
        faqs.MapPost("", CreateFaqAsync);
        faqs.MapPost("/import", ImportFaqsAsync);
        faqs.MapPut("/{id:int}", UpdateFaqAsync);
        faqs.MapPatch("/{id:int}/active", SetFaqActiveAsync);
        faqs.MapDelete("/{id:int}", DeleteFaqAsync);

        var ai = app.MapGroup("/api/admin/ai-settings");
        ai.MapGet("", ReadAiSettingsAsync);
        ai.MapGet("/summary", ReadAiSummaryAsync);
        ai.MapPut("/ollama", SaveOllamaAsync);
        ai.MapPut("/gemini", SaveGeminiAsync);
        ai.MapPost("/ollama/test", TestOllamaAsync);
        ai.MapPost("/gemini/test", TestGeminiAsync);
    }

    private static async Task<IResult> IssueCsrfAsync(HttpContext httpContext, AppDbContext context, AdminRequestGuard guard, CancellationToken cancellationToken)
    {
        var denied = guard.RejectDisallowedOrigin(httpContext);
        if (denied is not null) return denied;
        var admin = await guard.FindAdministratorAsync(httpContext, context, cancellationToken);
        if (admin is null) return Results.Json(new { message = "Chỉ Admin mới xem được mục này." }, statusCode: StatusCodes.Status403Forbidden);
        return Results.Ok(new { token = guard.IssueCsrfToken(admin.Id), headerName = guard.CsrfHeaderName });
    }

    private static async Task<IResult> ListFaqsAsync(HttpContext httpContext, AppDbContext context, AdminRequestGuard guard, IOptions<FaqOptions> options, string? search, string? category, int page = 1, int? pageSize = null, bool? needsFill = null, CancellationToken cancellationToken = default)
    {
        var denied = await RequireAdminAsync(httpContext, context, guard, false, cancellationToken);
        if (denied is not null) return denied;
        var limits = options.Value;
        var size = Math.Clamp(pageSize ?? limits.PageSize, 1, Math.Max(1, limits.MaxPageSize));
        var currentPage = Math.Max(1, page);
        var query = context.Faqs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(item => item.Category == category);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(item => item.Question.ToLower().Contains(term) || item.Answer.ToLower().Contains(term) || item.Keywords.ToLower().Contains(term));
        }
        if (needsFill == true) query = query.Where(item => item.Answer.Contains(limits.PlaceholderMarker));
        var total = await query.CountAsync(cancellationToken);
        var placeholderCount = await context.Faqs.CountAsync(item => item.Answer.Contains(limits.PlaceholderMarker), cancellationToken);
        var rows = await query.OrderBy(item => item.Category).ThenByDescending(item => item.Priority).ThenBy(item => item.Id).Skip((currentPage - 1) * size).Take(size).ToListAsync(cancellationToken);
        var categories = await context.Faqs.AsNoTracking().Select(item => item.Category).Distinct().OrderBy(item => item).ToListAsync(cancellationToken);
        return Results.Ok(new { items = rows.Select(item => ToListItem(item, limits.PlaceholderMarker)), page = currentPage, pageSize = size, total, placeholderCount, categories });
    }

    private static async Task<IResult> ExportFaqsAsync(HttpContext httpContext, AppDbContext context, AdminRequestGuard guard, IOptions<FaqOptions> options, CancellationToken cancellationToken)
    {
        var denied = await RequireAdminAsync(httpContext, context, guard, false, cancellationToken);
        if (denied is not null) return denied;
        var marker = options.Value.PlaceholderMarker;
        var rows = await context.Faqs.AsNoTracking().OrderBy(item => item.Id).ToListAsync(cancellationToken);
        return Results.Ok(rows.Select(item => ToListItem(item, marker)));
    }

    private static async Task<IResult> CreateFaqAsync(HttpContext httpContext, AppDbContext context, AdminRequestGuard guard, IFaqService faqService, IOptions<FaqOptions> options, FaqWriteModel model, CancellationToken cancellationToken)
    {
        var denied = await RequireAdminAsync(httpContext, context, guard, true, cancellationToken);
        if (denied is not null) return denied;
        var error = ValidateOrMessage(model, options.Value);
        if (error is not null) return error;
        var row = Apply(new FaqEntity(), model);
        context.Faqs.Add(row);
        await context.SaveChangesAsync(cancellationToken);
        faqService.InvalidateCache();
        return Results.Ok(ToListItem(row, options.Value.PlaceholderMarker));
    }

    private static async Task<IResult> UpdateFaqAsync(int id, HttpContext httpContext, AppDbContext context, AdminRequestGuard guard, IFaqService faqService, IOptions<FaqOptions> options, FaqWriteModel model, CancellationToken cancellationToken)
    {
        var denied = await RequireAdminAsync(httpContext, context, guard, true, cancellationToken);
        if (denied is not null) return denied;
        var error = ValidateOrMessage(model, options.Value);
        if (error is not null) return error;
        var row = await context.Faqs.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null) return Results.NotFound(new { message = "Không tìm thấy câu FAQ." });
        Apply(row, model);
        await context.SaveChangesAsync(cancellationToken);
        faqService.InvalidateCache();
        return Results.Ok(ToListItem(row, options.Value.PlaceholderMarker));
    }

    private static async Task<IResult> SetFaqActiveAsync(int id, HttpContext httpContext, AppDbContext context, AdminRequestGuard guard, IFaqService faqService, IOptions<FaqOptions> options, FaqActiveRequest request, CancellationToken cancellationToken)
    {
        var denied = await RequireAdminAsync(httpContext, context, guard, true, cancellationToken);
        if (denied is not null) return denied;
        var row = await context.Faqs.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null) return Results.NotFound(new { message = "Không tìm thấy câu FAQ." });
        if (request.IsActive && FaqMatcher.ContainsPlaceholder(row.Answer, options.Value.PlaceholderMarker))
            return Results.BadRequest(new { message = "Câu còn nội dung cần điền thì không được bật." });
        row.IsActive = request.IsActive;
        row.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        faqService.InvalidateCache();
        return Results.Ok(ToListItem(row, options.Value.PlaceholderMarker));
    }

    private static async Task<IResult> DeleteFaqAsync(int id, HttpContext httpContext, AppDbContext context, AdminRequestGuard guard, IFaqService faqService, CancellationToken cancellationToken)
    {
        var denied = await RequireAdminAsync(httpContext, context, guard, true, cancellationToken);
        if (denied is not null) return denied;
        var row = await context.Faqs.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null) return Results.NotFound(new { message = "Không tìm thấy câu FAQ." });
        context.Faqs.Remove(row);
        await context.SaveChangesAsync(cancellationToken);
        faqService.InvalidateCache();
        return Results.Ok(new { message = "Đã xóa câu FAQ." });
    }

    private static async Task<IResult> ImportFaqsAsync(HttpContext httpContext, AppDbContext context, AdminRequestGuard guard, IFaqService faqService, IOptions<FaqOptions> options, List<FaqImportItem> items, CancellationToken cancellationToken)
    {
        var denied = await RequireAdminAsync(httpContext, context, guard, true, cancellationToken);
        if (denied is not null) return denied;
        if (items.Count == 0 || items.Count > options.Value.MaxImportItems)
            return Results.BadRequest(new { message = $"File JSON phải có từ 1 đến {options.Value.MaxImportItems} câu." });
        var created = 0;
        var updated = 0;
        foreach (var item in items)
        {
            var model = new FaqWriteModel
            {
                Category = item.Category,
                Question = item.Question,
                Answer = item.Answer,
                Keywords = item.Keywords,
                Priority = item.Priority,
                IsActive = item.IsActive && !FaqMatcher.ContainsPlaceholder(item.Answer, options.Value.PlaceholderMarker),
            };
            var error = ValidateOrMessage(model, options.Value);
            if (error is not null) return error;
            FaqEntity? row = item.Id is > 0 ? await context.Faqs.FirstOrDefaultAsync(entry => entry.Id == item.Id, cancellationToken) : null;
            if (row is null)
            {
                context.Faqs.Add(Apply(new FaqEntity(), model));
                created++;
            }
            else
            {
                Apply(row, model);
                updated++;
            }
        }
        await context.SaveChangesAsync(cancellationToken);
        faqService.InvalidateCache();
        return Results.Ok(new { message = $"Đã nhập {created} câu mới và cập nhật {updated} câu.", created, updated });
    }

    private static async Task<IResult> ReadAiSettingsAsync(HttpContext httpContext, AppDbContext context, AdminRequestGuard guard, IAiSettingsService settingsService, IOllamaHealthState healthState, CancellationToken cancellationToken)
    {
        var denied = await RequireAdminAsync(httpContext, context, guard, false, cancellationToken);
        if (denied is not null) return denied;
        var settings = await settingsService.GetAdminViewAsync(cancellationToken);
        var health = healthState.LastResult;
        return Results.Ok(new
        {
            settings.Ollama,
            settings.Gemini,
            settings.GeminiApiKeyConfigured,
            settings.GeminiApiKeyUnreadable,
            ollamaHealth = health is null ? null : new { health.IsHealthy, health.Message, health.LatencyMilliseconds, checkedAt = health.CheckedAt },
        });
    }

    private static async Task<IResult> SaveOllamaAsync(HttpContext httpContext, AppDbContext context, AdminRequestGuard guard, IAiSettingsService settingsService, OllamaSettingsWriteModel model, CancellationToken cancellationToken)
    {
        var denied = await RequireAdminAsync(httpContext, context, guard, true, cancellationToken);
        if (denied is not null) return denied;
        try { await settingsService.SaveOllamaSettingsAsync(model, cancellationToken); }
        catch (ArgumentException exception) { return Results.BadRequest(new { message = exception.Message }); }
        return Results.Ok(await settingsService.GetAdminViewAsync(cancellationToken));
    }

    private static async Task<IResult> SaveGeminiAsync(HttpContext httpContext, AppDbContext context, AdminRequestGuard guard, IAiSettingsService settingsService, GeminiSettingsWriteModel model, CancellationToken cancellationToken)
    {
        var denied = await RequireAdminAsync(httpContext, context, guard, true, cancellationToken);
        if (denied is not null) return denied;
        try { await settingsService.SaveGeminiSettingsAsync(model, cancellationToken); }
        catch (ArgumentException exception) { return Results.BadRequest(new { message = exception.Message }); }
        return Results.Ok(await settingsService.GetAdminViewAsync(cancellationToken));
    }

    private static Task<IResult> TestOllamaAsync(HttpContext httpContext, AppDbContext context, AdminRequestGuard guard, OllamaProvider provider, CancellationToken cancellationToken) =>
        TestProviderAsync(httpContext, context, guard, provider, cancellationToken);

    private static Task<IResult> TestGeminiAsync(HttpContext httpContext, AppDbContext context, AdminRequestGuard guard, GeminiProvider provider, CancellationToken cancellationToken) =>
        TestProviderAsync(httpContext, context, guard, provider, cancellationToken);

    private static async Task<IResult> TestProviderAsync(HttpContext httpContext, AppDbContext context, AdminRequestGuard guard, IAiProvider provider, CancellationToken cancellationToken)
    {
        var denied = await RequireAdminAsync(httpContext, context, guard, true, cancellationToken);
        if (denied is not null) return denied;
        var health = await provider.CheckHealthAsync(cancellationToken);
        return Results.Ok(new { provider = health.ProviderName, isHealthy = health.IsHealthy, message = health.Message, latencyMilliseconds = health.LatencyMilliseconds });
    }

    private static async Task<IResult?> RequireAdminAsync(HttpContext httpContext, AppDbContext context, AdminRequestGuard guard, bool requireCsrf, CancellationToken cancellationToken)
    {
        var originDenied = guard.RejectDisallowedOrigin(httpContext);
        if (originDenied is not null) return originDenied;
        var admin = await guard.FindAdministratorAsync(httpContext, context, cancellationToken);
        if (admin is null) return Results.Json(new { message = "Chỉ Admin mới dùng được mục này." }, statusCode: StatusCodes.Status403Forbidden);
        if (requireCsrf && !guard.HasValidCsrf(httpContext, admin.Id))
            return Results.Json(new { message = "Phiên biểu mẫu đã hết hạn. Hãy tải lại trang rồi thử lại." }, statusCode: StatusCodes.Status403Forbidden);
        return null;
    }

    private static IResult? ValidateOrMessage(FaqWriteModel model, FaqOptions options)
    {
        try
        {
            FaqRules.Validate(model, options);
            return null;
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    }

    private static FaqEntity Apply(FaqEntity row, FaqWriteModel model)
    {
        row.Category = model.Category!.Trim();
        row.Question = model.Question!.Trim();
        row.Answer = model.Answer!.Trim();
        row.Keywords = model.Keywords!.Trim();
        row.Priority = model.Priority;
        row.IsActive = model.IsActive;
        row.UpdatedAt = DateTime.UtcNow;
        return row;
    }

    private static object ToListItem(FaqEntity item, string marker) => new
    {
        item.Id,
        item.Category,
        item.Question,
        item.Answer,
        item.Keywords,
        item.Priority,
        item.IsActive,
        item.UpdatedAt,
        needsFill = FaqMatcher.ContainsPlaceholder(item.Answer, marker),
    };

    private sealed class FaqActiveRequest
    {
        public bool IsActive { get; set; }
    }

    private sealed class FaqImportItem
    {
        public int? Id { get; set; }
        public string? Category { get; set; }
        public string? Question { get; set; }
        public string? Answer { get; set; }
        public string? Keywords { get; set; }
        public int Priority { get; set; }
        public bool IsActive { get; set; }
    }
}
