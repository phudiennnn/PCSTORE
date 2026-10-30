using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using PCStore.Core.Ai;

namespace PCStore.API.Services.Ai;

/// <summary>Chọn Ollama trước, thử sửa JSON một lần, rồi chuyển Gemini theo circuit breaker.</summary>
public sealed class AiOrchestrator : IAiOrchestrator
{
    private readonly IReadOnlyList<IAiProvider> _providers;
    private readonly IAiSettingsService _settingsService;
    private readonly IAiRequestLogService _requestLogService;
    private readonly IPromptTemplateProvider _promptTemplateProvider;
    private readonly IAiJsonParser _jsonParser;
    private readonly IAiJsonValidator _jsonValidator;
    private readonly IAiClock _clock;
    private readonly ILogger<AiOrchestrator> _logger;
    private readonly object _circuitGate = new();
    private int _consecutiveOllamaFailures;
    private DateTimeOffset? _circuitOpenUntil;

    public AiOrchestrator(
        IEnumerable<IAiProvider> providers,
        IAiSettingsService settingsService,
        IAiRequestLogService requestLogService,
        IPromptTemplateProvider promptTemplateProvider,
        IAiJsonParser jsonParser,
        IAiJsonValidator jsonValidator,
        IAiClock clock,
        ILogger<AiOrchestrator> logger)
    {
        _providers = providers.ToList();
        _settingsService = settingsService;
        _requestLogService = requestLogService;
        _promptTemplateProvider = promptTemplateProvider;
        _jsonParser = jsonParser;
        _jsonValidator = jsonValidator;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IntentExtractionResult> ExtractIntentAsync(string customerMessage, CancellationToken cancellationToken)
    {
        var settings = await _settingsService.GetRuntimeSettingsAsync(cancellationToken);
        var message = customerMessage?.Trim() ?? string.Empty;
        if (message.Length == 0)
            throw new ArgumentException("Nội dung khách hàng đang trống.");
        if (message.Length > settings.ExtractIntent.MaxCustomerMessageCharacters)
            throw new ArgumentException($"Nội dung khách hàng vượt quá {settings.ExtractIntent.MaxCustomerMessageCharacters} ký tự.");

        var request = new AiGenerationRequest
        {
            Prompt = _promptTemplateProvider.BuildIntentPrompt(message),
            TaskName = AiTaskNames.ExtractIntent,
            ExpectJson = true,
            JsonSchema = _promptTemplateProvider.BuildIntentJsonSchema(),
            Temperature = settings.ExtractIntent.Temperature,
            MaxOutputTokens = settings.ExtractIntent.NumPredict,
        };
        var errors = new List<string>();
        var skipReason = GetOllamaSkipReason(settings);
        string? failoverReason = skipReason;
        if (skipReason is null)
        {
            var ollama = RequireProvider(AiProviderNames.Ollama);
            var attempt = await TryExtractAsync(ollama, request, settings, cancellationToken);
            if (attempt.Result is not null)
            {
                RecordOllamaSuccess();
                await LogAsync(AiProviderNames.Ollama, request.TaskName, attempt.LatencyMilliseconds, true, null);
                return attempt.Result;
            }

            failoverReason = attempt.Error?.AdminMessage ?? "Ollama không trả về JSON hợp lệ.";
            errors.Add(failoverReason);
            if (attempt.Error?.CountsTowardCircuit == true)
                RecordOllamaFailure(settings);
            _logger.LogWarning("Ollama không trích được ý định: {Message}", failoverReason);
            await LogAsync(AiProviderNames.Ollama, request.TaskName, attempt.LatencyMilliseconds, false, failoverReason);
        }
        else
        {
            errors.Add(skipReason);
            _logger.LogWarning("Bỏ qua Ollama: {Message}", skipReason);
        }

        if (!settings.Gemini.IsEnabled)
        {
            errors.Add("Gemini đang tắt.");
            throw Failure(errors);
        }

        var gemini = RequireProvider(AiProviderNames.Gemini);
        var geminiAttempt = await TryExtractAsync(gemini, request, settings, cancellationToken);
        if (geminiAttempt.Result is not null)
        {
            await LogAsync(AiProviderNames.Gemini, request.TaskName, geminiAttempt.LatencyMilliseconds, true, failoverReason);
            return geminiAttempt.Result;
        }

        var geminiReason = geminiAttempt.Error?.AdminMessage ?? "Gemini không trả về JSON hợp lệ.";
        errors.Add(geminiReason);
        _logger.LogWarning("Gemini không trích được ý định: {Message}", geminiReason);
        await LogAsync(AiProviderNames.Gemini, request.TaskName, geminiAttempt.LatencyMilliseconds, false, geminiReason);
        throw Failure(errors);
    }

    public async Task<string> GenerateCustomerReplyAsync(string customerMessage, CancellationToken cancellationToken)
    {
        var settings = await _settingsService.GetRuntimeSettingsAsync(cancellationToken);
        var message = customerMessage?.Trim() ?? string.Empty;
        if (message.Length == 0)
            throw new ArgumentException("Nội dung khách hàng đang trống.");
        if (message.Length > settings.ExtractIntent.MaxCustomerMessageCharacters)
            throw new ArgumentException($"Nội dung khách hàng vượt quá {settings.ExtractIntent.MaxCustomerMessageCharacters} ký tự.");

        var request = new AiGenerationRequest
        {
            Prompt = _promptTemplateProvider.BuildCustomerReplyPrompt(message),
            TaskName = AiTaskNames.CustomerReply,
            ExpectJson = false,
            Temperature = settings.ExplainSelection.Temperature,
            MaxOutputTokens = settings.ExplainSelection.MaxOutputTokens,
        };
        var reply = new StringBuilder();
        await foreach (var chunk in StreamRequestAsync(request, cancellationToken))
            reply.Append(chunk);
        var text = reply.ToString().Trim();
        if (text.Length == 0)
            throw new AllProvidersFailedException("Không có nhà cung cấp AI nào xử lý được yêu cầu.", ["AI không trả về nội dung."]);
        return text;
    }

    public async IAsyncEnumerable<string> ExplainSelectionAsync(
        string selectedPartsJson,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var settings = await _settingsService.GetRuntimeSettingsAsync(cancellationToken);
        var parts = selectedPartsJson?.Trim() ?? string.Empty;
        if (parts.Length == 0)
            throw new ArgumentException("Danh sách linh kiện đã chọn đang trống.");

        var request = new AiGenerationRequest
        {
            Prompt = _promptTemplateProvider.BuildExplanationPrompt(parts),
            TaskName = AiTaskNames.ExplainSelection,
            ExpectJson = false,
            Temperature = settings.ExplainSelection.Temperature,
            MaxOutputTokens = settings.ExplainSelection.MaxOutputTokens,
        };
        await foreach (var chunk in StreamRequestAsync(request, cancellationToken))
            yield return chunk;
    }

    private async IAsyncEnumerable<string> StreamRequestAsync(
        AiGenerationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var settings = await _settingsService.GetRuntimeSettingsAsync(cancellationToken);
        var errors = new List<string>();
        var skipReason = GetOllamaSkipReason(settings);
        string? failoverReason = skipReason;
        if (skipReason is null)
        {
            var streamed = false;
            await foreach (var chunk in ReadProviderAsync(
                RequireProvider(AiProviderNames.Ollama),
                request,
                settings,
                countCircuit: true,
                failoverReason: null,
                cancellationToken))
            {
                if (chunk.Kind == StreamChunkKind.ErrorBeforeToken)
                {
                    failoverReason = chunk.Reason;
                    errors.Add(chunk.Reason ?? "Ollama không trả về nội dung.");
                    break;
                }

                if (chunk.Kind == StreamChunkKind.ErrorAfterToken)
                    throw new AiStreamInterruptedException("Câu trả lời bị gián đoạn sau khi đã bắt đầu. Vui lòng thử lại.");

                streamed = true;
                yield return chunk.Text ?? string.Empty;
            }

            if (streamed)
                yield break;
        }
        else
        {
            errors.Add(skipReason);
        }

        if (!settings.Gemini.IsEnabled)
        {
            errors.Add("Gemini đang tắt.");
            throw Failure(errors);
        }

        var geminiStreamed = false;
        await foreach (var chunk in ReadProviderAsync(
            RequireProvider(AiProviderNames.Gemini),
            request,
            settings,
            countCircuit: false,
            failoverReason,
            cancellationToken))
        {
            if (chunk.Kind == StreamChunkKind.ErrorBeforeToken)
            {
                errors.Add(chunk.Reason ?? "Gemini không trả về nội dung.");
                break;
            }

            if (chunk.Kind == StreamChunkKind.ErrorAfterToken)
                throw new AiStreamInterruptedException("Câu trả lời bị gián đoạn sau khi đã bắt đầu. Vui lòng thử lại.");

            geminiStreamed = true;
            yield return chunk.Text ?? string.Empty;
        }

        if (!geminiStreamed)
            throw Failure(errors);
    }

    private async IAsyncEnumerable<StreamChunk> ReadProviderAsync(
        IAiProvider provider,
        AiGenerationRequest request,
        AiRuntimeSettings settings,
        bool countCircuit,
        string? failoverReason,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var producedToken = false;
        await using var enumerator = provider.StreamTextAsync(request, cancellationToken).GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            var moved = false;
            var text = string.Empty;
            Exception? error = null;
            try
            {
                moved = await enumerator.MoveNextAsync();
                if (moved)
                    text = enumerator.Current ?? string.Empty;
            }
            catch (Exception exception) when (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                error = exception;
            }

            if (error is not null)
            {
                var reason = Describe(error, provider.ProviderName);
                var counts = error is AiProviderException providerError && providerError.CountsTowardCircuit;
                if (producedToken)
                {
                    if (countCircuit && counts)
                        RecordOllamaFailure(settings);
                    await LogAsync(provider.ProviderName, request.TaskName, started.ElapsedMilliseconds, false, reason);
                    yield return StreamChunk.AfterToken(reason);
                    yield break;
                }

                if (countCircuit && counts)
                    RecordOllamaFailure(settings);
                _logger.LogWarning("{Provider} lỗi trước token đầu: {Message}", provider.ProviderName, reason);
                await LogAsync(provider.ProviderName, request.TaskName, started.ElapsedMilliseconds, false, reason);
                yield return StreamChunk.BeforeToken(reason);
                yield break;
            }

            if (!moved)
            {
                if (!producedToken)
                {
                    var reason = $"{provider.ProviderName} không trả về nội dung.";
                    await LogAsync(provider.ProviderName, request.TaskName, started.ElapsedMilliseconds, false, reason);
                    yield return StreamChunk.BeforeToken(reason);
                    yield break;
                }

                if (countCircuit)
                    RecordOllamaSuccess();
                await LogAsync(provider.ProviderName, request.TaskName, started.ElapsedMilliseconds, true, failoverReason);
                yield break;
            }

            if (text.Length == 0)
                continue;

            producedToken = true;
            yield return StreamChunk.Token(text);
        }
    }

