using System.Text.Json;
using PCStore.Core.Ai;

namespace PCStore.API.Services.Chat;

/// <summary>Thẻ sản phẩm gửi kèm câu trả lời để giao diện vẽ.</summary>
public sealed class ChatProductCard
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string? Brand { get; init; }
    public long PriceVnd { get; init; }
    public required string Role { get; init; }
    public string? Socket { get; init; }
    public string? RamType { get; init; }
    public int StockQuantity { get; init; }
}

/// <summary>Dữ liệu code đã chọn trước khi AI diễn giải.</summary>
public sealed class ChatGrounding
{
    public bool CallModel { get; init; }
    public string? FixedMessage { get; init; }
    public string PartsJson { get; init; } = "{}";
    public IReadOnlyList<ChatProductCard> Products { get; init; } = [];
}

/// <summary>Một sự kiện SSE của luồng chat.</summary>
public sealed class ChatStreamEvent
{
    public required string Name { get; init; }
    public required string Data { get; init; }

    public static ChatStreamEvent Create(string name, object payload) =>
        new()
        {
            Name = name,
            Data = JsonSerializer.Serialize(payload, ChatJson.Options),
        };
}

/// <summary>JSON camelCase dùng chung cho sự kiện chat.</summary>
public static class ChatJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

/// <summary>Lấy linh kiện từ database rồi kiểm tra bằng code.</summary>
public interface IChatCatalog
{
    Task<ChatGrounding> GroundAsync(IntentExtractionResult intent, string customerMessage, CancellationToken cancellationToken);
}
