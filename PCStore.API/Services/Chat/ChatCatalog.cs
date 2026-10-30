using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PCStore.Core.Ai;
using PCStore.Core.Entities;
using PCStore.Core.Enums;
using PCStore.Infrastructure.Data;

namespace PCStore.API.Services.Chat;

/// <summary>Lọc linh kiện còn hàng và ghép cấu hình trước khi nhờ AI diễn giải.</summary>
public sealed class ChatCatalog : IChatCatalog
{
    private static readonly string[] SearchStopwords = ["dung", "cho", "voi", "hay", "hoac", "nao", "nhe", "minh", "ban", "giup", "muon", "xem", "may"];
    private static readonly ComponentType[] BuildOrder =
    [
        ComponentType.CPU,
        ComponentType.Mainboard,
        ComponentType.RAM,
        ComponentType.GPU,
        ComponentType.Cooler,
        ComponentType.Case,
        ComponentType.SSD,
        ComponentType.PSU,
        ComponentType.Monitor,
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<ChatOptions> _options;

    public ChatCatalog(IServiceScopeFactory scopeFactory, IOptionsMonitor<ChatOptions> options)
    {
        _scopeFactory = scopeFactory;
        _options = options;
    }

    public async Task<ChatGrounding> GroundAsync(IntentExtractionResult intent, string customerMessage, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (intent.Intent == CustomerIntent.BuildPc)
            return await BuildAsync(intent, options, cancellationToken);
        return await FindMentionedPartsAsync(intent, customerMessage, options, cancellationToken);
    }

    private async Task<ChatGrounding> BuildAsync(IntentExtractionResult intent, ChatOptions options, CancellationToken cancellationToken)
    {
        var budget = ClampBudget(intent.BudgetVnd, options);
        var assumed = intent.BudgetVnd is null or <= 0;
        var shares = ReadShares(intent.Purpose, options);
        if (intent.NeedMonitor)
            shares[ComponentType.Monitor] = options.MonitorShare;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var types = shares.Keys.ToArray();
        var products = await context.Products
            .AsNoTracking()
            .Include(product => product.Category)
            .Include(product => product.TechnicalSpec)
            .Where(product => product.IsActive
                && product.StockQuantity > 0
                && product.Category != null
                && product.Category.IsActive
                && types.Contains(product.Category.ComponentType))
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var priced = products
            .Select(product => new StockPart(product, EffectivePrice(product, now), ToSnapshot(product)))
            .Where(part => part.Price > 0)
            .ToList();
        var ceiling = decimal.Floor(budget * Math.Max(1m, options.BudgetCeilingRatio));
        var selected = new List<StockPart>();
        var spent = 0m;
        foreach (var type in BuildOrder)
        {
            if (!shares.TryGetValue(type, out var share) || share <= 0)
                continue;
            var target = Math.Min(ceiling - spent, decimal.Floor(budget * share));
            if (target <= 0)
                continue;
            var pool = priced
                .Where(part => part.Product.Category!.ComponentType == type && part.Price <= ceiling - spent)
                .Where(part => BrandMatches(type, part, intent))
                .OrderBy(part => Math.Abs(part.Price - target))
                .Take(Math.Max(1, options.CandidateLimit))
                .ToList();
            foreach (var candidate in pool)
            {
                var trial = selected.Select(part => part.Snapshot).Append(candidate.Snapshot).ToList();
                if (PcCompatibilityChecker.Check(trial, options.PowerReserveWatts, options.DefaultRamWatts).Count > 0)
                    continue;
                selected.Add(candidate);
                spent += candidate.Price;
                break;
            }
        }

        var hasCore = selected.Any(part => part.Product.Category!.ComponentType == ComponentType.CPU)
            && selected.Any(part => part.Product.Category!.ComponentType == ComponentType.Mainboard)
            && selected.Any(part => part.Product.Category!.ComponentType == ComponentType.RAM);
        if (!hasCore)
            return new ChatGrounding { CallModel = false, FixedMessage = options.NoBuildMessage };

        return Describe(CustomerIntent.BuildPc, selected, options, budget, assumed);
    }

    private async Task<ChatGrounding> FindMentionedPartsAsync(
        IntentExtractionResult intent,
        string customerMessage,
        ChatOptions options,
        CancellationToken cancellationToken)
    {
        var tokens = Tokenize(customerMessage).Take(6).ToList();
        if (tokens.Count == 0)
            return new ChatGrounding { CallModel = false, FixedMessage = options.MissingPartsMessage };

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var found = new Dictionary<int, Product>();
        var limit = Math.Clamp(options.PartSearchLimit, 1, 20);
        foreach (var token in tokens)
        {
            var pattern = "%" + token.Replace("%", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal) + "%";
            var rows = await context.Products
                .AsNoTracking()
                .Include(product => product.Category)
                .Include(product => product.TechnicalSpec)
                .Where(product => product.IsActive && product.StockQuantity > 0 && EF.Functions.ILike(product.Name, pattern))
                .OrderBy(product => product.Price)
                .Take(limit)
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
                found.TryAdd(row.Id, row);
            if (found.Count >= limit)
                break;
        }

        var now = DateTimeOffset.UtcNow;
        var selected = found.Values
            .Take(Math.Clamp(options.CandidateLimit, 1, 20))
            .Select(product => new StockPart(product, EffectivePrice(product, now), ToSnapshot(product)))
            .ToList();
        var minimum = intent.Intent == CustomerIntent.CheckCompatibility ? 2 : 1;
        if (selected.Count < minimum)
            return new ChatGrounding { CallModel = false, FixedMessage = options.MissingPartsMessage };
        return Describe(intent.Intent, selected, options, null, false);
    }

    private static ChatGrounding Describe(
        string intent,
        IReadOnlyList<StockPart> selected,
        ChatOptions options,
        long? budget,
        bool budgetAssumed)
    {
        var distinctRoles = selected
            .GroupBy(part => part.Snapshot.Role, StringComparer.OrdinalIgnoreCase)
            .All(group => group.Count() == 1);
        var issues = distinctRoles
            ? PcCompatibilityChecker.Check(selected.Select(part => part.Snapshot).ToList(), options.PowerReserveWatts, options.DefaultRamWatts)
            : [];
        var cards = selected.Select(part => new ChatProductCard
        {
            Id = part.Product.Id,
            Name = part.Product.Name,
            Brand = part.Product.Brand,
            PriceVnd = (long)decimal.Round(part.Price, 0),
            Role = part.Snapshot.Role,
            Socket = part.Snapshot.Socket,
            RamType = part.Snapshot.RamType,
            StockQuantity = part.Product.StockQuantity,
        }).ToList();
        var payload = new
        {
            intent,
            budget_vnd = budget,
            budget_note = budgetAssumed ? "Khách chưa nói ngân sách, dùng mức mặc định trong cấu hình." : null,
            compatible = issues.Count == 0,
            issues = issues.Select(issue => new { code = issue.Code, message = issue.Message }),
            parts = cards.Select(card => new
            {
                name = card.Name,
                brand = card.Brand,
                price_vnd = card.PriceVnd,
                role = card.Role,
                socket = card.Socket,
                ram_type = card.RamType,
            }),
        };
        return new ChatGrounding
        {
            CallModel = true,
            PartsJson = JsonSerializer.Serialize(payload, ChatJson.Options),
            Products = cards,
        };
    }

    private static Dictionary<ComponentType, decimal> ReadShares(string? purpose, ChatOptions options)
    {
        var key = string.IsNullOrWhiteSpace(purpose) ? PcPurpose.Other : purpose.Trim();
        if (!options.PurposeBudgetShares.TryGetValue(key, out var configured) && options.PurposeBudgetShares.Count > 0)
            configured = options.PurposeBudgetShares.Values.First();
        var shares = new Dictionary<ComponentType, decimal>();
        if (configured is null)
            return shares;
        foreach (var pair in configured)
        {
            if (Enum.TryParse<ComponentType>(pair.Key, true, out var type) && pair.Value > 0)
                shares[type] = pair.Value;
        }
        return shares;
    }

    private static bool BrandMatches(ComponentType type, StockPart part, IntentExtractionResult intent)
    {
        var brand = type == ComponentType.CPU ? intent.CpuBrand : type == ComponentType.GPU ? intent.GpuBrand : null;
        if (string.IsNullOrWhiteSpace(brand))
            return true;
        var needle = brand.Trim();
        return (part.Product.Brand?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
            || part.Product.Name.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private static long ClampBudget(long? budget, ChatOptions options)
    {
        var value = budget is null or <= 0 ? options.DefaultBudgetVnd : budget.Value;
        if (value < options.MinBudgetVnd)
            return options.MinBudgetVnd;
        if (value > options.MaxBudgetVnd)
            return options.MaxBudgetVnd;
        return value;
    }

    private static decimal EffectivePrice(Product product, DateTimeOffset now)
    {
        if (product.PromotionalPrice is decimal promotional
            && product.PromotionStartsAt is DateTimeOffset starts
            && product.PromotionEndsAt is DateTimeOffset ends
            && starts <= now
            && now < ends)
            return promotional;
        return product.Price;
    }

    private static PcPartSnapshot ToSnapshot(Product product)
    {
        var spec = product.TechnicalSpec;
        return new PcPartSnapshot
        {
            Role = RoleName(product.Category?.ComponentType),
            Name = product.Name,
            Socket = spec?.Socket,
            RamType = spec?.RamType,
            FormFactor = spec?.FormFactor,
            TdpWattage = spec?.TdpWattage,
            RecommendedPsuWattage = spec?.RecommendedPsu,
            LengthMm = spec?.LengthMm,
            HeightMm = ReadExtra(spec?.AdditionalSpecsJson, "heightMm"),
            MaxGpuLengthMm = ReadExtra(spec?.AdditionalSpecsJson, "maxGpuLengthMm"),
            MaxCoolerHeightMm = ReadExtra(spec?.AdditionalSpecsJson, "maxCpuCoolerHeightMm"),
        };
    }

    private static string RoleName(ComponentType? type) => type switch
    {
        ComponentType.CPU => PcCompatibilityChecker.Cpu,
        ComponentType.Mainboard => PcCompatibilityChecker.Mainboard,
        ComponentType.RAM => PcCompatibilityChecker.Ram,
        ComponentType.GPU => PcCompatibilityChecker.Gpu,
        ComponentType.PSU => PcCompatibilityChecker.Psu,
        ComponentType.Case => PcCompatibilityChecker.Case,
        ComponentType.Cooler => PcCompatibilityChecker.Cooler,
        ComponentType.SSD => "ssd",
        ComponentType.Monitor => "monitor",
        _ => "other",
    };

    private static int? ReadExtra(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<string> Tokenize(string message)
    {
        var normalized = Faq.FaqMatcher.NormalizeVietnamese(message);
        return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 3 && !SearchStopwords.Contains(token));
    }

    private sealed record StockPart(Product Product, decimal Price, PcPartSnapshot Snapshot);
}
