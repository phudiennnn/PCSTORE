using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PCStore.Core.Ai;
using PCStore.Core.Entities;
using PCStore.Infrastructure.Data;

namespace PCStore.API.Services.Ai;

/// <summary>Đọc và ghi cấu hình AI, giải mã API Key và làm mới bộ nhớ đệm khi dữ liệu đổi.</summary>
public sealed class AiSettingsService : IAiSettingsService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IOptionsMonitor<AiOptions> _options;
    private readonly IAiClock _clock;
    private readonly ILogger<AiSettingsService> _logger;
    private readonly object _cacheGate = new();
    private CacheEntry? _cache;

    public AiSettingsService(
        IServiceScopeFactory scopeFactory,
        IDataProtectionProvider dataProtectionProvider,
        IOptionsMonitor<AiOptions> options,
        IAiClock clock,
        ILogger<AiSettingsService> logger)
    {
        _scopeFactory = scopeFactory;
        _dataProtectionProvider = dataProtectionProvider;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public async Task<AiRuntimeSettings> GetRuntimeSettingsAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var refresh = TimeSpan.FromSeconds(Math.Max(0, _options.CurrentValue.CacheRefreshSeconds));
        var current = ReadCache();
        if (current is not null && now - current.CachedAt < refresh)
            return current.Settings;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await context.AiProviderSettings.AsNoTracking().ToListAsync(cancellationToken);
        var updatedAt = rows.Count == 0
            ? DateTime.MinValue
            : rows.Max(row => row.UpdatedAt);
        if (current is not null && current.DatabaseUpdatedAtUtc == updatedAt)
        {
            WriteCache(new CacheEntry
            {
                Settings = current.Settings,
                DatabaseUpdatedAtUtc = updatedAt,
                CachedAt = now,
            });
            return current.Settings;
        }

        var settings = MapRuntime(rows, _options.CurrentValue);
        WriteCache(new CacheEntry
        {
            Settings = settings,
            DatabaseUpdatedAtUtc = updatedAt,
            CachedAt = now,
        });
        return settings;
    }

    public async Task<AiSettingsAdminView> GetAdminViewAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await context.AiProviderSettings.AsNoTracking().ToListAsync(cancellationToken);
        var options = _options.CurrentValue;
        var ollama = rows.FirstOrDefault(row => row.ProviderName == AiProviderNames.Ollama);
        var gemini = rows.FirstOrDefault(row => row.ProviderName == AiProviderNames.Gemini);
        var keyState = ReadGeminiKey(gemini?.ProtectedApiKey, options);
        return new AiSettingsAdminView
        {
            Ollama = ToOllamaWriteModel(ollama, options),
            Gemini = ToGeminiWriteModel(gemini, options),
            GeminiApiKeyConfigured = !string.IsNullOrWhiteSpace(keyState.ApiKey),
            GeminiApiKeyUnreadable = keyState.Unreadable,
            OllamaUpdatedAtUtc = ollama?.UpdatedAt,
            GeminiUpdatedAtUtc = gemini?.UpdatedAt,
        };
    }

    public async Task SaveOllamaSettingsAsync(OllamaSettingsWriteModel model, CancellationToken cancellationToken)
    {
        AiSettingsRules.ValidateOllama(model);
        await SaveAsync(AiProviderNames.Ollama, cancellationToken, row =>
        {
            row.BaseUrl = model.BaseUrl.Trim();
            row.ModelName = model.ModelName.Trim();
            row.KeepAlive = model.KeepAlive.Trim();
            row.NumCtx = model.NumCtx;
            row.TimeoutSeconds = model.TimeoutSeconds;
            row.ConnectTimeoutSeconds = model.ConnectTimeoutSeconds;
            row.IsMaintenanceMode = model.IsMaintenanceMode;
            row.IsEnabled = !model.IsMaintenanceMode;
        });
    }

    public async Task SaveGeminiSettingsAsync(GeminiSettingsWriteModel model, CancellationToken cancellationToken)
    {
        AiSettingsRules.ValidateGemini(model);
        var protector = CreateProtector(_options.CurrentValue.DataProtectionPurpose);
        await SaveAsync(AiProviderNames.Gemini, cancellationToken, row =>
        {
            row.BaseUrl = model.BaseUrl.Trim();
            row.ModelName = model.ModelName.Trim();
            row.FallbackModelNames = AiSettingsRules.SerializeFallbackModels(model.FallbackModelNames);
            row.ApiVersion = model.ApiVersion.Trim();
            row.TimeoutSeconds = model.TimeoutSeconds;
            row.ConnectTimeoutSeconds = model.ConnectTimeoutSeconds;
            row.Temperature = model.Temperature;
            row.MaxOutputTokens = model.MaxOutputTokens;
            row.IsEnabled = model.IsEnabled;
            if (!string.IsNullOrWhiteSpace(model.ApiKey))
                row.ProtectedApiKey = protector.Protect(model.ApiKey.Trim());
        });
    }

    public void InvalidateCache()
    {
        lock (_cacheGate)
            _cache = null;
    }

    public int GetOllamaConnectTimeoutSeconds()
    {
        var cached = ReadCache();
        if (cached is not null)
            return cached.Settings.Ollama.ConnectTimeoutSeconds;
        return AiSettingsRules.Clamp(
            _options.CurrentValue.Ollama.ConnectTimeoutSeconds,
            AiSettingBounds.MinConnectTimeoutSeconds,
            AiSettingBounds.MaxConnectTimeoutSeconds);
    }

    public int GetGeminiConnectTimeoutSeconds()
    {
        var cached = ReadCache();
        if (cached is not null)
            return cached.Settings.Gemini.ConnectTimeoutSeconds;
        return AiSettingsRules.Clamp(
            _options.CurrentValue.Gemini.ConnectTimeoutSeconds,
            AiSettingBounds.MinConnectTimeoutSeconds,
            AiSettingBounds.MaxConnectTimeoutSeconds);
    }

    private async Task SaveAsync(string providerName, CancellationToken cancellationToken, Action<AiProviderSetting> apply)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await context.AiProviderSettings
            .FirstOrDefaultAsync(item => item.ProviderName == providerName, cancellationToken);
        if (row is null)
        {
            row = new AiProviderSetting { ProviderName = providerName, FallbackModelNames = "[]" };
            context.AiProviderSettings.Add(row);
        }

        apply(row);
        row.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        InvalidateCache();
    }

    private AiRuntimeSettings MapRuntime(IReadOnlyList<AiProviderSetting> rows, AiOptions options)
    {
        var ollamaRow = rows.FirstOrDefault(row => row.ProviderName == AiProviderNames.Ollama);
        var geminiRow = rows.FirstOrDefault(row => row.ProviderName == AiProviderNames.Gemini);
        var ollama = MapOllama(ollamaRow, options);
        var gemini = MapGemini(geminiRow, options);
        var explainTemperature = (double)gemini.Temperature;
        return new AiRuntimeSettings
        {
            Ollama = ollama,
            Gemini = gemini,
            CircuitFailureThreshold = Math.Max(1, options.CircuitFailureThreshold),
            CircuitOpenSeconds = Math.Max(1, options.CircuitOpenSeconds),
            ExtractIntent = new IntentTaskSettings
            {
                Temperature = options.Tasks.ExtractIntent.Temperature,
                NumPredict = Math.Max(1, options.Tasks.ExtractIntent.NumPredict),
                MaxRepairInputCharacters = Math.Max(200, options.Tasks.ExtractIntent.MaxRepairInputCharacters),
                MaxCustomerMessageCharacters = Math.Max(1, options.Tasks.ExtractIntent.MaxCustomerMessageCharacters),
            },
            ExplainSelection = new ExplainTaskSettings
            {
                Temperature = explainTemperature,
                MaxOutputTokens = gemini.MaxOutputTokens,
                MaxWords = Math.Max(1, options.Tasks.ExplainSelection.MaxWords),
            },
        };
    }

    private static OllamaRuntimeSettings MapOllama(AiProviderSetting? row, AiOptions options)
    {
        var seed = options.Ollama;
        return new OllamaRuntimeSettings
        {
            BaseUrl = FirstText(row?.BaseUrl, seed.BaseUrl),
            ModelName = FirstText(row?.ModelName, seed.ModelName),
            KeepAlive = FirstText(row?.KeepAlive, seed.KeepAlive),
            NumCtx = row?.NumCtx ?? seed.NumCtx,
            TimeoutSeconds = row?.TimeoutSeconds > 0 ? row.TimeoutSeconds : seed.TimeoutSeconds,
            ConnectTimeoutSeconds = row?.ConnectTimeoutSeconds > 0 ? row.ConnectTimeoutSeconds : seed.ConnectTimeoutSeconds,
            IsMaintenanceMode = row?.IsMaintenanceMode ?? seed.IsMaintenanceMode,
        };
    }

    private GeminiRuntimeSettings MapGemini(AiProviderSetting? row, AiOptions options)
    {
        var seed = options.Gemini;
        var keyState = ReadGeminiKey(row?.ProtectedApiKey, options);
        var fallbacks = row is null
            ? seed.FallbackModelNames
            : AiSettingsRules.ReadFallbackModels(row.FallbackModelNames);
        return new GeminiRuntimeSettings
        {
            BaseUrl = FirstText(row?.BaseUrl, seed.BaseUrl),
            ApiVersion = FirstText(row?.ApiVersion, seed.ApiVersion),
            ModelName = FirstText(row?.ModelName, seed.ModelName),
            FallbackModelNames = fallbacks,
            ApiKey = keyState.ApiKey,
            ApiKeyUnreadable = keyState.Unreadable,
            TimeoutSeconds = row?.TimeoutSeconds > 0 ? row.TimeoutSeconds : seed.TimeoutSeconds,
            ConnectTimeoutSeconds = row?.ConnectTimeoutSeconds > 0 ? row.ConnectTimeoutSeconds : seed.ConnectTimeoutSeconds,
            Temperature = row?.Temperature ?? seed.Temperature,
            MaxOutputTokens = row?.MaxOutputTokens ?? seed.MaxOutputTokens,
            IsEnabled = row?.IsEnabled ?? seed.IsEnabled,
        };
    }

    private (string? ApiKey, bool Unreadable) ReadGeminiKey(string? protectedApiKey, AiOptions options)
    {
        if (string.IsNullOrWhiteSpace(protectedApiKey))
            return (null, false);

        try
        {
            var key = CreateProtector(options.DataProtectionPurpose).Unprotect(protectedApiKey);
            return (string.IsNullOrWhiteSpace(key) ? null : key, false);
        }
        catch (CryptographicException)
        {
            _logger.LogWarning("Không giải mã được API Key Gemini đã lưu. Key không được ghi vào nhật ký.");
            return (null, true);
        }
    }

    private IDataProtector CreateProtector(string purpose) =>
        _dataProtectionProvider.CreateProtector(purpose);

    private static OllamaSettingsWriteModel ToOllamaWriteModel(AiProviderSetting? row, AiOptions options)
    {
        var runtime = MapOllama(row, options);
        return new OllamaSettingsWriteModel
        {
            BaseUrl = runtime.BaseUrl,
            ModelName = runtime.ModelName,
            KeepAlive = runtime.KeepAlive,
            NumCtx = runtime.NumCtx,
            TimeoutSeconds = runtime.TimeoutSeconds,
            ConnectTimeoutSeconds = runtime.ConnectTimeoutSeconds,
            IsMaintenanceMode = runtime.IsMaintenanceMode,
        };
    }

    private static GeminiSettingsWriteModel ToGeminiWriteModel(AiProviderSetting? row, AiOptions options)
    {
        var seed = options.Gemini;
        return new GeminiSettingsWriteModel
        {
            ApiKey = null,
            BaseUrl = FirstText(row?.BaseUrl, seed.BaseUrl),
            ApiVersion = FirstText(row?.ApiVersion, seed.ApiVersion),
            ModelName = FirstText(row?.ModelName, seed.ModelName),
            FallbackModelNames = row is null
                ? seed.FallbackModelNames
                : AiSettingsRules.ReadFallbackModels(row.FallbackModelNames),
            TimeoutSeconds = row?.TimeoutSeconds > 0 ? row.TimeoutSeconds : seed.TimeoutSeconds,
            ConnectTimeoutSeconds = row?.ConnectTimeoutSeconds > 0 ? row.ConnectTimeoutSeconds : seed.ConnectTimeoutSeconds,
            Temperature = row?.Temperature ?? seed.Temperature,
            MaxOutputTokens = row?.MaxOutputTokens ?? seed.MaxOutputTokens,
            IsEnabled = row?.IsEnabled ?? seed.IsEnabled,
        };
    }

    private static string FirstText(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private CacheEntry? ReadCache()
    {
        lock (_cacheGate)
            return _cache;
    }

    private void WriteCache(CacheEntry entry)
    {
        lock (_cacheGate)
            _cache = entry;
    }

    private sealed class CacheEntry
    {
        public required AiRuntimeSettings Settings { get; init; }

        public required DateTime DatabaseUpdatedAtUtc { get; init; }

        public required DateTimeOffset CachedAt { get; init; }
    }
}
