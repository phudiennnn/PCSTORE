using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PCStore.Core.Ai;
using PCStore.Core.Entities;
using PCStore.Infrastructure.Data;

namespace PCStore.API.Services.Ai;

/// <summary>Tạo dòng cấu hình AI mặc định nếu chưa có, không ghi đè dữ liệu đã lưu.</summary>
public static class AiProviderSettingsSeeder
{
    public static async Task SeedAsync(
        AppDbContext context,
        AiOptions options,
        IDataProtectionProvider dataProtectionProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var hasOllama = await context.AiProviderSettings
            .AnyAsync(row => row.ProviderName == AiProviderNames.Ollama, cancellationToken);
        if (!hasOllama)
        {
            context.AiProviderSettings.Add(new AiProviderSetting
            {
                ProviderName = AiProviderNames.Ollama,
                BaseUrl = (options.Ollama.BaseUrl ?? string.Empty).Trim(),
                ModelName = (options.Ollama.ModelName ?? string.Empty).Trim(),
                FallbackModelNames = "[]",
                KeepAlive = (options.Ollama.KeepAlive ?? string.Empty).Trim(),
                NumCtx = options.Ollama.NumCtx,
                TimeoutSeconds = AiSettingsRules.Clamp(options.Ollama.TimeoutSeconds, AiSettingBounds.MinTimeoutSeconds, AiSettingBounds.MaxTimeoutSeconds),
                ConnectTimeoutSeconds = AiSettingsRules.Clamp(options.Ollama.ConnectTimeoutSeconds, AiSettingBounds.MinConnectTimeoutSeconds, AiSettingBounds.MaxConnectTimeoutSeconds),
                IsEnabled = !options.Ollama.IsMaintenanceMode,
                IsMaintenanceMode = options.Ollama.IsMaintenanceMode,
                UpdatedAt = DateTime.UtcNow,
            });
            logger.LogInformation("Đã tạo cấu hình Ollama mặc định từ appsettings.");
        }

        var hasGemini = await context.AiProviderSettings
            .AnyAsync(row => row.ProviderName == AiProviderNames.Gemini, cancellationToken);
        if (!hasGemini)
        {
            var legacy = await ReadLegacyAsync(context, options, dataProtectionProvider, logger, cancellationToken);
            var apiKey = !string.IsNullOrWhiteSpace(options.Gemini.ApiKey)
                ? options.Gemini.ApiKey.Trim()
                : legacy.ApiKey;
            var protector = dataProtectionProvider.CreateProtector(options.DataProtectionPurpose);
            context.AiProviderSettings.Add(new AiProviderSetting
            {
                ProviderName = AiProviderNames.Gemini,
                BaseUrl = (options.Gemini.BaseUrl ?? string.Empty).Trim(),
                ModelName = First(legacy.Model, options.Gemini.ModelName ?? string.Empty),
                FallbackModelNames = JsonSerializer.Serialize(options.Gemini.FallbackModelNames ?? []),
                ApiVersion = (options.Gemini.ApiVersion ?? string.Empty).Trim(),
                ProtectedApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : protector.Protect(apiKey),
                TimeoutSeconds = AiSettingsRules.Clamp(options.Gemini.TimeoutSeconds, AiSettingBounds.MinTimeoutSeconds, AiSettingBounds.MaxTimeoutSeconds),
                ConnectTimeoutSeconds = AiSettingsRules.Clamp(options.Gemini.ConnectTimeoutSeconds, AiSettingBounds.MinConnectTimeoutSeconds, AiSettingBounds.MaxConnectTimeoutSeconds),
                Temperature = legacy.Temperature ?? options.Gemini.Temperature,
                MaxOutputTokens = legacy.MaxOutputTokens ?? options.Gemini.MaxOutputTokens,
                IsEnabled = options.Gemini.IsEnabled,
                IsMaintenanceMode = false,
                UpdatedAt = DateTime.UtcNow,
            });
            if (!string.IsNullOrWhiteSpace(legacy.ApiKey) && string.IsNullOrWhiteSpace(options.Gemini.ApiKey))
                logger.LogInformation("Đã chuyển API Key Gemini từ cấu hình cũ sang bảng mới. Giá trị key không được ghi log.");
            else
                logger.LogInformation("Đã tạo cấu hình Gemini mặc định. API Key để trống cho đến khi Admin nhập.");
        }

        await ReplaceRetiredGeminiModelAsync(context, options, logger, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task ReplaceRetiredGeminiModelAsync(
        AppDbContext context,
        AiOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var retired = new HashSet<string>(options.Gemini.RetiredModelNames ?? [], StringComparer.Ordinal);
        var replacement = (options.Gemini.ModelName ?? string.Empty).Trim();
        if (retired.Count == 0 || replacement.Length == 0 || retired.Contains(replacement))
            return;

        var gemini = await context.AiProviderSettings
            .FirstOrDefaultAsync(row => row.ProviderName == AiProviderNames.Gemini, cancellationToken);
        if (gemini is not null && retired.Contains(gemini.ModelName))
        {
            gemini.ModelName = replacement;
            gemini.FallbackModelNames = JsonSerializer.Serialize(options.Gemini.FallbackModelNames ?? []);
            gemini.UpdatedAt = DateTime.UtcNow;
            logger.LogInformation("Đã đổi model Gemini đã ngừng hoạt động sang model trong cấu hình.");
        }

        var legacyModel = await context.SystemSettings
            .FirstOrDefaultAsync(setting => setting.Key == options.LegacySettingKeys.Model, cancellationToken);
        if (legacyModel is not null && retired.Contains(legacyModel.Value))
        {
            legacyModel.Value = replacement;
            logger.LogInformation("Đã đổi model AI cũ đã ngừng hoạt động. Chat dùng model này.");
        }
    }

    private static async Task<LegacyAiValues> ReadLegacyAsync(
        AppDbContext context,
        AiOptions options,
        IDataProtectionProvider dataProtectionProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var keys = new[]
        {
            options.LegacySettingKeys.ApiKey,
            options.LegacySettingKeys.Model,
            options.LegacySettingKeys.Temperature,
            options.LegacySettingKeys.MaxTokens,
        };
        var rows = await context.SystemSettings.AsNoTracking()
            .Where(setting => keys.Contains(setting.Key))
            .ToDictionaryAsync(setting => setting.Key, setting => setting.Value, cancellationToken);

        string? apiKey = null;
        if (rows.TryGetValue(options.LegacySettingKeys.ApiKey, out var protectedKey) && !string.IsNullOrWhiteSpace(protectedKey))
        {
            try
            {
                apiKey = dataProtectionProvider
                    .CreateProtector(options.LegacyDataProtectionPurpose)
                    .Unprotect(protectedKey);
            }
            catch (CryptographicException)
            {
                logger.LogWarning("Không giải mã được API Key AI cũ. Bỏ qua việc copy, không ghi giá trị key.");
            }
        }

        decimal? temperature = null;
        if (rows.TryGetValue(options.LegacySettingKeys.Temperature, out var rawTemperature)
            && decimal.TryParse(rawTemperature, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedTemperature))
            temperature = parsedTemperature;

        int? maxTokens = null;
        if (rows.TryGetValue(options.LegacySettingKeys.MaxTokens, out var rawTokens) && int.TryParse(rawTokens, out var parsedTokens))
            maxTokens = parsedTokens;

        rows.TryGetValue(options.LegacySettingKeys.Model, out var model);
        return new LegacyAiValues(string.IsNullOrWhiteSpace(apiKey) ? null : apiKey, model, temperature, maxTokens);
    }

    private static string First(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback.Trim() : value.Trim();

    private sealed record LegacyAiValues(string? ApiKey, string? Model, decimal? Temperature, int? MaxOutputTokens);
}
