using PCStore.API.Services.Ai;

namespace PCStore.Tests;

public class AiJsonParserTests
{
    private readonly AiJsonParser _parser = new();

    [Fact]
    public void ParseAiJsonResponse_RemovesFenceAndTrailingComma()
    {
        const string raw = """
            Đây là kết quả:
            ```json
            {"intent":"faq","budget_vnd":null,"purpose":null,"cpu_brand":null,"gpu_brand":null,"need_monitor":false,"notes":null,}
            ```
            Cảm ơn.
            """;

        var json = _parser.ParseAiJsonResponse(raw);

        Assert.NotNull(json);
        Assert.Contains("\"intent\":\"faq\"", json);
        Assert.DoesNotContain("```", json);
        Assert.DoesNotContain(",}", json);
    }

    [Fact]
    public void ParseAiJsonResponse_ReturnsNullWhenNoObject()
    {
        Assert.Null(_parser.ParseAiJsonResponse("không có json"));
        Assert.Null(_parser.ParseAiJsonResponse("   "));
        Assert.Null(_parser.ParseAiJsonResponse(null));
    }

    [Fact]
    public void BuildIntentPrompt_KeepsVietnameseInputAndFixedRule()
    {
        var prompt = PromptTemplates.BuildIntentPrompt("Dựng PC chơi game khoảng 15 triệu");

        Assert.Equal("1.0.0", PromptTemplates.PromptVersion);
        Assert.Contains(PromptTemplates.JsonOnlyRule, prompt);
        Assert.EndsWith("Dựng PC chơi game khoảng 15 triệu", prompt);
    }

    [Fact]
    public void MaskPersonalData_HidesPhoneEmailAndAddress()
    {
        const string input = "Gọi 0905123456 hoặc a@b.com, địa chỉ: 12 đường Lê Lợi, quận Hải Châu.";
        var masked = PersonalDataMasker.MaskPersonalData(input, "[đã ẩn]");

        Assert.DoesNotContain("0905123456", masked);
        Assert.DoesNotContain("a@b.com", masked);
        Assert.DoesNotContain("Lê Lợi", masked);
        Assert.DoesNotContain("Hải Châu", masked);
        Assert.Contains("15 triệu", PersonalDataMasker.MaskPersonalData("ngân sách 15 triệu", "[đã ẩn]"));
    }
}
