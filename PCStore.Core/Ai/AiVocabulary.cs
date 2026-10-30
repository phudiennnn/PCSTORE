namespace PCStore.Core.Ai;

/// <summary>Tên nhà cung cấp dùng chung giữa cấu hình, log và failover.</summary>
public static class AiProviderNames
{
    public const string Ollama = "Ollama";
    public const string Gemini = "Gemini";
}

/// <summary>Tên tác vụ ghi vào cột Intent của AiRequestLog.</summary>
public static class AiTaskNames
{
    public const string ExtractIntent = "extract_intent";
    public const string ExplainSelection = "explain_selection";
    public const string CustomerReply = "customer_reply";
    public const string WarmUp = "warm_up";
}

/// <summary>Giá trị hợp lệ của trường intent.</summary>
public static class CustomerIntent
{
    public const string BuildPc = "build_pc";
    public const string CompareParts = "compare_parts";
    public const string CheckCompatibility = "check_compatibility";
    public const string Faq = "faq";
    public const string OrderSupport = "order_support";
    public const string Other = "other";

    public static readonly string[] All =
    [
        BuildPc,
        CompareParts,
        CheckCompatibility,
        Faq,
        OrderSupport,
        Other,
    ];
}

/// <summary>Giá trị hợp lệ của trường purpose.</summary>
public static class PcPurpose
{
    public const string Gaming = "gaming";
    public const string Office = "office";
    public const string Design = "design";
    public const string Ai = "ai";
    public const string Streaming = "streaming";
    public const string Other = "other";

    public static readonly string[] All = [Gaming, Office, Design, Ai, Streaming, Other];
}

/// <summary>Nhóm lỗi để circuit breaker và thông báo Admin xử lý khác nhau.</summary>
public enum AiProviderErrorKind
{
    Timeout,
    ConnectionFailed,
    ServerError,
    BadRequest,
    Unauthorized,
    QuotaExceeded,
    Blocked,
    InvalidResponse,
    InvalidJson,
}
