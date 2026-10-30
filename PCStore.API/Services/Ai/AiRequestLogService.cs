using System.Text.RegularExpressions;
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
                CreatedAt = DateTime.UtcNow,
            });
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning("Không ghi được AiRequestLog cho {Provider}: {Message}", entry.Provider, exception.Message);
        }
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
