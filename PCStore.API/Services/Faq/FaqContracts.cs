namespace PCStore.API.Services.Faq;

/// <summary>FAQ đã vượt ngưỡng và được phép trả cho khách.</summary>
public sealed class FaqMatch
{
    public int Id { get; init; }

    public required string Category { get; init; }

    public required string Question { get; init; }

    public required string Answer { get; init; }

    public double Score { get; init; }
}

/// <summary>Giá trị Admin ghi cho một câu FAQ.</summary>
public sealed class FaqWriteModel
{
    public string? Category { get; init; }

    public string? Question { get; init; }

    public string? Answer { get; init; }

    public string? Keywords { get; init; }

    public int Priority { get; init; }

    public bool IsActive { get; init; }
}

/// <summary>Tìm FAQ và xóa bộ nhớ đệm khi Admin lưu.</summary>
public interface IFaqService
{
    Task<FaqMatch?> FindMatchingFaqAsync(string userQuestion, CancellationToken cancellationToken);

    Task<string?> FindUnfilledNoticeAsync(string userQuestion, CancellationToken cancellationToken);

    Task<FaqMatch?> FindNearestFaqAsync(string userQuestion, CancellationToken cancellationToken);

    void InvalidateCache();
}
