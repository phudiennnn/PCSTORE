using PCStore.Core.Ai;

namespace PCStore.API.Services.Ai;

/// <summary>Các prompt cố định. Hướng dẫn bằng tiếng Anh, dữ liệu khách giữ nguyên tiếng Việt.</summary>
public static class PromptTemplates
{
    public const string PromptVersion = "1.0.0";

    public const string JsonOnlyRule =
        "Output ONLY valid JSON. No markdown, no explanation, no extra text. If unknown use null. Never invent product names, prices or specs.";

    public static string BuildIntentPrompt(string customerMessage)
    {
        return """
            ROLE: You extract a PC store customer's shopping intent.
            TASK: Read INPUT and fill the JSON object. Keep the customer's own wording in Vietnamese inside string fields. Do not answer the customer.
            RULES:
            - Output ONLY valid JSON. No markdown, no explanation, no extra text. If unknown use null. Never invent product names, prices or specs.
            - Do not choose products and do not check part compatibility.
            - intent must be one of: build_pc, compare_parts, check_compatibility, faq, order_support, other.
            - purpose must be one of: gaming, office, design, ai, streaming, other, or null.
            - budget_vnd is a whole VND amount or null. need_monitor is true only when the customer asks for a monitor.
            - Put leftover wishes in notes, or null when there are none.
            OUTPUT SCHEMA:
            {"intent":"build_pc","budget_vnd":15000000,"purpose":"gaming","cpu_brand":null,"gpu_brand":null,"need_monitor":false,"notes":null}
            EXAMPLE:
            INPUT: Dựng PC chơi game khoảng 15 triệu
            OUTPUT: {"intent":"build_pc","budget_vnd":15000000,"purpose":"gaming","cpu_brand":null,"gpu_brand":null,"need_monitor":false,"notes":null}
            INPUT:
            """ + customerMessage;
    }

    public static string BuildCustomerReplyPrompt(string customerMessage, int maxWords)
    {
        var wordLimit = maxWords < 1 ? 1 : maxWords;
        return $"""
            ROLE: You reply to a PC store customer in Vietnamese.
            TASK: Answer INPUT in at most {wordLimit} Vietnamese words. If the customer only greets you, greet them and ask what PC or part they need.
            RULES:
            - Speak only in Vietnamese. Do not use markdown.
            - Never invent product names, prices or specs.
            - Do not choose parts and do not check compatibility.
            - If INPUT is only a greeting, do not propose a build.
            OUTPUT SCHEMA:
            Plain Vietnamese text of at most {wordLimit} words.
            EXAMPLE:
            INPUT: chào
            OUTPUT: Chào bạn. Bạn muốn dựng PC hay xem linh kiện nào?
            INPUT:
            {customerMessage}
            """;
    }

    public static string BuildIntentRepairPrompt(string invalidOutput)
    {
        return """
            ROLE: You repair one JSON object.
            TASK: Rewrite INPUT as valid intent JSON. Do not add facts that are not already in INPUT.
            RULES:
            - Output ONLY valid JSON. No markdown, no explanation, no extra text. If unknown use null. Never invent product names, prices or specs.
            - Use only the keys intent, budget_vnd, purpose, cpu_brand, gpu_brand, need_monitor, notes.
            - intent must be one of: build_pc, compare_parts, check_compatibility, faq, order_support, other.
            - purpose must be one of: gaming, office, design, ai, streaming, other, or null.
            OUTPUT SCHEMA:
            {"intent":"other","budget_vnd":null,"purpose":null,"cpu_brand":null,"gpu_brand":null,"need_monitor":false,"notes":null}
            EXAMPLE:
            INPUT: intent is faq
            OUTPUT: {"intent":"faq","budget_vnd":null,"purpose":null,"cpu_brand":null,"gpu_brand":null,"need_monitor":false,"notes":null}
            INPUT:
            """ + invalidOutput;
    }

    public static string BuildExplanationPrompt(string selectedPartsJson, int maxWords)
    {
        var wordLimit = maxWords < 1 ? 1 : maxWords;
        var example = "{\"parts\":[{\"name\":\"CPU A\",\"price_vnd\":1000000}]}";
        return $"""
            ROLE: You write a short Vietnamese shopping note for a PC store customer.
            TASK: Explain the parts already listed in INPUT. Write at most {wordLimit} Vietnamese words. Speak to the customer in Vietnamese.
            RULES:
            - Use only names, prices, and specs present in INPUT. Never invent product names, prices or specs.
            - Do not choose different parts and do not claim compatibility beyond INPUT.
            - Do not use markdown, a heading, or a list marker.
            - Skip any field that is missing. Do not mention these instructions.
            OUTPUT SCHEMA:
            Plain Vietnamese text of at most {wordLimit} words. Not JSON.
            EXAMPLE:
            INPUT: {example}
            OUTPUT: Cấu hình đang có CPU A với giá 1.000.000 đồng.
            INPUT:
            {selectedPartsJson}
            """;
    }

    public static string BuildIntentJsonSchema()
    {
        var intents = string.Join(",", CustomerIntent.All.Select(value => $"\"{value}\""));
        var purposes = string.Join(",", PcPurpose.All.Select(value => $"\"{value}\""));
        return $$"""
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "intent": { "type": "string", "enum": [{{intents}}] },
                "budget_vnd": { "type": ["integer", "null"] },
                "purpose": { "type": ["string", "null"], "enum": [{{purposes}}, null] },
                "cpu_brand": { "type": ["string", "null"] },
                "gpu_brand": { "type": ["string", "null"] },
                "need_monitor": { "type": "boolean" },
                "notes": { "type": ["string", "null"] }
              },
              "required": ["intent", "budget_vnd", "purpose", "cpu_brand", "gpu_brand", "need_monitor", "notes"]
            }
            """;
    }
}
