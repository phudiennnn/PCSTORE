using System.Text.Json;
using System.Text.RegularExpressions;
using PCStore.Core.Ai;

namespace PCStore.API.Services.Ai;

/// <summary>Bỏ rào markdown và dấu phẩy thừa, rồi trả về JSON parse được.</summary>
public sealed partial class AiJsonParser : IAiJsonParser
{
    public string? ParseAiJsonResponse(string? rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            return null;

        var text = rawResponse.Trim().TrimStart('\uFEFF');
        text = FenceRegex().Replace(text, string.Empty);
        var sliced = SliceFirstJsonValue(text);
        if (sliced is null)
            return null;

        var cleaned = RemoveTrailingCommas(sliced.Trim());
        try
        {
            using var _ = JsonDocument.Parse(cleaned);
            return cleaned;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string RemoveTrailingCommas(string json)
    {
        var current = json;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var next = TrailingCommaRegex().Replace(current, "$1");
            if (next == current)
                return next;
            current = next;
        }

        return current;
    }

    private static string? SliceFirstJsonValue(string text)
    {
        var objectStart = text.IndexOf('{');
        var arrayStart = text.IndexOf('[');
        if (objectStart < 0 && arrayStart < 0)
            return null;

        var start = objectStart < 0
            ? arrayStart
            : arrayStart < 0
                ? objectStart
                : Math.Min(objectStart, arrayStart);
        var closing = text[start] == '{' ? '}' : ']';
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var index = start; index < text.Length; index++)
        {
            var character = text[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (character == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (character == '"')
                    inString = false;
                continue;
            }

            if (character == '"')
            {
                inString = true;
                continue;
            }

            if (character == text[start])
                depth++;
            else if (character == closing)
            {
                depth--;
                if (depth == 0)
                    return text[start..(index + 1)];
            }
        }

        return null;
    }

    [GeneratedRegex(@"```(?:json)?", RegexOptions.IgnoreCase)]
    private static partial Regex FenceRegex();

    [GeneratedRegex(@",(\s*[}\]])")]
    private static partial Regex TrailingCommaRegex();
}