    private async Task<ProviderAttempt> TryExtractAsync(
        IAiProvider provider,
        AiGenerationRequest request,
        AiRuntimeSettings settings,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        try
        {
            var raw = await provider.GenerateJsonAsync(request, cancellationToken);
            var parsed = Accept(provider.ProviderName, raw);
            if (parsed is not null)
                return ProviderAttempt.Ok(parsed, started.ElapsedMilliseconds);

            var repairSource = raw ?? string.Empty;
            if (repairSource.Length > settings.ExtractIntent.MaxRepairInputCharacters)
                repairSource = repairSource[..settings.ExtractIntent.MaxRepairInputCharacters];
            var repaired = await provider.GenerateJsonAsync(new AiGenerationRequest
            {
                Prompt = _promptTemplateProvider.BuildIntentRepairPrompt(repairSource),
                TaskName = request.TaskName,
                ExpectJson = true,
                JsonSchema = request.JsonSchema,
                Temperature = request.Temperature,
                MaxOutputTokens = request.MaxOutputTokens,
            }, cancellationToken);
            parsed = Accept(provider.ProviderName, repaired);
            if (parsed is not null)
                return ProviderAttempt.Ok(parsed, started.ElapsedMilliseconds);

            return ProviderAttempt.Fail(
                new AiProviderException(AiProviderErrorKind.InvalidJson, $"{provider.ProviderName} trả về JSON không hợp lệ sau khi thử sửa."),
                started.ElapsedMilliseconds);
        }
        catch (Exception exception) when (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var mapped = exception as AiProviderException
                ?? AiHttpErrorMapper.MapTransportFailure(exception, provider.ProviderName, cancellationToken);
            if (mapped is OperationCanceledException)
                throw;
            var providerError = mapped as AiProviderException
                ?? new AiProviderException(AiProviderErrorKind.InvalidResponse, $"{provider.ProviderName} không xử lý được yêu cầu.");
            return ProviderAttempt.Fail(providerError, started.ElapsedMilliseconds);
        }
    }

