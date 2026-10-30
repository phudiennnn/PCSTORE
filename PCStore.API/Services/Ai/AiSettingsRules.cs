using System.Text.Json;
using System.Text.RegularExpressions;
using PCStore.Core.Ai;

namespace PCStore.API.Services.Ai;

/// <summary>Kiểm tra dữ liệu form cấu hình AI trước khi ghi.</summary>
internal static partial class AiSettingsRules
{
    public static void ValidateOllama(OllamaSettingsWriteModel model)
    {
        ValidateUrl(model.BaseUrl, "Ollama");
        ValidateModelName(model.ModelName, "Ollama");
        if (!KeepAliveRegex().IsMatch(model.KeepAlive?.Trim() ?? string.Empty))
            throw new ArgumentException("KeepAlive của Ollama phải có dạng 30m, 30s, 1h hoặc -1.");
        ValidateRange(model.NumCtx, AiSettingBounds.MinNumCtx, AiSettingBounds.MaxNumCtx, "NumCtx của Ollama");
        ValidateTimeouts(model.TimeoutSeconds, model.ConnectTimeoutSeconds, "Ollama");
    }

    public static void ValidateGemini(GeminiSettingsWriteModel model)
    {
        ValidateUrl(model.BaseUrl, "Gemini");
        ValidateModelName(model.ModelName, "Gemini");
        if (string.IsNullOrWhiteSpace(model.ApiVersion) || !ApiVersionRegex().IsMatch(model.ApiVersion.Trim()))
            throw new ArgumentException("ApiVersion của Gemini phải có dạng v1 hoặc v1beta.");
        ValidateTimeouts(model.TimeoutSeconds, model.ConnectTimeoutSeconds, "Gemini");
        if (model.Temperature < AiSettingBounds.MinTemperature || model.Temperature > AiSettingBounds.MaxTemperature)
            throw new ArgumentException("Temperature của Gemini phải từ 0 đến 2.");
        ValidateRange(model.MaxOutputTokens, AiSettingBounds.MinOutputTokens, AiSettingBounds.MaxOutputTokens, "MaxOutputTokens của Gemini");
        if (model.FallbackModelNames.Count > AiSettingBounds.MaxFallbackModels)
            throw new ArgumentException($"Gemini chỉ nhận tối đa {AiSettingBounds.MaxFallbackModels} model dự phòng.");
        foreach (var name in model.FallbackModelNames)
            ValidateModelName(name, "Gemini dự phòng");
    }

    public static string SerializeFallbackModels(IReadOnlyList<string> names)
    {
        var cleaned = names
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return JsonSerializer.Serialize(cleaned);
    }

    public static IReadOnlyList<string> ReadFallbackModels(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static int Clamp(int value, int minimum, int maximum) =>
        Math.Clamp(value, minimum, maximum);

    private static void ValidateUrl(string? url, string provider)
    {
        if (string.IsNullOrWhiteSpace(url)
            || url.Length > AiSettingBounds.MaxUrlLength
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)
            || parsed.Scheme is not ("http" or "https"))
            throw new ArgumentException($"BaseUrl của {provider} phải là URL http hoặc https.");
    }

    private static void ValidateModelName(string? modelName, string provider)
    {
        if (string.IsNullOrWhiteSpace(modelName) || modelName.Trim().Length > AiSettingBounds.MaxModelNameLength)
            throw new ArgumentException($"Tên model {provider} không được trống và không quá {AiSettingBounds.MaxModelNameLength} ký tự.");
    }

    private static void ValidateTimeouts(int timeoutSeconds, int connectTimeoutSeconds, string provider)
    {
        ValidateRange(timeoutSeconds, AiSettingBounds.MinTimeoutSeconds, AiSettingBounds.MaxTimeoutSeconds, $"TimeoutSeconds của {provider}");
        ValidateRange(connectTimeoutSeconds, AiSettingBounds.MinConnectTimeoutSeconds, AiSettingBounds.MaxConnectTimeoutSeconds, $"ConnectTimeoutSeconds của {provider}");
    }

    private static void ValidateRange(int value, int minimum, int maximum, string label)
    {
        if (value < minimum || value > maximum)
            throw new ArgumentException($"{label} phải từ {minimum} đến {maximum}.");
    }

    [GeneratedRegex(@"^(-1|\d+[smh]?)$", RegexOptions.CultureInvariant)]
    private static partial Regex KeepAliveRegex();

    [GeneratedRegex(@"^v[0-9][A-Za-z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ApiVersionRegex();
}
