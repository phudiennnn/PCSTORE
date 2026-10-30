using PCStore.Core.Ai;

namespace PCStore.API.Services.Ai;

/// <summary>Đổi mã HTTP và lỗi mạng thành thông điệp tiếng Việt cho Admin.</summary>
internal static class AiHttpErrorMapper
{
    public static AiProviderException FromStatusCode(string providerName, int statusCode, bool usesApiKey)
    {
        if (statusCode is 401 or 403)
        {
            var message = usesApiKey
                ? $"{providerName} từ chối API Key (HTTP {statusCode}). Hãy kiểm tra key trong cấu hình."
                : $"{providerName} từ chối kết nối (HTTP {statusCode}).";
            return new AiProviderException(AiProviderErrorKind.Unauthorized, message);
        }

        if (statusCode == 429)
        {
            var message = providerName == AiProviderNames.Gemini
                ? "Gemini đã hết hạn mức miễn phí (HTTP 429). Hãy thử lại sau hoặc kiểm tra quota."
                : $"{providerName} đang bị giới hạn yêu cầu (HTTP 429).";
            return new AiProviderException(AiProviderErrorKind.QuotaExceeded, message);
        }

        if (statusCode == 400)
            return new AiProviderException(
                AiProviderErrorKind.BadRequest,
                $"{providerName} từ chối yêu cầu (HTTP 400). Có thể model hoặc tham số không được phiên bản này hỗ trợ.");

        if (statusCode == 404)
            return new AiProviderException(
                AiProviderErrorKind.InvalidResponse,
                $"{providerName} không tìm thấy model hoặc đường dẫn (HTTP 404).");

        if (statusCode == 408)
            return new AiProviderException(AiProviderErrorKind.Timeout, $"{providerName} hết thời gian chờ (HTTP 408).");

        if (statusCode >= 500)
            return new AiProviderException(AiProviderErrorKind.ServerError, $"{providerName} gặp lỗi máy chủ (HTTP {statusCode}).");

        return new AiProviderException(AiProviderErrorKind.InvalidResponse, $"{providerName} trả về HTTP {statusCode}.");
    }

    public static Exception MapTransportFailure(Exception exception, string providerName, CancellationToken callerToken)
    {
        if (exception is AiProviderException || exception is AiStreamInterruptedException)
            return exception;

        if (exception is OperationCanceledException && callerToken.IsCancellationRequested)
            return exception;

        if (exception is OperationCanceledException or TimeoutException)
            return new AiProviderException(AiProviderErrorKind.Timeout, $"{providerName} hết thời gian chờ.");

        if (exception is HttpRequestException)
            return new AiProviderException(AiProviderErrorKind.ConnectionFailed, $"Không kết nối được tới {providerName}.");

        return new AiProviderException(AiProviderErrorKind.InvalidResponse, $"{providerName} trả về dữ liệu không đọc được.");
    }
}
