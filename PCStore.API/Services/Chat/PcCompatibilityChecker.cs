namespace PCStore.API.Services.Chat;

/// <summary>Một linh kiện đã rút gọn để kiểm tra tương thích, không phụ thuộc database.</summary>
public sealed class PcPartSnapshot
{
    public required string Role { get; init; }
    public string? Name { get; init; }
    public string? Socket { get; init; }
    public string? RamType { get; init; }
    public string? FormFactor { get; init; }
    public int? TdpWattage { get; init; }
    public int? RecommendedPsuWattage { get; init; }
    public int? LengthMm { get; init; }
    public int? HeightMm { get; init; }
    public int? MaxGpuLengthMm { get; init; }
    public int? MaxCoolerHeightMm { get; init; }
}

/// <summary>Một lỗi tương thích do code phát hiện.</summary>
public sealed class PcCompatibilityIssue
{
    public required string Code { get; init; }
    public required string Message { get; init; }
}

/// <summary>Kiểm tra socket, RAM, nguồn và kích thước. Thiếu dữ liệu thì bỏ qua luật đó.</summary>
public static class PcCompatibilityChecker
{
    public const string Cpu = "cpu";
    public const string Mainboard = "mainboard";
    public const string Ram = "ram";
    public const string Gpu = "gpu";
    public const string Psu = "psu";
    public const string Case = "case";
    public const string Cooler = "cooler";

    public static IReadOnlyList<PcCompatibilityIssue> Check(
        IReadOnlyCollection<PcPartSnapshot> parts,
        int powerReserveWatts,
        int defaultRamWatts)
    {
        var issues = new List<PcCompatibilityIssue>();
        var cpu = Find(parts, Cpu);
        var board = Find(parts, Mainboard);
        var ram = Find(parts, Ram);
        var gpu = Find(parts, Gpu);
        var psu = Find(parts, Psu);
        var pcCase = Find(parts, Case);
        var cooler = Find(parts, Cooler);

        if (HasText(cpu?.Socket) && HasText(board?.Socket)
            && !Same(cpu!.Socket, board!.Socket))
        {
            issues.Add(Issue("socket", $"Socket CPU ({cpu.Socket}) không khớp mainboard ({board.Socket})."));
        }

        if (HasText(board?.RamType) && HasText(ram?.RamType))
        {
            var supported = Split(board!.RamType);
            if (!supported.Contains(ram!.RamType!.Trim(), StringComparer.OrdinalIgnoreCase))
                issues.Add(Issue("ram-type", $"Mainboard hỗ trợ RAM {board.RamType}, RAM đã chọn là {ram.RamType}."));
        }

        var psuCapacity = psu?.TdpWattage ?? psu?.RecommendedPsuWattage ?? 0;
        if (psuCapacity > 0 && (cpu is not null || gpu is not null))
        {
            var estimated = (cpu?.TdpWattage ?? 0)
                + (gpu?.TdpWattage ?? 0)
                + (ram is null ? 0 : ram.TdpWattage ?? Math.Max(0, defaultRamWatts))
                + Math.Max(0, powerReserveWatts);
            if (estimated > psuCapacity)
                issues.Add(Issue("power", $"Công suất ước tính {estimated}W vượt nguồn {psuCapacity}W."));
        }

        if (HasText(board?.FormFactor) && HasText(pcCase?.FormFactor)
            && !CaseSupportsBoard(pcCase!.FormFactor!, board!.FormFactor!))
        {
            issues.Add(Issue("case-form-factor", $"Mainboard {board.FormFactor} có thể không vừa case {pcCase.FormFactor}."));
        }

        if (gpu?.LengthMm is int gpuLength && pcCase?.MaxGpuLengthMm is int gpuLimit && gpuLength > gpuLimit)
            issues.Add(Issue("gpu-length", $"Card dài {gpuLength}mm, case chỉ nhận tới {gpuLimit}mm."));

        if (HasText(cpu?.Socket) && HasText(cooler?.Socket))
        {
            var supportedSockets = Split(cooler!.Socket);
            if (!supportedSockets.Contains(cpu!.Socket!.Trim(), StringComparer.OrdinalIgnoreCase))
                issues.Add(Issue("cooler-socket", $"Tản nhiệt không khai báo socket {cpu.Socket}."));
        }

        if (cooler?.HeightMm is int coolerHeight && pcCase?.MaxCoolerHeightMm is int coolerLimit && coolerHeight > coolerLimit)
            issues.Add(Issue("cooler-height", $"Tản nhiệt cao {coolerHeight}mm, case chỉ nhận tới {coolerLimit}mm."));

        return issues;
    }

    public static bool CaseSupportsBoard(string caseFormFactor, string boardFormFactor)
    {
        var caseForm = caseFormFactor.ToLowerInvariant();
        var boardForm = boardFormFactor.ToLowerInvariant();
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

    private static PcPartSnapshot? Find(IReadOnlyCollection<PcPartSnapshot> parts, string role) =>
        parts.FirstOrDefault(part => string.Equals(part.Role, role, StringComparison.OrdinalIgnoreCase));

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> Split(string? value) =>
        (value ?? string.Empty)
            .Split(['/', ',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static PcCompatibilityIssue Issue(string code, string message) =>
        new() { Code = code, Message = message };
}
