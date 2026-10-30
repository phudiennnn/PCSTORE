using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PCStore.Core.Ai;
using PCStore.Core.Entities;
using PCStore.Infrastructure.Data;

namespace PCStore.API.Services.Ai;

/// <summary>Ghi AiRequestLog và nuốt lỗi ghi để không chặn luồng tư vấn.</summary>
public sealed partial class AiRequestLogService : IAiRequestLogService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AiRequestLogService> _logger;

    public AiRequestLogService(IServiceScopeFactory scopeFactory, ILogger<AiRequestLogService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task WriteAsync(AiRequestLogEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.AiRequestLogs.Add(new AiRequestLog
            {
                Provider = Trim(entry.Provider, 20),
                Intent = Trim(entry.Intent, AiSettingBounds.MaxIntentLength),
                LatencyMilliseconds = Math.Max(0, entry.LatencyMilliseconds),
                IsSuccess = entry.IsSuccess,
                FailoverReason = RedactSecrets(TrimNullable(entry.FailoverReason, AiSettingBounds.MaxFailoverReasonLength)),
                TimeToFirstTokenMilliseconds = entry.TimeToFirstTokenMilliseconds is < 0 ? 0 : entry.TimeToFirstTokenMilliseconds,
                FaqHit = entry.FaqHit,
                CreatedAt = DateTime.UtcNow,
            });
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning("Không ghi được AiRequestLog cho {Provider}: {Message}", entry.Provider, exception.Message);
        }
    }

    public async Task<AiUsageSummary> ReadRecentSummaryAsync(int lookbackHours, CancellationToken cancellationToken)
    {
        var hours = Math.Clamp(lookbackHours, 1, 24 * 30);
        var since = DateTime.UtcNow.AddHours(-hours);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await context.AiRequestLogs.AsNoTracking()
                .Where(log => log.CreatedAt >= since)
                .Select(log => new { log.Provider, log.LatencyMilliseconds, log.IsSuccess, log.FailoverReason, log.TimeToFirstTokenMilliseconds, log.FaqHit })
                .ToListAsync(cancellationToken);
            var firstTokenSamples = rows.Where(row => row.TimeToFirstTokenMilliseconds.HasValue).Select(row => row.TimeToFirstTokenMilliseconds!.Value).ToList();
            return new AiUsageSummary
            {
                LookbackHours = hours,
                RequestCount = rows.Count,
                SuccessCount = rows.Count(row => row.IsSuccess),
                FaqHitCount = rows.Count(row => row.FaqHit),
                FailoverCount = rows.Count(row => !string.IsNullOrWhiteSpace(row.FailoverReason)),
                AverageLatencyMilliseconds = Average(rows.Select(row => row.LatencyMilliseconds)),
                AverageTimeToFirstTokenMilliseconds = Average(firstTokenSamples),
                Providers = rows
                    .GroupBy(row => string.IsNullOrWhiteSpace(row.Provider) ? "Khác" : row.Provider)
                    .OrderByDescending(group => group.Count())
                    .Select(group => new AiProviderUsage
                    {
                        Provider = group.Key,
                        RequestCount = group.Count(),
                        SuccessCount = group.Count(row => row.IsSuccess),
                        AverageLatencyMilliseconds = Average(group.Select(row => row.LatencyMilliseconds)),
                    })
                    .ToList(),
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning("Không đọc được thống kê AI: {Message}", exception.Message);
            return new AiUsageSummary { LookbackHours = hours };
        }
    }

    private static int? Average(IEnumerable<int> values)
    {
        var list = values.ToList();
        if (list.Count == 0)
            return null;
        return (int)Math.Round(list.Average());
    }

    private static string Trim(string value, int maxLength)
    {
        var text = value ?? string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static string? TrimNullable(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? RedactSecrets(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return ApiKeyRegex().Replace(value, "[redacted]");
    }

    [GeneratedRegex(@"AIza[0-9A-Za-z\-_]{10,}", RegexOptions.CultureInvariant)]
    private static partial Regex ApiKeyRegex();
}
