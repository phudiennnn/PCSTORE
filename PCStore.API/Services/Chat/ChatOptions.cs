namespace PCStore.API.Services.Chat;

/// <summary>Tham số luồng chat. Mọi giới hạn và câu mẫu đọc từ mục Chat.</summary>
public sealed class ChatOptions
{
    public const string SectionName = "Chat";

    public int MaxMessageCharacters { get; set; } = 4000;

    public int FaqChunkCharacters { get; set; } = 48;

    public int CandidateLimit { get; set; } = 20;

    public long DefaultBudgetVnd { get; set; } = 20_000_000;

    public long MinBudgetVnd { get; set; } = 1_000_000;

    public long MaxBudgetVnd { get; set; } = 500_000_000;

    public decimal BudgetCeilingRatio { get; set; } = 1.05m;

    public decimal MonitorShare { get; set; } = 0.12m;

    public int PowerReserveWatts { get; set; } = 50;

    public int DefaultRamWatts { get; set; } = 10;

    public int RateLimitPermitLimit { get; set; } = 20;

    public int RateLimitWindowSeconds { get; set; } = 60;

    public int SummaryLookbackHours { get; set; } = 24;

    public int PartSearchLimit { get; set; } = 20;

    public string OrderSupportMessage { get; set; } = "Để xem đơn, bạn mở mục Đơn hàng và nhập mã đơn. Cần thêm thì liên hệ cửa hàng theo số trên website.";

    public string OtherMessage { get; set; } = "Mình tư vấn dựng PC, so sánh linh kiện hoặc kiểm tra tương thích. Bạn nói ngân sách và mục đích sử dụng giúp mình.";

    public string MissingPartsMessage { get; set; } = "Mình chưa thấy đủ tên linh kiện trong câu hỏi. Bạn ghi rõ tên sản phẩm đang có.";

    public string NoBuildMessage { get; set; } = "Chưa ghép được cấu hình còn hàng, tương thích và nằm trong ngân sách. Bạn có thể tăng ngân sách hoặc bỏ bớt hãng.";

    public string ProviderFailedMessage { get; set; } = "Trợ lý chưa trả lời được lúc này. Bạn thử lại sau hoặc hỏi nhân viên cửa hàng.";

    public Dictionary<string, Dictionary<string, decimal>> PurposeBudgetShares { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
