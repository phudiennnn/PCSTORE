using System.Text.RegularExpressions;

namespace PCStore.API.Services.Ai;

/// <summary>Che số điện thoại, email và địa chỉ trước khi gửi dữ liệu ra ngoài máy.</summary>
public static partial class PersonalDataMasker
{
    public static string MaskPersonalData(string? input, string replacement)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var token = string.IsNullOrWhiteSpace(replacement) ? "[đã ẩn]" : replacement;
        var masked = EmailRegex().Replace(input, token);
        masked = PhoneRegex().Replace(masked, token);
        masked = LabeledAddressRegex().Replace(masked, token);
        masked = StreetAddressRegex().Replace(masked, token);
        masked = AdminAreaRegex().Replace(masked, token);
        return masked;
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?<!\d)(?:\+84|0)(?:[\s.\-]?\d){8,10}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"(?i)(?:địa chỉ|dia chi|address)\s*[:：]\s*[^\n]{5,160}")]
    private static partial Regex LabeledAddressRegex();

    [GeneratedRegex(@"(?i)\b\d{1,5}(?:/\d{1,5})?\s+(?:đường|duong|phố|pho|ngõ|ngo|hẻm|hem|street)\s+[^\n,.]{2,80}")]
    private static partial Regex StreetAddressRegex();

    [GeneratedRegex(@"(?i)\b(?:phường|phuong|quận|quan|huyện|huyen|thành phố|thanh pho|tỉnh|tinh)\s+[^\n,.]{2,60}")]
    private static partial Regex AdminAreaRegex();
}
