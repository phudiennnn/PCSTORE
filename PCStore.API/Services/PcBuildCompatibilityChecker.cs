using System.Text.Json;
using PCStore.Core.Entities;
using PCStore.Core.Enums;

namespace PCStore.API.Services;

public static class PcBuildCompatibilityChecker
{
    public static List<PcCompatibilityIssue> Check(
        IReadOnlyDictionary<ComponentType, Product> parts)
    {
        parts.TryGetValue(ComponentType.CPU, out var cpu);
        parts.TryGetValue(ComponentType.Mainboard, out var mainboard);
        parts.TryGetValue(ComponentType.RAM, out var ram);
        parts.TryGetValue(ComponentType.GPU, out var gpu);
        parts.TryGetValue(ComponentType.PSU, out var psu);
        parts.TryGetValue(ComponentType.Case, out var pcCase);
        parts.TryGetValue(ComponentType.Cooler, out var cooler);

        var issues = new List<PcCompatibilityIssue>();
        if (!string.IsNullOrWhiteSpace(cpu?.TechnicalSpec?.Socket)
            && !string.IsNullOrWhiteSpace(mainboard?.TechnicalSpec?.Socket)
            && !string.Equals(
                cpu.TechnicalSpec.Socket.Trim(),
                mainboard.TechnicalSpec.Socket.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new(
                "critical",
                "socket",
                $"Socket CPU ({cpu.TechnicalSpec.Socket}) không khớp với mainboard ({mainboard.TechnicalSpec.Socket}).",
                [ComponentType.CPU, ComponentType.Mainboard]));
        }

        if (!string.IsNullOrWhiteSpace(mainboard?.TechnicalSpec?.RamType)
            && !string.IsNullOrWhiteSpace(ram?.TechnicalSpec?.RamType))
        {
            var supportedRamTypes = mainboard.TechnicalSpec.RamType
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (!supportedRamTypes.Contains(ram.TechnicalSpec.RamType.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new(
                    "critical",
                    "ram-type",
                    $"Mainboard hỗ trợ RAM {mainboard.TechnicalSpec.RamType}, nhưng RAM đã chọn là {ram.TechnicalSpec.RamType}.",
                    [ComponentType.Mainboard, ComponentType.RAM]));
            }
        }

        var estimatedPower = (cpu?.TechnicalSpec?.TdpWattage ?? 0)
            + (gpu?.TechnicalSpec?.TdpWattage ?? 0)
            + (ram?.TechnicalSpec?.TdpWattage ?? 10)
            + 50;
        var psuCapacity = psu?.TechnicalSpec?.TdpWattage
            ?? psu?.TechnicalSpec?.RecommendedPsu
            ?? 0;
        if (psuCapacity > 0 && estimatedPower > psuCapacity)
        {
            issues.Add(new(
                "critical",
                "power",
                $"Công suất ước tính ({estimatedPower}W) vượt khả năng nguồn ({psuCapacity}W).",
                [ComponentType.PSU, ComponentType.CPU, ComponentType.GPU]));
        }

        if (mainboard is not null && pcCase is not null
            && !string.IsNullOrWhiteSpace(mainboard.TechnicalSpec?.FormFactor)
            && !string.IsNullOrWhiteSpace(pcCase.TechnicalSpec?.FormFactor)
            && !CaseSupportsMainboard(pcCase, mainboard))
        {
            issues.Add(new(
                "critical",
                "case-form-factor",
                $"Kích thước mainboard ({mainboard.TechnicalSpec.FormFactor}) có thể không vừa case ({pcCase.TechnicalSpec.FormFactor}).",
                [ComponentType.Mainboard, ComponentType.Case]));
        }

        var maxGpuLength = ReadAdditionalSpecInt(pcCase, "maxGpuLengthMm");
        if (gpu?.TechnicalSpec?.LengthMm is int gpuLength
            && maxGpuLength is int gpuLimit
            && gpuLength > gpuLimit)
        {
            issues.Add(new(
                "critical",
                "gpu-length",
                $"Card đồ họa dài {gpuLength}mm, vượt giới hạn case {gpuLimit}mm.",
                [ComponentType.GPU, ComponentType.Case]));
        }

        var coolerSocketSupport = cooler?.TechnicalSpec?.Socket;
        if (!string.IsNullOrWhiteSpace(cpu?.TechnicalSpec?.Socket)
            && !string.IsNullOrWhiteSpace(coolerSocketSupport)
            && !coolerSocketSupport.Split('/', ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Contains(cpu.TechnicalSpec.Socket.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(new(
                "critical",
                "cooler-socket",
                $"Tản nhiệt không khai báo hỗ trợ socket {cpu.TechnicalSpec.Socket} của CPU.",
                [ComponentType.CPU, ComponentType.Cooler]));
        }

        var coolerHeight = ReadAdditionalSpecInt(cooler, "heightMm");
        var maxCoolerHeight = ReadAdditionalSpecInt(pcCase, "maxCpuCoolerHeightMm");
        if (coolerHeight is int coolerSize
            && maxCoolerHeight is int coolerLimit
            && coolerSize > coolerLimit)
        {
            issues.Add(new(
                "critical",
                "cooler-height",
                $"Tản nhiệt cao {coolerSize}mm, vượt giới hạn case {coolerLimit}mm.",
                [ComponentType.Cooler, ComponentType.Case]));
        }

        return issues;
    }

    private static bool CaseSupportsMainboard(Product pcCase, Product mainboard)
    {
        var caseForm = pcCase.TechnicalSpec?.FormFactor?.ToLowerInvariant() ?? string.Empty;
        var boardForm = mainboard.TechnicalSpec?.FormFactor?.ToLowerInvariant() ?? string.Empty;
        if (caseForm.Length == 0 || boardForm.Length == 0)
            return true;

        if (caseForm.Contains("tower"))
        {
            if (caseForm.Contains("mini") && !caseForm.Contains("mid"))
                return boardForm.Contains("itx");
            if (caseForm.Contains("micro"))
                return boardForm.Contains("itx") || boardForm.Contains("micro") || boardForm.Contains("matx");
            if (caseForm.Contains("mid"))
                return boardForm.Contains("itx") || boardForm.Contains("micro") || boardForm.Contains("matx") || boardForm == "atx";
            if (caseForm.Contains("full") || caseForm.Contains("super"))
                return true;
        }

        if (caseForm.Contains("eatx"))
            return boardForm.Contains("eatx") || boardForm == "atx" || boardForm.Contains("micro") || boardForm.Contains("itx");
        if (caseForm.Contains("atx"))
            return boardForm == "atx" || boardForm.Contains("micro") || boardForm.Contains("itx");
        if (caseForm.Contains("micro") || caseForm.Contains("matx"))
            return boardForm.Contains("micro") || boardForm.Contains("matx") || boardForm.Contains("itx");
        if (caseForm.Contains("itx"))
            return boardForm.Contains("itx");

        return true;
    }

    private static int? ReadAdditionalSpecInt(Product? product, string propertyName)
    {
        var json = product?.TechnicalSpec?.AdditionalSpecsJson;
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(propertyName, out var property)
                && property.TryGetInt32(out var value)
                    ? value
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record PcCompatibilityIssue(
    string Severity,
    string Code,
    string Message,
    IReadOnlyList<ComponentType> CategoryTypes);
