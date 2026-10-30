using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PCStore.Core.Ai;

namespace PCStore.API.Services.Ai;

/// <summary>Gọi Ollama qua IHttpClientFactory, có timeout kết nối và timeout sinh nội dung tách riêng.</summary>
public sealed class OllamaProvider : IAiProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAiSettingsService _settingsService;
    private readonly IOptionsMonitor<AiOptions> _options;
    private readonly IAiClock _clock;
    private readonly ILogger<OllamaProvider> _logger;

    public OllamaProvider(
        IHttpClientFactory httpClientFactory,
        IAiSettingsService settingsService,
        IOptionsMonitor<AiOptions> options,
        IAiClock clock,
        ILogger<OllamaProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settingsService = settingsService;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public string ProviderName => AiProviderNames.Ollama;

    public async Task<string> GenerateJsonAsync(AiGenerationRequest request, CancellationToken cancellationToken)
    {
        var settings = (await _settingsService.GetRuntimeSettingsAsync(cancellationToken)).Ollama;
        return await CompleteAsync(settings, request, cancellationToken);
    }

    public async IAsyncEnumerable<string> StreamTextAsync(
        AiGenerationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var settings = (await _settingsService.GetRuntimeSettingsAsync(cancellationToken)).Ollama;
        using var timeout = StartTimeout(settings.TimeoutSeconds, cancellationToken);
        using var response = await SendAsync(settings, request, stream: true, timeout.Token, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw AiHttpErrorMapper.FromStatusCode(ProviderName, (int)response.StatusCode, usesApiKey: false);

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);
        var started = false;
        while (true)
        {
            var line = await ReadLineOrFailAsync(reader, timeout.Token, cancellationToken, started);
            if (line is null)
                yield break;

            var parsed = ParseStreamLine(line);
            if (parsed.Failed)
            {
                if (started)
                    throw new AiStreamInterruptedException("Luồng Ollama bị gián đoạn sau khi đã có nội dung.");
                throw parsed.Error ?? new AiProviderException(AiProviderErrorKind.InvalidResponse, "Ollama trả về dòng stream không đọc được.");
            }

            if (!string.IsNullOrEmpty(parsed.Token))
            {
                started = true;
                yield return parsed.Token;
            }

            if (parsed.Done)
                yield break;
        }
    }

    public async Task<AiHealthResult> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var settings = (await _settingsService.GetRuntimeSettingsAsync(cancellationToken)).Ollama;
        var healthTimeout = Math.Max(1, _options.CurrentValue.Ollama.HealthTimeoutSeconds);
        var started = Stopwatch.StartNew();
        try
        {
            using var timeout = StartTimeout(healthTimeout, cancellationToken);
            var client = CreateClient();
            using var response = await client.GetAsync(BuildUrl(settings.BaseUrl, _options.CurrentValue.Ollama.HealthPath), timeout.Token);
            started.Stop();
            var healthy = response.IsSuccessStatusCode;
            var message = healthy
                ? "Ollama phản hồi bình thường."
                : AiHttpErrorMapper.FromStatusCode(ProviderName, (int)response.StatusCode, usesApiKey: false).AdminMessage;
            if (settings.IsMaintenanceMode)
                message += " Chế độ bảo trì đang bật.";
            return CreateHealth(healthy, message, (int)started.ElapsedMilliseconds);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            started.Stop();
            var mapped = AiHttpErrorMapper.MapTransportFailure(exception, ProviderName, cancellationToken);
            var message = mapped is AiProviderException provider ? provider.AdminMessage : "Không kết nối được tới Ollama.";
            _logger.LogWarning("Health-check Ollama thất bại: {Message}", message);
            return CreateHealth(false, message, (int)started.ElapsedMilliseconds);
        }
    }

    public async Task<AiHealthResult> WarmUpAsync(CancellationToken cancellationToken)
    {
        var runtime = await _settingsService.GetRuntimeSettingsAsync(cancellationToken);
        if (runtime.Ollama.IsMaintenanceMode)
            return CreateHealth(false, "Bỏ qua warm-up vì Ollama đang ở chế độ bảo trì.", null);

        var started = Stopwatch.StartNew();
        try
        {
            await CompleteAsync(runtime.Ollama, new AiGenerationRequest
            {
                Prompt = _options.CurrentValue.Ollama.WarmUpPrompt,
                TaskName = AiTaskNames.WarmUp,
                Temperature = runtime.ExtractIntent.Temperature,
                MaxOutputTokens = Math.Max(1, _options.CurrentValue.Ollama.WarmUpNumPredict),
            }, cancellationToken);
            started.Stop();
            return CreateHealth(true, "Đã warm-up Ollama.", (int)started.ElapsedMilliseconds);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            started.Stop();
            var mapped = AiHttpErrorMapper.MapTransportFailure(exception, ProviderName, cancellationToken);
            var message = mapped is AiProviderException provider ? provider.AdminMessage : "Warm-up Ollama thất bại.";
            _logger.LogWarning("Warm-up Ollama thất bại: {Message}", message);
            return CreateHealth(false, message, (int)started.ElapsedMilliseconds);
        }
    }

    private async Task<string> CompleteAsync(
        OllamaRuntimeSettings settings,
        AiGenerationRequest request,
        CancellationToken cancellationToken)
    {
        using var timeout = StartTimeout(settings.TimeoutSeconds, cancellationToken);
        using var response = await SendAsync(settings, request, stream: false, timeout.Token, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw AiHttpErrorMapper.FromStatusCode(ProviderName, (int)response.StatusCode, usesApiKey: false);

        var text = ReadCompletion(body);
        if (string.IsNullOrWhiteSpace(text))
            throw new AiProviderException(AiProviderErrorKind.InvalidResponse, "Ollama không trả về nội dung.");
        return text;
    }

    private async Task<HttpResponseMessage> SendAsync(
        OllamaRuntimeSettings settings,
        AiGenerationRequest request,
        bool stream,
        CancellationToken timeoutToken,
        CancellationToken callerToken)
    {
        var client = CreateClient();
        using var message = new HttpRequestMessage(HttpMethod.Post, BuildUrl(settings.BaseUrl, _options.CurrentValue.Ollama.GeneratePath));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            message.Content = new StringContent(BuildBody(settings, request, stream).ToJsonString(), Encoding.UTF8, "application/json");
            return await client.SendAsync(
                message,
                stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                timeoutToken);
        }
        catch (JsonException)
        {
            throw new AiProviderException(AiProviderErrorKind.InvalidResponse, "JSON schema gửi cho Ollama không hợp lệ.");
        }
        catch (Exception exception)
        {
            var mapped = AiHttpErrorMapper.MapTransportFailure(exception, ProviderName, callerToken);
            if (mapped is OperationCanceledException)
                throw;
            throw mapped;
        }
    }

    private static JsonObject BuildBody(OllamaRuntimeSettings settings, AiGenerationRequest request, bool stream)
    {
        var body = new JsonObject
        {
            ["model"] = settings.ModelName,
            ["prompt"] = request.Prompt,
            ["stream"] = stream,
            ["keep_alive"] = settings.KeepAlive,
            ["options"] = new JsonObject
            {
                ["temperature"] = request.Temperature,
                ["num_predict"] = Math.Max(1, request.MaxOutputTokens),
                ["num_ctx"] = settings.NumCtx,
            },
        };
        if (!stream && !string.IsNullOrWhiteSpace(request.JsonSchema))
            body["format"] = JsonNode.Parse(request.JsonSchema);
        return body;
    }

    private static string ReadCompletion(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new AiProviderException(AiProviderErrorKind.InvalidResponse, "Ollama trả về JSON không đúng dạng.");
            if (root.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(error.GetString()))
                throw new AiProviderException(AiProviderErrorKind.InvalidResponse, "Ollama không sinh được nội dung với model hiện tại.");
            if (root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.String)
                return response.GetString() ?? string.Empty;
            throw new AiProviderException(AiProviderErrorKind.InvalidResponse, "Ollama không trả về nội dung.");
        }
        catch (JsonException)
        {
            throw new AiProviderException(AiProviderErrorKind.InvalidResponse, "Ollama trả về dữ liệu không phải JSON.");
        }
    }

    private async Task<string?> ReadLineOrFailAsync(
        StreamReader reader,
        CancellationToken timeoutToken,
        CancellationToken callerToken,
        bool started)
    {
        try
        {
            return await reader.ReadLineAsync(timeoutToken);
        }
        catch (Exception exception) when (!callerToken.IsCancellationRequested)
        {
            if (started)
                throw new AiStreamInterruptedException("Luồng Ollama bị gián đoạn sau khi đã có nội dung.");
            var mapped = AiHttpErrorMapper.MapTransportFailure(exception, ProviderName, callerToken);
            if (mapped is OperationCanceledException)
                throw;
            throw mapped;
        }
    }

    private static StreamLine ParseStreamLine(string line)
    {
        if (line.Length == 0)
            return new StreamLine(string.Empty, false, false, null);
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new StreamLine(string.Empty, false, true, new AiProviderException(AiProviderErrorKind.InvalidResponse, "Ollama trả về dòng stream không đúng dạng."));
            if (root.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(error.GetString()))
                return new StreamLine(string.Empty, true, true, new AiProviderException(AiProviderErrorKind.InvalidResponse, "Ollama không sinh được nội dung với model hiện tại."));

            var token = root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.String
                ? response.GetString() ?? string.Empty
                : string.Empty;
            var done = root.TryGetProperty("done", out var doneProperty)
                && doneProperty.ValueKind == JsonValueKind.True;
            return new StreamLine(token, done, false, null);
        }
        catch (JsonException)
        {
            return new StreamLine(string.Empty, false, true, new AiProviderException(AiProviderErrorKind.InvalidResponse, "Ollama trả về dòng stream không phải JSON."));
        }
    }

    private HttpClient CreateClient() =>
        _httpClientFactory.CreateClient(_options.CurrentValue.Ollama.HttpClientName);

    private static string BuildUrl(string baseUrl, string path)
    {
        var suffix = path.StartsWith('/') ? path : "/" + path;
        return baseUrl.TrimEnd('/') + suffix;
    }

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

    private readonly record struct StreamLine(string Token, bool Done, bool Failed, AiProviderException? Error);
}
