using System.Text.Json;
using PCStore.Core.Ai;

namespace PCStore.API.Services.Ai;

/// <summary>Kiểm tra JSON ý định: enum, kiểu và các trường bắt buộc.</summary>
public sealed class AiJsonValidator : IAiJsonValidator
{
    private static readonly HashSet<string> Intents = new(CustomerIntent.All, StringComparer.Ordinal);
    private static readonly HashSet<string> Purposes = new(PcPurpose.All, StringComparer.Ordinal);

    public AiJsonValidationResult ValidateIntentJson(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return Invalid("JSON không hợp lệ.");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Invalid("JSON gốc phải là object.");

            var root = document.RootElement;
            var errors = new List<string>();
            var intent = ReadEnum(root, "intent", Intents, required: true, errors);
            var purpose = ReadEnum(root, "purpose", Purposes, required: false, errors);
            var budget = ReadBudget(root, errors);
            var cpuBrand = ReadOptionalString(root, "cpu_brand", errors);
            var gpuBrand = ReadOptionalString(root, "gpu_brand", errors);
            var notes = ReadOptionalString(root, "notes", errors);
            var needMonitor = ReadBoolean(root, "need_monitor", errors);

            if (errors.Count > 0 || intent is null || needMonitor is null)
                return new AiJsonValidationResult { IsValid = false, Errors = errors };

            return new AiJsonValidationResult
            {
                IsValid = true,
                Intent = new IntentExtractionResult
                {
                    Intent = intent,
                    BudgetVnd = budget,
                    Purpose = purpose,
                    CpuBrand = cpuBrand,
                    GpuBrand = gpuBrand,
                    NeedMonitor = needMonitor.Value,
                    Notes = notes,
                    ProviderName = string.Empty,
                    PromptVersion = PromptTemplates.PromptVersion,
                },
            };
        }
    }

    private static AiJsonValidationResult Invalid(string error) =>
        new() { IsValid = false, Errors = [error] };

    private static string? ReadEnum(
        JsonElement root,
        string name,
        HashSet<string> allowed,
        bool required,
        List<string> errors)
    {
        if (!root.TryGetProperty(name, out var property))
        {
            if (required)
                errors.Add($"Thiếu trường {name}.");
            return null;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            if (required)
                errors.Add($"Trường {name} không được null.");
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            errors.Add($"Trường {name} phải là chuỗi hoặc null.");
            return null;
        }

        var value = property.GetString()?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            if (required)
                errors.Add($"Trường {name} đang trống.");
            return null;
        }

        if (!allowed.Contains(value))
        {
            errors.Add($"Trường {name} không thuộc danh sách cho phép.");
            return null;
        }

        return value;
    }

    private static long? ReadBudget(JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("budget_vnd", out var property) || property.ValueKind == JsonValueKind.Null)
            return null;

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt64(out var budget))
        {
            errors.Add("Trường budget_vnd phải là số nguyên hoặc null.");
            return null;
        }

        if (budget < 0)
            errors.Add("Trường budget_vnd không được âm.");

        return budget;
    }

    private static string? ReadOptionalString(JsonElement root, string name, List<string> errors)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;

        if (property.ValueKind != JsonValueKind.String)
        {
            errors.Add($"Trường {name} phải là chuỗi hoặc null.");
            return null;
        }

        var value = property.GetString()?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static bool? ReadBoolean(JsonElement root, string name, List<string> errors)
    {
        if (!root.TryGetProperty(name, out var property))
        {
            errors.Add($"Thiếu trường {name}.");
            return null;
        }

        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            errors.Add($"Trường {name} phải là true hoặc false.");
            return null;
        }

        return property.GetBoolean();
    }
}
