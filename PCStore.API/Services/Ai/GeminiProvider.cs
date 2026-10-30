using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PCStore.Core.Ai;

namespace PCStore.API.Services.Ai;

/// <summary>Gọi Gemini bằng văn bản thuần, tự hạ payload và đổi model khi API từ chối.</summary>
public sealed class GeminiProvider : IAiProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAiSettingsService _settingsService;
    private readonly IOptionsMonitor<AiOptions> _options;
    private readonly IAiClock _clock;
    private readonly ILogger<GeminiProvider> _logger;

    public GeminiProvider(
        IHttpClientFactory httpClientFactory,
        IAiSettingsService settingsService,
        IOptionsMonitor<AiOptions> options,
        IAiClock clock,
        ILogger<GeminiProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settingsService = settingsService;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public string ProviderName => AiProviderNames.Gemini;

    public async Task<string> GenerateJsonAsync(AiGenerationRequest request, CancellationToken cancellationToken)
    {
        var settings = (await _settingsService.GetRuntimeSettingsAsync(cancellationToken)).Gemini;
        EnsureReady(settings);
        var prompt = Mask(request.Prompt);
        AiProviderException? lastError = null;
        foreach (var model in ListModels(settings))
        {
            try
            {
                return await GenerateWithModelAsync(settings, model, prompt, request, cancellationToken);
            }
            catch (AiProviderException exception) when (!IsTerminal(exception))
            {
                lastError = exception;
                _logger.LogWarning("Gemini model {Model} không xử lý được yêu cầu: {Message}", model, exception.AdminMessage);
            }
        }

        throw lastError ?? new AiProviderException(AiProviderErrorKind.InvalidResponse, "Gemini không trả về nội dung.");
    }

    public async IAsyncEnumerable<string> StreamTextAsync(
        AiGenerationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var settings = (await _settingsService.GetRuntimeSettingsAsync(cancellationToken)).Gemini;
        EnsureReady(settings);
        var prompt = Mask(request.Prompt);
        AiProviderException? lastError = null;
        foreach (var model in ListModels(settings))
        {
            var opened = await OpenStreamAsync(settings, model, prompt, request, cancellationToken);
            await using (opened)
            {
                if (opened.Error is not null)
                {
                    if (opened.StopTryingModels)
                        throw opened.Error;
                    lastError = opened.Error;
                    _logger.LogWarning("Gemini stream model {Model} lỗi: {Message}", model, opened.Error.AdminMessage);
                    continue;
                }

                if (opened.SingleChunk is not null)
                {
                    if (opened.SingleChunk.Length > 0)
                        yield return opened.SingleChunk;
                    yield break;
                }

                if (!string.IsNullOrEmpty(opened.FirstChunk))
                    yield return opened.FirstChunk;

                if (opened.Reader is null)
                    yield break;

                while (true)
                {
                    var line = await ReadLineOrInterruptAsync(opened, cancellationToken);
                    if (line is null)
                        yield break;
                    var chunk = ReadChunkOrInterrupt(line);
                    if (!string.IsNullOrEmpty(chunk))
                        yield return chunk;
                }
            }
        }

        throw lastError ?? new AiProviderException(AiProviderErrorKind.InvalidResponse, "Gemini không trả về nội dung.");
    }

    public async Task<AiHealthResult> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var settings = (await _settingsService.GetRuntimeSettingsAsync(cancellationToken)).Gemini;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return CreateHealth(
                false,
                settings.ApiKeyUnreadable
                    ? "Không đọc được API Key Gemini đã lưu. Hãy nhập lại key."
                    : "Chưa cấu hình API Key Gemini.",
                null);
        }

        var started = Stopwatch.StartNew();
        try
        {
            using var timeout = StartTimeout(_options.CurrentValue.Gemini.HealthTimeoutSeconds, cancellationToken);
            var client = CreateClient();
            using var message = new HttpRequestMessage(HttpMethod.Get, BuildModelUrl(settings, settings.ModelName, string.Empty, null));
            message.Headers.TryAddWithoutValidation(HeaderName(), settings.ApiKey);
            using var response = await client.SendAsync(message, timeout.Token);
            started.Stop();
            if (response.IsSuccessStatusCode)
                return CreateHealth(true, "Gemini phản hồi bình thường.", (int)started.ElapsedMilliseconds);

            var error = AiHttpErrorMapper.FromStatusCode(ProviderName, (int)response.StatusCode, usesApiKey: true);
            return CreateHealth(false, error.AdminMessage, (int)started.ElapsedMilliseconds);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            started.Stop();
            var mapped = AiHttpErrorMapper.MapTransportFailure(exception, ProviderName, cancellationToken);
            var message = mapped is AiProviderException provider ? provider.AdminMessage : "Không kết nối được tới Gemini.";
            _logger.LogWarning("Health-check Gemini thất bại: {Message}", message);
            return CreateHealth(false, message, (int)started.ElapsedMilliseconds);
        }
    }

    private async Task<string> GenerateWithModelAsync(
        GeminiRuntimeSettings settings,
        string model,
        string prompt,
        AiGenerationRequest request,
        CancellationToken cancellationToken)
    {
        var url = BuildModelUrl(settings, model, _options.CurrentValue.Gemini.GeneratePathSuffix, null);
        var first = await PostAsync(settings, url, BuildBody(prompt, request, includeGenerationConfig: true), cancellationToken);
        if (first.StatusCode == StatusCodes.Status400BadRequest)
        {
            _logger.LogWarning("Gemini HTTP 400 với model {Model}. Gửi lại bản chỉ có contents.", model);
            first = await PostAsync(settings, url, BuildBody(prompt, request, includeGenerationConfig: false), cancellationToken);
        }

        if (first.StatusCode is < 200 or >= 300)
            throw AiHttpErrorMapper.FromStatusCode(ProviderName, first.StatusCode, usesApiKey: true);
        return ReadGeminiText(first.Body);
    }

    private async Task<OpenGeminiStream> OpenStreamAsync(
        GeminiRuntimeSettings settings,
        string model,
        string prompt,
        AiGenerationRequest request,
        CancellationToken cancellationToken)
    {
        Exception? streamError = null;
        OpenGeminiStream? opened = null;
        try
        {
            opened = await ReadFirstStreamChunkAsync(settings, model, prompt, request, cancellationToken);
        }
        catch (Exception exception) when (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            streamError = exception;
        }

        if (opened is not null)
            return opened;

        if (streamError is AiProviderException terminal && IsTerminal(terminal))
            return new OpenGeminiStream { Error = terminal, StopTryingModels = true };

        try
        {
            var text = await GenerateWithModelAsync(settings, model, prompt, request, cancellationToken);
            if (!string.IsNullOrEmpty(text))
                return new OpenGeminiStream { SingleChunk = text };
        }
        catch (AiProviderException exception)
        {
            return new OpenGeminiStream { Error = exception, StopTryingModels = IsTerminal(exception) };
        }

        var mapped = streamError is null
            ? new AiProviderException(AiProviderErrorKind.InvalidResponse, "Gemini không trả về nội dung stream.")
            : streamError as AiProviderException
                ?? AiHttpErrorMapper.MapTransportFailure(streamError, ProviderName, cancellationToken);
        if (mapped is AiProviderException providerError)
            return new OpenGeminiStream { Error = providerError, StopTryingModels = IsTerminal(providerError) };
        throw mapped;
    }

    private async Task<OpenGeminiStream?> ReadFirstStreamChunkAsync(
        GeminiRuntimeSettings settings,
        string model,
        string prompt,
        AiGenerationRequest request,
        CancellationToken cancellationToken)
    {
        var timeout = StartTimeout(settings.TimeoutSeconds, cancellationToken);
        HttpResponseMessage? response = null;
        StreamReader? reader = null;
        try
        {
            var url = BuildModelUrl(settings, model, _options.CurrentValue.Gemini.StreamPathSuffix, _options.CurrentValue.Gemini.StreamQuery);
            response = await SendStreamAsync(settings, url, BuildBody(prompt, request, includeGenerationConfig: true), timeout.Token, cancellationToken);
            if ((int)response.StatusCode == StatusCodes.Status400BadRequest)
            {
                response.Dispose();
                response = await SendStreamAsync(settings, url, BuildBody(prompt, request, includeGenerationConfig: false), timeout.Token, cancellationToken);
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                response.Dispose();
                throw AiHttpErrorMapper.FromStatusCode(ProviderName, status, usesApiKey: true);
            }

            var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            reader = new StreamReader(stream);
            while (true)
            {
                var line = await reader.ReadLineAsync(timeout.Token);
                if (line is null)
                {
                    reader.Dispose();
                    response.Dispose();
                    timeout.Dispose();
                    return null;
                }

                var chunk = ReadSseData(line);
                if (string.IsNullOrEmpty(chunk))
                    continue;

                return new OpenGeminiStream
                {
                    FirstChunk = chunk,
                    Reader = reader,
                    Response = response,
                    Timeout = timeout,
                };
            }
        }
        catch
        {
            reader?.Dispose();
            response?.Dispose();
            timeout.Dispose();
            throw;
        }
    }

    private async Task<GeminiHttpResult> PostAsync(
        GeminiRuntimeSettings settings,
        string url,
        JsonObject body,
        CancellationToken cancellationToken)
    {
        using var timeout = StartTimeout(settings.TimeoutSeconds, cancellationToken);
        using var response = await SendStreamAsync(settings, url, body, timeout.Token, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(timeout.Token);
        return new GeminiHttpResult((int)response.StatusCode, responseBody);
    }

    private async Task<HttpResponseMessage> SendStreamAsync(
        GeminiRuntimeSettings settings,
        string url,
        JsonObject body,
        CancellationToken timeoutToken,
        CancellationToken callerToken)
    {
        var client = CreateClient();
        using var message = new HttpRequestMessage(HttpMethod.Post, url);
        message.Headers.TryAddWithoutValidation(HeaderName(), settings.ApiKey);
        message.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        try
        {
            return await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeoutToken);
        }
        catch (Exception exception)
        {
            var mapped = AiHttpErrorMapper.MapTransportFailure(exception, ProviderName, callerToken);
            if (mapped is OperationCanceledException)
                throw;
            throw mapped;
        }
    }

    private static JsonObject BuildBody(string prompt, AiGenerationRequest request, bool includeGenerationConfig)
    {
        var text = includeGenerationConfig || !request.ExpectJson
            ? prompt
            : PromptTemplates.JsonOnlyRule + "\n\n" + prompt;
        var body = new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["parts"] = new JsonArray
                    {
                        new JsonObject { ["text"] = text },
                    },
                },
            },
        };
        if (includeGenerationConfig)
        {
            body["generationConfig"] = new JsonObject
            {
                ["temperature"] = request.Temperature,
                ["maxOutputTokens"] = Math.Max(1, request.MaxOutputTokens),
            };
        }

        return body;
    }

    private static string ReadGeminiText(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new AiProviderException(AiProviderErrorKind.InvalidResponse, "Gemini trả về dữ liệu không phải JSON.");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new AiProviderException(AiProviderErrorKind.InvalidResponse, "Gemini trả về JSON không đúng dạng.");

            var blocked = ReadBlockReason(document.RootElement);
            var text = CollectText(document.RootElement);
            if (text.Length > 0)
                return text;
            if (blocked is not null)
                throw new AiProviderException(
                    AiProviderErrorKind.Blocked,
                    $"Gemini đã chặn nội dung ({blocked}). Hãy diễn đạt lại, không gửi dữ liệu nhạy cảm.");
            throw new AiProviderException(AiProviderErrorKind.InvalidResponse, "Gemini không trả về nội dung.");
        }
    }

    private static string? ReadSseData(string line)
    {
        if (line.Length == 0 || line.StartsWith(':'))
            return null;
        if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;
        var payload = line[5..].Trim();
        if (payload.Length == 0 || payload == "[DONE]")
            return null;
        return ReadGeminiText(payload);
    }

    private static string CollectText(JsonElement root)
    {
        var builder = new StringBuilder();
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
            return string.Empty;

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (candidate.ValueKind != JsonValueKind.Object
                || !candidate.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Object
                || !content.TryGetProperty("parts", out var parts)
                || parts.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var part in parts.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.Object
                    && part.TryGetProperty("text", out var text)
                    && text.ValueKind == JsonValueKind.String)
                {
                    var value = text.GetString();
                    if (!string.IsNullOrEmpty(value))
                        builder.Append(value);
                }
            }
        }

        return builder.ToString();
    }

    private static string? ReadBlockReason(JsonElement root)
    {
        if (root.TryGetProperty("promptFeedback", out var feedback)
            && feedback.ValueKind == JsonValueKind.Object
            && feedback.TryGetProperty("blockReason", out var blockReason)
            && blockReason.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(blockReason.GetString()))
            return blockReason.GetString();

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (candidate.ValueKind != JsonValueKind.Object
                || !candidate.TryGetProperty("finishReason", out var finish)
                || finish.ValueKind != JsonValueKind.String)
                continue;
            var value = finish.GetString();
            if (value is "SAFETY" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "RECITATION")
                return value;
        }

        return null;
    }

    private async Task<string?> ReadLineOrInterruptAsync(OpenGeminiStream opened, CancellationToken cancellationToken)
    {
        try
        {
            return await opened.Reader!.ReadLineAsync(opened.Timeout?.Token ?? cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Stream Gemini đứt sau token đầu: {Message}", exception.Message);
            throw new AiStreamInterruptedException("Luồng Gemini bị gián đoạn sau khi đã có nội dung.");
        }
    }

    private string? ReadChunkOrInterrupt(string line)
    {
        try
        {
            return ReadSseData(line);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning("Không đọc được sự kiện SSE của Gemini: {Message}", exception.Message);
            throw new AiStreamInterruptedException("Luồng Gemini bị gián đoạn sau khi đã có nội dung.");
        }
    }

    private void EnsureReady(GeminiRuntimeSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            return;
        var message = settings.ApiKeyUnreadable
            ? "Không đọc được API Key Gemini đã lưu. Hãy nhập lại key."
            : "Chưa cấu hình API Key Gemini.";
        throw new AiProviderException(AiProviderErrorKind.Unauthorized, message);
    }

    private static bool IsTerminal(AiProviderException exception) =>
        exception.Kind is AiProviderErrorKind.Unauthorized
            or AiProviderErrorKind.QuotaExceeded
            or AiProviderErrorKind.Blocked;

    private static IReadOnlyList<string> ListModels(GeminiRuntimeSettings settings)
    {
        var models = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.ModelName))
            models.Add(settings.ModelName.Trim());
        foreach (var name in settings.FallbackModelNames)
        {
            var trimmed = name?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) && !models.Contains(trimmed, StringComparer.Ordinal))
                models.Add(trimmed);
        }

        if (models.Count == 0)
            throw new AiProviderException(AiProviderErrorKind.BadRequest, "Chưa cấu hình tên model Gemini.");
        return models;
    }

    private string BuildModelUrl(GeminiRuntimeSettings settings, string model, string suffix, string? query)
    {
        var url = $"{settings.BaseUrl.TrimEnd('/')}/{settings.ApiVersion.Trim('/')}/models/{Uri.EscapeDataString(model)}{suffix}";
        if (!string.IsNullOrWhiteSpace(query))
            url += "?" + query.TrimStart('?');
        return url;
    }

    private string HeaderName()
    {
        var name = _options.CurrentValue.Gemini.ApiKeyHeaderName;
        return string.IsNullOrWhiteSpace(name) ? "x-goog-api-key" : name.Trim();
    }

    private string Mask(string prompt) =>
        PersonalDataMasker.MaskPersonalData(prompt, _options.CurrentValue.PrivacyReplacement);

    private HttpClient CreateClient() =>
        _httpClientFactory.CreateClient(_options.CurrentValue.Gemini.HttpClientName);

    private static CancellationTokenSource StartTimeout(int seconds, CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, seconds)));
        return timeout;
    }

    private AiHealthResult CreateHealth(bool isHealthy, string message, int? latencyMilliseconds) =>
        new()
        {
            ProviderName = ProviderName,
            IsHealthy = isHealthy,
            Message = message,
            LatencyMilliseconds = latencyMilliseconds,
            CheckedAt = _clock.UtcNow,
        };

    private readonly record struct GeminiHttpResult(int StatusCode, string Body);

    private sealed class OpenGeminiStream : IAsyncDisposable
    {
        public string? SingleChunk { get; init; }

        public string? FirstChunk { get; init; }

        public StreamReader? Reader { get; init; }

        public HttpResponseMessage? Response { get; init; }

        public CancellationTokenSource? Timeout { get; init; }

        public AiProviderException? Error { get; init; }

        public bool StopTryingModels { get; init; }

        public ValueTask DisposeAsync()
        {
            Reader?.Dispose();
            Response?.Dispose();
            Timeout?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
