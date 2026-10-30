using Microsoft.AspNetCore.Http.Features;

namespace PCStore.API.Services.Chat;

/// <summary>POST /api/chat/stream trả text/event-stream và dừng khi client ngắt.</summary>
public static class ChatStreamEndpoints
{
    public static void MapChatStream(this WebApplication app)
    {
        app.MapPost("/api/chat/stream", StreamAsync).RequireRateLimiting("chat-stream");
    }

    private static async Task StreamAsync(HttpContext httpContext, ChatPipelineService pipeline, CancellationToken cancellationToken)
    {
        ChatMessageRequest? request = null;
        try
        {
            request = await httpContext.Request.ReadFromJsonAsync<ChatMessageRequest>(cancellationToken);
        }
        catch (BadHttpRequestException)
        {
            request = null;
        }

        var bodyFeature = httpContext.Features.Get<IHttpResponseBodyFeature>();
        bodyFeature?.DisableBuffering();
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "text/event-stream; charset=utf-8";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Append("X-Accel-Buffering", "no");

        try
        {
            await foreach (var item in pipeline.HandleAsync(request?.Message, cancellationToken))
                await WriteEventAsync(httpContext, item, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await WriteEventAsync(
                httpContext,
                ChatStreamEvent.Create("error", new { message = "Trợ lý gặp lỗi khi đang trả lời." }),
                CancellationToken.None);
        }
    }

    private static async Task WriteEventAsync(HttpContext httpContext, ChatStreamEvent item, CancellationToken cancellationToken)
    {
        await httpContext.Response.WriteAsync($"event: {item.Name}\ndata: {item.Data}\n\n", cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);
    }

    private sealed class ChatMessageRequest
    {
        public string? Message { get; init; }
    }
}
