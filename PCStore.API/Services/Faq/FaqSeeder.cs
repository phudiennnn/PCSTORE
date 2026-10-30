using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FaqEntity = PCStore.Core.Entities.Faq;
using PCStore.Infrastructure.Data;

namespace PCStore.API.Services.Faq;

/// <summary>Nạp FAQ khi bảng trống và chỉ cập nhật từ khóa khi câu cùng nội dung seed.</summary>
public static class FaqSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task SeedAsync(AppDbContext context, FaqOptions options, string contentRootPath, ILogger logger, CancellationToken cancellationToken)
    {
        var path = ResolveSeedPath(contentRootPath);
        if (path is null)
        {
            logger.LogWarning("Không thấy file seed FAQ.");
            return;
        }

        List<FaqSeedItem>? items;
        try
        {
            await using var stream = File.OpenRead(path);
            items = await JsonSerializer.DeserializeAsync<List<FaqSeedItem>>(stream, JsonOptions, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            logger.LogWarning(exception, "Không đọc được file seed FAQ.");
            return;
        }

        if (items is null || items.Count == 0)
            return;

        if (await context.Faqs.AnyAsync(cancellationToken))
        {
            await RefreshSeedKeywordsAsync(context, items, logger, cancellationToken);
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var item in items)
        {
            var answer = item.Answer?.Trim() ?? string.Empty;
            var needsFill = FaqMatcher.ContainsPlaceholder(answer, options.PlaceholderMarker);
            var model = new FaqWriteModel
            {
                Category = item.Category,
                Question = item.Question,
                Answer = answer,
                Keywords = item.Keywords,
                Priority = item.Priority,
                IsActive = item.IsActive && !needsFill,
            };
            try
            {
                FaqRules.Validate(model, options);
            }
            catch (ArgumentException exception)
            {
                logger.LogWarning("Bỏ qua câu FAQ seed không hợp lệ: {Message}", exception.Message);
                continue;
            }

            context.Faqs.Add(new FaqEntity
            {
                Category = model.Category!.Trim(),
                Question = model.Question!.Trim(),
                Answer = model.Answer!.Trim(),
                Keywords = model.Keywords!.Trim(),
                Priority = model.Priority,
                IsActive = model.IsActive,
                UpdatedAt = now,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task RefreshSeedKeywordsAsync(AppDbContext context, List<FaqSeedItem> items, ILogger logger, CancellationToken cancellationToken)
    {
        var rows = await context.Faqs.ToListAsync(cancellationToken);
        var updated = 0;
        foreach (var item in items)
        {
            var question = item.Question?.Trim() ?? string.Empty;
            var answer = item.Answer?.Trim() ?? string.Empty;
            var keywords = item.Keywords?.Trim() ?? string.Empty;
            var row = rows.FirstOrDefault(entry => entry.Question == question && entry.Answer == answer);
            if (row is null || keywords.Length == 0 || row.Keywords == keywords)
                continue;
            row.Keywords = keywords;
            row.UpdatedAt = DateTime.UtcNow;
            updated++;
        }

        if (updated == 0)
            return;
        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Đã cập nhật từ khóa cho {Count} câu FAQ.", updated);
    }

    private static string? ResolveSeedPath(string contentRootPath)
    {
        var candidates = new[]
        {
            Path.Combine(contentRootPath, "Data", "faq-seed.json"),
            Path.Combine(AppContext.BaseDirectory, "Data", "faq-seed.json"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private sealed class FaqSeedItem
    {
        public string? Category { get; set; }
        public string? Question { get; set; }
        public string? Answer { get; set; }
        public string? Keywords { get; set; }
        public int Priority { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
