using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace PCStore.API.Services.Chat;

/// <summary>Đăng ký pipeline chat và giới hạn số lần gọi theo IP.</summary>
public static class ChatServiceCollectionExtensions
{
    public static IServiceCollection AddPcStoreChat(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ChatOptions>(configuration.GetSection(ChatOptions.SectionName));
        services.AddScoped<IChatCatalog, ChatCatalog>();
        services.AddScoped<ChatPipelineService>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { message = "Bạn gửi hơi nhanh. Hãy đợi một lát rồi thử lại." },
                    token);
            };
            options.AddPolicy("chat-stream", httpContext =>
            {
                var chat = httpContext.RequestServices.GetRequiredService<IOptionsMonitor<ChatOptions>>().CurrentValue;
                var permit = Math.Max(1, chat.RateLimitPermitLimit);
                var windowSeconds = Math.Max(1, chat.RateLimitWindowSeconds);
                var address = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(
                    address,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permit,
                        Window = TimeSpan.FromSeconds(windowSeconds),
                        QueueLimit = 0,
                    });
            });
        });
        return services;
    }
}