    private IntentExtractionResult? Accept(string providerName, string? raw)
    {
        var json = _jsonParser.ParseAiJsonResponse(raw);
        if (json is null)
            return null;
        var validation = _jsonValidator.ValidateIntentJson(json);
        if (!validation.IsValid || validation.Intent is null)
            return null;
        var intent = validation.Intent;
        return new IntentExtractionResult
        {
            Intent = intent.Intent,
            BudgetVnd = intent.BudgetVnd,
            Purpose = intent.Purpose,
            CpuBrand = intent.CpuBrand,
            GpuBrand = intent.GpuBrand,
            NeedMonitor = intent.NeedMonitor,
            Notes = intent.Notes,
            ProviderName = providerName,
            PromptVersion = _promptTemplateProvider.PromptVersion,
        };
    }

    private string? GetOllamaSkipReason(AiRuntimeSettings settings)
    {
        if (settings.Ollama.IsMaintenanceMode)
            return "Ollama đang bảo trì.";
        if (IsCircuitOpen())
            return "Circuit breaker đang mở.";
        return null;
    }

    private bool IsCircuitOpen()
    {
        lock (_circuitGate)
            return _circuitOpenUntil is not null && _clock.UtcNow < _circuitOpenUntil.Value;
    }

    private void RecordOllamaSuccess()
    {
        lock (_circuitGate)
        {
            _consecutiveOllamaFailures = 0;
            _circuitOpenUntil = null;
        }
    }

