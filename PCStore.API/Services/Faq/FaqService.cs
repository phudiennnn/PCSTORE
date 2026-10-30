using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PCStore.Core.Ai;
using PCStore.Infrastructure.Data;

namespace PCStore.API.Services.Faq;

/// <summary>Chấm FAQ đang bật và giữ danh sách trong bộ nhớ cho đến khi Admin lưu.</summary>
public sealed class FaqService : IFaqService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<FaqOptions> _options;
    private readonly IAiClock _clock;
    private readonly object _cacheGate = new();
    private IReadOnlyList<CachedFaq> _cache = [];
    private IReadOnlyList<CachedFaq> _unfilled = [];
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    public FaqService(IServiceScopeFactory scopeFactory, IOptionsMonitor<FaqOptions> options, IAiClock clock)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _clock = clock;
    }

    public async Task<FaqMatch?> FindMatchingFaqAsync(string userQuestion, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var normalized = FaqMatcher.NormalizeVietnamese(userQuestion);
        if (normalized.Length == 0)
            return null;

        var entries = await ReadEntriesAsync(options, cancellationToken);
        CachedFaq? bestEntry = null;
        var bestScore = 0d;
        foreach (var entry in entries)
        {
            var score = FaqMatcher.Score(normalized, entry.NormalizedQuestion, entry.Keywords, options.KeywordWeight, options.QuestionWeight);
            if (score < options.MinimumScore)
                continue;
            if (bestEntry is not null && (score < bestScore || (Math.Abs(score - bestScore) < 0.0001 && entry.Priority <= bestEntry.Priority)))
                continue;
            bestEntry = entry;
            bestScore = score;
        }

        if (bestEntry is null)
            return null;
        return new FaqMatch
        {
            Id = bestEntry.Id,
            Category = bestEntry.Category,
            Question = bestEntry.Question,
            Answer = bestEntry.Answer,
            Score = bestScore,
        };
    }

    public async Task<string?> FindUnfilledNoticeAsync(string userQuestion, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var normalized = FaqMatcher.NormalizeVietnamese(userQuestion);
        if (normalized.Length == 0 || string.IsNullOrWhiteSpace(options.UnfilledCustomerMessage))
            return null;

        await ReadEntriesAsync(options, cancellationToken);
        IReadOnlyList<CachedFaq> entries;
        lock (_cacheGate)
            entries = _unfilled;

        foreach (var entry in entries)
        {
            var score = FaqMatcher.Score(normalized, entry.NormalizedQuestion, entry.Keywords, options.KeywordWeight, options.QuestionWeight);
            if (score >= options.MinimumScore)
                return options.UnfilledCustomerMessage;
        }

        return null;
    }

    public async Task<FaqMatch?> FindNearestFaqAsync(string userQuestion, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var normalized = FaqMatcher.NormalizeVietnamese(userQuestion);
        if (normalized.Length == 0)
            return null;

        var entries = await ReadEntriesAsync(options, cancellationToken);
        CachedFaq? bestEntry = null;
        var bestScore = 0d;
        foreach (var entry in entries)
        {
            var score = FaqMatcher.Score(normalized, entry.NormalizedQuestion, entry.Keywords, options.KeywordWeight, options.QuestionWeight);
            if (score <= 0)
                continue;
            if (bestEntry is not null && (score < bestScore || (Math.Abs(score - bestScore) < 0.0001 && entry.Priority <= bestEntry.Priority)))
                continue;
            bestEntry = entry;
            bestScore = score;
        }

        if (bestEntry is null)
            return null;
        return new FaqMatch
        {
            Id = bestEntry.Id,
            Category = bestEntry.Category,
            Question = bestEntry.Question,
            Answer = bestEntry.Answer,
            Score = bestScore,
        };
    }

    public void InvalidateCache()
    {
        lock (_cacheGate)
            _cachedAt = DateTimeOffset.MinValue;
    }

    private async Task<IReadOnlyList<CachedFaq>> ReadEntriesAsync(FaqOptions options, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        lock (_cacheGate)
        {
            if (now - _cachedAt < TimeSpan.FromSeconds(Math.Max(1, options.CacheSeconds)))
                return _cache;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await context.Faqs.AsNoTracking()
            .Select(item => new { item.Id, item.Category, item.Question, item.Answer, item.Keywords, item.Priority, item.IsActive })
            .ToListAsync(cancellationToken);
        var entries = rows
            .Where(item => item.IsActive && !FaqMatcher.ContainsPlaceholder(item.Answer, options.PlaceholderMarker))
            .Select(item => Map(item.Id, item.Category, item.Question, item.Answer, item.Keywords, item.Priority))
            .ToList();
        var unfilled = rows
            .Where(item => FaqMatcher.ContainsPlaceholder(item.Answer, options.PlaceholderMarker))
            .Select(item => Map(item.Id, item.Category, item.Question, item.Answer, item.Keywords, item.Priority))
            .ToList();
        lock (_cacheGate)
        {
            _cache = entries;
            _unfilled = unfilled;
            _cachedAt = now;
        }

        return entries;
    }

    private static CachedFaq Map(int id, string category, string question, string answer, string keywords, int priority) =>
        new(id, category, question, FaqMatcher.NormalizeVietnamese(question), answer, keywords, priority);

    private sealed record CachedFaq(int Id, string Category, string Question, string NormalizedQuestion, string Answer, string Keywords, int Priority);
}
