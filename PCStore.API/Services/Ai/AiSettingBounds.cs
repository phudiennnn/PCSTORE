namespace PCStore.API.Services.Ai;

/// <summary>Khung giá trị hợp lệ khi ghi cấu hình AI.</summary>
internal static class AiSettingBounds
{
    public const int MinTimeoutSeconds = 1;
    public const int MaxTimeoutSeconds = 300;
    public const int MinConnectTimeoutSeconds = 1;
    public const int MaxConnectTimeoutSeconds = 30;
    public const int MinNumCtx = 256;
    public const int MaxNumCtx = 131072;
    public const int MinOutputTokens = 1;
    public const int MaxOutputTokens = 8192;
    public const int MaxFallbackModels = 8;
    public const int MaxUrlLength = 300;
    public const int MaxModelNameLength = 120;
    public const int MaxIntentLength = 40;
    public const int MaxFailoverReasonLength = 300;
    public const decimal MinTemperature = 0m;
    public const decimal MaxTemperature = 2m;
}
