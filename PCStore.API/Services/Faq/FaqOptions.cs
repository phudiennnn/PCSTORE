namespace PCStore.API.Services.Faq;

/// <summary>Ngưỡng FAQ, phân trang và phiên chống CSRF.</summary>
public sealed class FaqOptions
{
    public const string SectionName = "Faq";

    public double MinimumScore { get; set; } = 0.45;

    public double KeywordWeight { get; set; } = 0.7;

    public double QuestionWeight { get; set; } = 0.3;

    public int PageSize { get; set; } = 10;

    public int MaxPageSize { get; set; } = 50;

    public int MaxImportItems { get; set; } = 200;

    public int MaxCategoryLength { get; set; } = 80;

    public int MaxQuestionLength { get; set; } = 300;

    public int MaxAnswerLength { get; set; } = 2000;

    public int MaxAnswerWords { get; set; } = 80;

    public int MaxKeywordsLength { get; set; } = 800;

    public int MaxPriority { get; set; } = 1000;

    public string PlaceholderMarker { get; set; } = "[CẦN ĐIỀN]";

    public string UnfilledCustomerMessage { get; set; } = "Cửa hàng chưa công bố nội dung này. Bạn vui lòng hỏi nhân viên để biết thông tin chính xác.";

    public int CacheSeconds { get; set; } = 30;

    public string CsrfHeaderName { get; set; } = "X-CSRF-Token";

    public int CsrfLifetimeMinutes { get; set; } = 120;

    public string AccessTokenPurpose { get; set; } = "PCSTORE.API.AccessToken.v1";

    public string[] AllowedOrigins { get; set; } =
    [
        "http://localhost:3000",
        "http://127.0.0.1:3000",
        "http://localhost:5170",
        "http://127.0.0.1:5170",
    ];
}
