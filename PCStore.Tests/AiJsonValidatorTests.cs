using PCStore.API.Services.Ai;

namespace PCStore.Tests;

public class AiJsonValidatorTests
{
    private readonly AiJsonValidator _validator = new();

    private const string ValidIntent =
        """{"intent":"build_pc","budget_vnd":15000000,"purpose":"gaming","cpu_brand":null,"gpu_brand":null,"need_monitor":false,"notes":null}""";

    [Fact]
    public void ValidateIntentJson_AcceptsSampleSchema()
    {
        var result = _validator.ValidateIntentJson(ValidIntent);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Intent);
        Assert.Equal("build_pc", result.Intent!.Intent);
        Assert.Equal(15000000, result.Intent.BudgetVnd);
        Assert.Equal("gaming", result.Intent.Purpose);
        Assert.False(result.Intent.NeedMonitor);
        Assert.Null(result.Intent.CpuBrand);
    }

    [Fact]
    public void ValidateIntentJson_AllowsNullPurpose()
    {
        const string json =
            """{"intent":"faq","budget_vnd":null,"purpose":null,"cpu_brand":"","gpu_brand":null,"need_monitor":false,"notes":"  "}""";

        var result = _validator.ValidateIntentJson(json);

        Assert.True(result.IsValid);
        Assert.Null(result.Intent!.Purpose);
        Assert.Null(result.Intent.CpuBrand);
        Assert.Null(result.Intent.Notes);
    }

    [Fact]
    public void ValidateIntentJson_RejectsUnknownIntentAndMissingMonitor()
    {
        var unknown = _validator.ValidateIntentJson(
            """{"intent":"Build_PC","budget_vnd":1,"purpose":"gaming","cpu_brand":null,"gpu_brand":null,"need_monitor":false,"notes":null}""");
        var missing = _validator.ValidateIntentJson(
            """{"intent":"other","budget_vnd":null,"purpose":null,"cpu_brand":null,"gpu_brand":null,"notes":null}""");
        var textBudget = _validator.ValidateIntentJson(
            """{"intent":"other","budget_vnd":"15000000","purpose":null,"cpu_brand":null,"gpu_brand":null,"need_monitor":true,"notes":null}""");

        Assert.False(unknown.IsValid);
        Assert.False(missing.IsValid);
        Assert.False(textBudget.IsValid);
        Assert.Contains(missing.Errors, error => error.Contains("need_monitor"));
    }
}
