using PCStore.API.Services;
using PCStore.Core.Entities;
using PCStore.Core.Enums;

namespace PCStore.Tests;

public class PcBuildCompatibilityTests
{
[Fact(DisplayName = "FUNC-BD01: mismatched CPU and motherboard sockets are reported")]
public void FuncBd01_SocketMismatch_IsReported()
{
var parts = Build(
Part(ComponentType.CPU, new TechnicalSpec { Socket = "AM5" }),
Part(ComponentType.Mainboard, new TechnicalSpec { Socket = "LGA1700" }));

var issues = PcBuildCompatibilityChecker.Check(parts);

Assert.Contains(issues, issue => issue.Code == "socket");
}

[Fact(DisplayName = "FUNC-BD02: DDR4 memory is incompatible with a DDR5-only board")]
public void FuncBd02_RamGenerationMismatch_IsReported()
{
var parts = Build(
Part(ComponentType.Mainboard, new TechnicalSpec { RamType = "DDR5" }),
Part(ComponentType.RAM, new TechnicalSpec { RamType = "DDR4" }));

var issues = PcBuildCompatibilityChecker.Check(parts);

Assert.Contains(issues, issue => issue.Code == "ram-type");
}

[Fact(DisplayName = "FUNC-BD03: 485W estimated load exceeds a 400W PSU")]
public void FuncBd03_485WLoadWith400WPowerSupply_IsReported()
{
var parts = Build(
Part(ComponentType.CPU, new TechnicalSpec { TdpWattage = 125 }),
Part(ComponentType.GPU, new TechnicalSpec { TdpWattage = 300 }),
Part(ComponentType.RAM, new TechnicalSpec { TdpWattage = 10 }),
Part(ComponentType.PSU, new TechnicalSpec { TdpWattage = 400 }));

var issues = PcBuildCompatibilityChecker.Check(parts);

Assert.Contains(issues, issue => issue.Code == "power");
Assert.Contains(issues, issue => issue.Message.Contains("485W", StringComparison.Ordinal));
}

[Fact(DisplayName = "FUNC-BD04: GPU longer than the case limit is reported")]
public void FuncBd04_GpuExceedsCaseLength_IsReported()
{
var parts = Build(
Part(ComponentType.GPU, new TechnicalSpec { LengthMm = 320 }),
Part(ComponentType.Case, new TechnicalSpec
{
AdditionalSpecsJson = "{\"maxGpuLengthMm\":300}"
}));

var issues = PcBuildCompatibilityChecker.Check(parts);

Assert.Contains(issues, issue => issue.Code == "gpu-length");
}

[Fact(DisplayName = "FUNC-BD04: an ATX board does not fit an ITX-only case")]
public void FuncBd04_IncompatibleCaseFormFactor_IsReported()
{
var parts = Build(
    Part(ComponentType.Mainboard, new TechnicalSpec { FormFactor = "ATX" }),
    Part(ComponentType.Case, new TechnicalSpec { FormFactor = "Mini-ITX" }));

var issues = PcBuildCompatibilityChecker.Check(parts);

Assert.Contains(issues, issue => issue.Code == "case-form-factor");
}

[Fact(DisplayName = "FUNC-BD04: missing case GPU limit skips the length check")]
public void FuncBd04_MissingCaseGpuLimit_SkipsLengthCheck()
{
var parts = Build(
Part(ComponentType.GPU, new TechnicalSpec { LengthMm = 320 }),
Part(ComponentType.Case, new TechnicalSpec()));

var issues = PcBuildCompatibilityChecker.Check(parts);

Assert.DoesNotContain(issues, issue => issue.Code == "gpu-length");
}

[Fact(DisplayName = "FUNC-BD04: missing compatibility specifications skip unavailable checks")]
public void FuncBd04_MissingSpecifications_SkipUnavailableChecks()
{
var parts = Build(
    Part(ComponentType.CPU, new TechnicalSpec()),
    Part(ComponentType.Mainboard, new TechnicalSpec { Socket = "AM5" }),
    Part(ComponentType.GPU, new TechnicalSpec { LengthMm = 320 }),
    Part(ComponentType.Case, new TechnicalSpec()));

var issues = PcBuildCompatibilityChecker.Check(parts);

Assert.DoesNotContain(issues, issue => issue.Code is "socket" or "case-form-factor" or "gpu-length");
}

private static IReadOnlyDictionary<ComponentType, Product> Build(params Product[] products) =>
products.ToDictionary(product => product.Category!.ComponentType);

private static Product Part(ComponentType type, TechnicalSpec spec) => new()
{
Category = new Category { ComponentType = type },
TechnicalSpec = spec
};
}