    private void RecordOllamaFailure(AiRuntimeSettings settings)
    {
        lock (_circuitGate)
        {
            _consecutiveOllamaFailures++;
            if (_consecutiveOllamaFailures >= Math.Max(1, settings.CircuitFailureThreshold))
                _circuitOpenUntil = _clock.UtcNow.AddSeconds(Math.Max(1, settings.CircuitOpenSeconds));
        }
    }

    private IAiProvider RequireProvider(string providerName) =>
        _providers.FirstOrDefault(provider => provider.ProviderName == providerName)
        ?? throw new InvalidOperationException($"Chưa đăng ký nhà cung cấp {providerName}.");

    private static string Describe(Exception exception, string providerName)
    {
        if (exception is AiProviderException provider)
            return provider.AdminMessage;
        if (exception is AiStreamInterruptedException interrupted)
            return interrupted.Message;
        return $"{providerName} không xử lý được yêu cầu.";
    }

    private static AllProvidersFailedException Failure(IReadOnlyList<string> errors) =>
        new("Không có nhà cung cấp AI nào xử lý được yêu cầu.", errors);

    private async Task LogAsync(string provider, string intent, long latencyMilliseconds, bool isSuccess, string? failoverReason)
    {
        try
        {
            await _requestLogService.WriteAsync(new AiRequestLogEntry
            {
                Provider = provider,
                Intent = intent,
                LatencyMilliseconds = (int)Math.Min(int.MaxValue, Math.Max(0, latencyMilliseconds)),
                IsSuccess = isSuccess,
                FailoverReason = failoverReason,
            }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Không ghi được nhật ký AI: {Message}", exception.Message);
        }
    }

    private sealed class ProviderAttempt
    {
        public IntentExtractionResult? Result { get; private init; }

        public AiProviderException? Error { get; private init; }

        public int LatencyMilliseconds { get; private init; }

        public static ProviderAttempt Ok(IntentExtractionResult result, long latency) =>
            new() { Result = result, LatencyMilliseconds = (int)Math.Min(int.MaxValue, latency) };

        public static ProviderAttempt Fail(AiProviderException error, long latency) =>
            new() { Error = error, LatencyMilliseconds = (int)Math.Min(int.MaxValue, latency) };
    }

    private enum StreamChunkKind
    {
        Token,
        ErrorBeforeToken,
        ErrorAfterToken,
    }

    private readonly record struct StreamChunk(StreamChunkKind Kind, string? Text, string? Reason)
    {
        public static StreamChunk Token(string text) => new(StreamChunkKind.Token, text, null);

        public static StreamChunk BeforeToken(string reason) => new(StreamChunkKind.ErrorBeforeToken, null, reason);

        public static StreamChunk AfterToken(string reason) => new(StreamChunkKind.ErrorAfterToken, null, reason);
    }
}
