using System.Globalization;
using System.Text;

namespace PCStore.API.Services.Faq;

/// <summary>Chuẩn hóa tiếng Việt và chấm điểm một câu FAQ với câu khách hỏi.</summary>
public static class FaqMatcher
{
    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "va", "hay", "hoac", "cho", "cua", "la", "mot", "cac", "nao", "nen", "vs", "voi",
        "trong", "khi", "de", "co", "khong", "duoc", "ban", "toi", "minh", "thi", "nhu",
        "the", "gi", "sao", "ma", "neu", "can", "muon", "hoi", "ve", "pc", "may",
    };

    public static string NormalizeVietnamese(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var lower = value.Trim().ToLowerInvariant().Replace('đ', 'd');
        var decomposed = lower.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(character))
                builder.Append(character);
            else if (builder.Length > 0 && builder[^1] != ' ')
                builder.Append(' ');
        }

        return builder.ToString().Trim();
    }

    public static bool ContainsPlaceholder(string? answer, string marker)
    {
        if (string.IsNullOrWhiteSpace(answer) || string.IsNullOrWhiteSpace(marker))
            return false;
        return answer.Contains(marker, StringComparison.Ordinal);
    }

    public static double Score(string normalizedUserQuestion, string normalizedFaqQuestion, string? keywords, double keywordWeight, double questionWeight)
    {
        if (normalizedUserQuestion.Length == 0)
            return 0;
        if (normalizedUserQuestion == normalizedFaqQuestion)
            return 1;

        var keywordScore = ScoreKeywords(normalizedUserQuestion, keywords);
        var questionScore = TokenJaccard(normalizedUserQuestion, normalizedFaqQuestion);
        var weightSum = keywordWeight + questionWeight;
        if (weightSum <= 0)
            return 0;
        return (keywordWeight * keywordScore + questionWeight * questionScore) / weightSum;
    }

    private static double ScoreKeywords(string normalizedUserQuestion, string? keywords)
    {
        var phrases = SplitKeywords(keywords);
        if (phrases.Count == 0)
            return 0;

        var userTokens = SplitTokens(normalizedUserQuestion)
            .Where(token => token.Length >= 3 && !Stopwords.Contains(token))
            .ToHashSet(StringComparer.Ordinal);
        var bestPhrase = 0d;
        foreach (var phrase in phrases)
        {
            var rawTokens = SplitTokens(phrase).Where(token => token.Length >= 2).ToList();
            var meaningful = rawTokens
                .Where(token => token.Length >= 3 && !Stopwords.Contains(token))
                .ToList();
            if (rawTokens.Count >= 2 && ContainsPhrase(normalizedUserQuestion, phrase))
                bestPhrase = 1d;
            else if (meaningful.Count == 1 && userTokens.Contains(meaningful[0]))
                bestPhrase = Math.Max(bestPhrase, 0.4d);
            if (bestPhrase >= 1d)
                break;
        }

        var keywordTokens = phrases
            .SelectMany(SplitTokens)
            .Where(token => token.Length >= 3 && !Stopwords.Contains(token))
            .ToHashSet(StringComparer.Ordinal);
        var overlap = userTokens.Count == 0 || keywordTokens.Count == 0
            ? 0
            : (double)userTokens.Count(keywordTokens.Contains) / userTokens.Count;
        if (userTokens.Count < 2)
            overlap = Math.Min(overlap, 0.4d);
        return Math.Max(bestPhrase, overlap);
    }

    private static bool ContainsPhrase(string normalizedUserQuestion, string phrase)
    {
        var userTokens = SplitTokens(normalizedUserQuestion).ToArray();
        var phraseTokens = SplitTokens(phrase).ToArray();
        if (phraseTokens.Length == 0 || phraseTokens.Length > userTokens.Length)
            return false;

        for (var start = 0; start <= userTokens.Length - phraseTokens.Length; start++)
        {
            var matches = true;
            for (var index = 0; index < phraseTokens.Length; index++)
            {
                if (!string.Equals(userTokens[start + index], phraseTokens[index], StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return true;
        }

        return false;
    }

    private static double TokenJaccard(string left, string right)
    {
        var leftTokens = SplitTokens(left).Where(token => token.Length >= 3 && !Stopwords.Contains(token)).ToHashSet(StringComparer.Ordinal);
        var rightTokens = SplitTokens(right).Where(token => token.Length >= 3 && !Stopwords.Contains(token)).ToHashSet(StringComparer.Ordinal);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
            return 0;
        var intersection = leftTokens.Count(rightTokens.Contains);
        var union = leftTokens.Count + rightTokens.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static List<string> SplitKeywords(string? keywords)
    {
        if (string.IsNullOrWhiteSpace(keywords))
            return [];
        return keywords
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeVietnamese)
            .Where(keyword => keyword.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<string> SplitTokens(string normalized) =>
        normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
