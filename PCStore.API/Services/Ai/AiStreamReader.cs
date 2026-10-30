namespace PCStore.API.Services.Ai;

/// <summary>Đọc một bước của luồng mà không yield bên trong catch.</summary>
internal static class AiStreamReader
{
    public static async Task<AiStreamStep> MoveNextAsync(
        IAsyncEnumerator<string> enumerator,
        CancellationToken cancellationToken)
    {
        try
        {
            var moved = await enumerator.MoveNextAsync();
            return new AiStreamStep(moved, moved ? enumerator.Current ?? string.Empty : string.Empty, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new AiStreamStep(false, string.Empty, exception);
        }
    }
}

/// <summary>Một bước đọc từ IAsyncEnumerable.</summary>
internal readonly record struct AiStreamStep(bool Moved, string Chunk, Exception? Error)
{
    public bool Failed => Error is not null;
}
