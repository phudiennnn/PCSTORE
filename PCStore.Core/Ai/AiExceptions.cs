namespace PCStore.Core.Ai;

/// <summary>Lỗi của một nhà cung cấp. Message chỉ chứa câu tiếng Việt cho Admin.</summary>
public sealed class AiProviderException : Exception
{
    public AiProviderException(AiProviderErrorKind kind, string adminMessage)
        : base(adminMessage)
    {
        Kind = kind;
        AdminMessage = adminMessage;
    }

    public AiProviderErrorKind Kind { get; }
    public string AdminMessage { get; }

    public bool CountsTowardCircuit => Kind is AiProviderErrorKind.Timeout
        or AiProviderErrorKind.ConnectionFailed
        or AiProviderErrorKind.ServerError
        or AiProviderErrorKind.InvalidJson;
}

/// <summary>Cả Ollama và Gemini đều thất bại. Không kèm stack trace cho tầng trên.</summary>
public sealed class AllProvidersFailedException : Exception
{
    public AllProvidersFailedException(string summary, IReadOnlyList<string> providerErrors)
        : base(Join(summary, providerErrors))
    {
        ProviderErrors = providerErrors;
    }

    public IReadOnlyList<string> ProviderErrors { get; }

    public override string? StackTrace => null;

    public override string ToString() => Message;

    private static string Join(string summary, IReadOnlyList<string> providerErrors)
    {
        var details = string.Join(" ", providerErrors.Where(error => !string.IsNullOrWhiteSpace(error)));
        return string.IsNullOrWhiteSpace(details) ? summary : summary + " " + details;
    }
}

/// <summary>Luồng đã trả token rồi mới lỗi. Tầng trên không được đổi nhà cung cấp.</summary>
public sealed class AiStreamInterruptedException : Exception
{
    public AiStreamInterruptedException(string message)
        : base(message)
    {
    }

    public override string? StackTrace => null;

    public override string ToString() => Message;
}
