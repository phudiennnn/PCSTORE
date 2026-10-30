namespace PCStore.Core.Entities;

/// <summary>Câu hỏi thường gặp dùng để trả lời trước khi gọi AI.</summary>
public class Faq
{
    public int Id { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Question { get; set; } = string.Empty;

    public string Answer { get; set; } = string.Empty;

    public string Keywords { get; set; } = string.Empty;

    public int Priority { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
