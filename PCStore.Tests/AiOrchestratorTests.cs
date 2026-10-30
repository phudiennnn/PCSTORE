using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using PCStore.API.Services.Ai;
using PCStore.Core.Ai;

namespace PCStore.Tests;

public class AiOrchestratorTests
{
    private const string ValidIntent =
        """{"intent":"build_pc","budget_vnd":15000000,"purpose":"gaming","cpu_brand":null,"gpu_brand":null,"need_monitor":false,"notes":null}""";

    [Fact]
    public async Task ExtractIntent_WhenOllamaFails_UsesGemini()
    {
        var ollama = Provider(AiProviderNames.Ollama);
        var gemini = Provider(AiProviderNames.Gemini);
        ollama.GenerateHandler = _ => throw new AiProviderException(AiProviderErrorKind.Timeout, "Ollama hết thời gian chờ.");
        gemini.GenerateHandler = _ => Task.FromResult(ValidIntent);
        var logs = new LogCapture();
        var orchestrator = CreateOrchestrator(ollama, gemini, logs, out _);

        var result = await orchestrator.ExtractIntentAsync("Dựng PC chơi game 15 triệu", CancellationToken.None);

        Assert.Equal(AiProviderNames.Gemini, result.ProviderName);
        Assert.Equal(CustomerIntent.BuildPc, result.Intent);
        Assert.Equal(1, ollama.GenerateCalls);
        Assert.Equal(1, gemini.GenerateCalls);
        Assert.Contains(logs.Entries, entry => entry.Provider == AiProviderNames.Gemini && entry.IsSuccess && entry.FailoverReason == "Ollama hết thời gian chờ.");
    }

    [Fact]
    public async Task ExtractIntent_WhenJsonIsInvalid_RetriesOnceThenUsesGemini()
    {
        var ollama = Provider(AiProviderNames.Ollama);
        var gemini = Provider(AiProviderNames.Gemini);
        var attempts = 0;
        ollama.GenerateHandler = _ =>
        {
            attempts++;
            return Task.FromResult(attempts == 1 ? "không phải json" : "vẫn sai");
        };
        gemini.GenerateHandler = _ => Task.FromResult(ValidIntent);
        var orchestrator = CreateOrchestrator(ollama, gemini, new LogCapture(), out _);

        var result = await orchestrator.ExtractIntentAsync("hỏi bảo hành", CancellationToken.None);

        Assert.Equal(AiProviderNames.Gemini, result.ProviderName);
        Assert.Equal(2, ollama.GenerateCalls);
        Assert.Equal(1, gemini.GenerateCalls);
    }

    [Fact]
    public async Task ExtractIntent_WhenBothProvidersFail_ThrowsWithoutStackTrace()
    {
        var ollama = Provider(AiProviderNames.Ollama);
        var gemini = Provider(AiProviderNames.Gemini);
        ollama.GenerateHandler = _ => throw new AiProviderException(AiProviderErrorKind.ConnectionFailed, "Không kết nối được tới Ollama.");
        gemini.GenerateHandler = _ => throw new AiProviderException(AiProviderErrorKind.QuotaExceeded, "Gemini đã hết hạn mức miễn phí (HTTP 429).");
        var orchestrator = CreateOrchestrator(ollama, gemini, new LogCapture(), out _);

        var exception = await Assert.ThrowsAsync<AllProvidersFailedException>(() =>
            orchestrator.ExtractIntentAsync("xin chào", CancellationToken.None));

        Assert.Null(exception.StackTrace);
        Assert.DoesNotContain("at ", exception.ToString());
        Assert.Contains("Không kết nối được tới Ollama.", exception.Message);
        Assert.Contains("Gemini đã hết hạn mức", exception.Message);
        Assert.Equal(1, ollama.GenerateCalls);
        Assert.Equal(1, gemini.GenerateCalls);
    }

    [Fact]
    public async Task ExtractIntent_CircuitOpensAfterThreeFailuresAndClosesAfterSuccess()
    {
        var ollama = Provider(AiProviderNames.Ollama);
        var gemini = Provider(AiProviderNames.Gemini);
        ollama.GenerateHandler = _ =>
        {
            if (ollama.GenerateCalls <= 3)
                throw new AiProviderException(AiProviderErrorKind.ServerError, "Ollama gặp lỗi máy chủ (HTTP 500).");
            return Task.FromResult(ValidIntent);
        };
        gemini.GenerateHandler = _ => Task.FromResult(ValidIntent);
        var orchestrator = CreateOrchestrator(ollama, gemini, new LogCapture(), out var clock);

        for (var attempt = 0; attempt < 3; attempt++)
            await orchestrator.ExtractIntentAsync("build", CancellationToken.None);

        await orchestrator.ExtractIntentAsync("build", CancellationToken.None);
        Assert.Equal(3, ollama.GenerateCalls);
        Assert.Equal(4, gemini.GenerateCalls);

        clock.UtcNow = clock.UtcNow.AddSeconds(60);
        var recovered = await orchestrator.ExtractIntentAsync("build", CancellationToken.None);
        Assert.Equal(AiProviderNames.Ollama, recovered.ProviderName);
        Assert.Equal(4, ollama.GenerateCalls);

        var next = await orchestrator.ExtractIntentAsync("build", CancellationToken.None);
        Assert.Equal(AiProviderNames.Ollama, next.ProviderName);
        Assert.Equal(5, ollama.GenerateCalls);
        Assert.Equal(4, gemini.GenerateCalls);
    }

    [Fact]
    public async Task ExtractIntent_WhenOllamaIsInMaintenance_SkipsToGemini()
    {
        var ollama = Provider(AiProviderNames.Ollama);
        var gemini = Provider(AiProviderNames.Gemini);
        gemini.GenerateHandler = _ => Task.FromResult(ValidIntent);
        var orchestrator = CreateOrchestrator(ollama, gemini, new LogCapture(), out _, maintenance: true);

        var result = await orchestrator.ExtractIntentAsync("cần tư vấn", CancellationToken.None);

        Assert.Equal(AiProviderNames.Gemini, result.ProviderName);
        Assert.Equal(0, ollama.GenerateCalls);
        Assert.Equal(1, gemini.GenerateCalls);
    }

    [Fact]
    public async Task ExplainSelection_WhenOllamaFailsBeforeFirstToken_StreamsGemini()
    {
        var ollama = Provider(AiProviderNames.Ollama);
        var gemini = Provider(AiProviderNames.Gemini);
        ollama.ErrorBeforeToken = new AiProviderException(AiProviderErrorKind.ConnectionFailed, "Không kết nối được tới Ollama.");
        gemini.Chunks = ["Xin chào, cấu hình đã chọn có CPU A."];
        var orchestrator = CreateOrchestrator(ollama, gemini, new LogCapture(), out _);

        var text = "";
        await foreach (var chunk in orchestrator.ExplainSelectionAsync("""{"parts":[]}""", CancellationToken.None))
            text += chunk;

        Assert.Equal("Xin chào, cấu hình đã chọn có CPU A.", text);
        Assert.Equal(1, ollama.StreamCalls);
        Assert.Equal(1, gemini.StreamCalls);
    }

    [Fact]
    public async Task ExplainSelection_WhenOllamaFailsAfterFirstToken_DoesNotSwitchProvider()
    {
        var ollama = Provider(AiProviderNames.Ollama);
        var gemini = Provider(AiProviderNames.Gemini);
        ollama.Chunks = ["Một"];
        ollama.ErrorAfterToken = new AiProviderException(AiProviderErrorKind.ConnectionFailed, "Mất kết nối.");
        gemini.Chunks = ["Không được gọi."];
        var orchestrator = CreateOrchestrator(ollama, gemini, new LogCapture(), out _);
        var enumerator = orchestrator.ExplainSelectionAsync("""{"parts":[]}""", CancellationToken.None).GetAsyncEnumerator();
        try
        {
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal("Một", enumerator.Current);
            var exception = await Assert.ThrowsAsync<AiStreamInterruptedException>(() => enumerator.MoveNextAsync().AsTask());
            Assert.Null(exception.StackTrace);
            Assert.Equal(0, gemini.StreamCalls);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    private static AiOrchestrator CreateOrchestrator(
        FakeAiProvider ollama,
        FakeAiProvider gemini,
        LogCapture logs,
        out ManualClock clock,
        bool maintenance = false)
    {
        clock = new ManualClock();
        return new AiOrchestrator(
            [ollama, gemini],
            new FixedSettings(CreateSettings(maintenance)),
            logs,
            new FakePrompts(),
            new AiJsonParser(),
            new AiJsonValidator(),
            clock,
            new SilentLogger<AiOrchestrator>());
    }

    private static AiRuntimeSettings CreateSettings(bool maintenance) => new()
    {
        Ollama = new OllamaRuntimeSettings
        {
            BaseUrl = "http://ollama.test",
            ModelName = "test-local",
            KeepAlive = "30m",
            NumCtx = 4096,
            TimeoutSeconds = 30,
            ConnectTimeoutSeconds = 3,
            IsMaintenanceMode = maintenance,
        },
        Gemini = new GeminiRuntimeSettings
        {
            BaseUrl = "https://gemini.test",
            ApiVersion = "v1beta",
            ModelName = "test-remote",
            FallbackModelNames = [],
            ApiKey = "test-key",
            TimeoutSeconds = 30,
            ConnectTimeoutSeconds = 3,
            Temperature = 0.4m,
            MaxOutputTokens = 512,
            IsEnabled = true,
        },
        CircuitFailureThreshold = 3,
        CircuitOpenSeconds = 60,
        ExtractIntent = new IntentTaskSettings
        {
            Temperature = 0.1,
            NumPredict = 200,
            MaxRepairInputCharacters = 2000,
            MaxCustomerMessageCharacters = 4000,
        },
        ExplainSelection = new ExplainTaskSettings
        {
            Temperature = 0.4,
            MaxOutputTokens = 512,
            MaxWords = 120,
        },
    };

    private static FakeAiProvider Provider(string name) => new()
    {
        ProviderName = name,
        GenerateHandler = _ => Task.FromResult(ValidIntent),
    };

    private sealed class FakeAiProvider : IAiProvider
    {
        public required string ProviderName { get; init; }

        public int GenerateCalls { get; private set; }

        public int StreamCalls { get; private set; }

        public Func<AiGenerationRequest, Task<string>>? GenerateHandler { get; set; }

        public Exception? ErrorBeforeToken { get; set; }

        public Exception? ErrorAfterToken { get; set; }

        public IReadOnlyList<string> Chunks { get; set; } = [];

        public Task<string> GenerateJsonAsync(AiGenerationRequest request, CancellationToken cancellationToken)
        {
            GenerateCalls++;
            return GenerateHandler!(request);
        }

        public async IAsyncEnumerable<string> StreamTextAsync(
            AiGenerationRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            StreamCalls++;
            if (ErrorBeforeToken is not null)
                throw ErrorBeforeToken;
            foreach (var chunk in Chunks)
                yield return chunk;
            if (ErrorAfterToken is not null)
                throw ErrorAfterToken;
            await Task.CompletedTask;
        }

        public Task<AiHealthResult> CheckHealthAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AiHealthResult
            {
                ProviderName = ProviderName,
                IsHealthy = true,
                Message = "ok",
                CheckedAt = DateTimeOffset.UtcNow,
            });
    }

    private sealed class FixedSettings(AiRuntimeSettings settings) : IAiSettingsService
    {
        public Task<AiRuntimeSettings> GetRuntimeSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(settings);

        public Task<AiSettingsAdminView> GetAdminViewAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveOllamaSettingsAsync(OllamaSettingsWriteModel model, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveGeminiSettingsAsync(GeminiSettingsWriteModel model, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void InvalidateCache()
        {
        }

        public int GetOllamaConnectTimeoutSeconds() => settings.Ollama.ConnectTimeoutSeconds;

        public int GetGeminiConnectTimeoutSeconds() => settings.Gemini.ConnectTimeoutSeconds;
    }

    private sealed class LogCapture : IAiRequestLogService
    {
        public List<AiRequestLogEntry> Entries { get; } = [];

        public Task WriteAsync(AiRequestLogEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<AiUsageSummary> ReadRecentSummaryAsync(int lookbackHours, CancellationToken cancellationToken) =>
            Task.FromResult(new AiUsageSummary { LookbackHours = lookbackHours });
    }

    private sealed class FakePrompts : IPromptTemplateProvider
    {
        public string PromptVersion => PromptTemplates.PromptVersion;

        public string BuildIntentPrompt(string customerMessage) => "intent:" + customerMessage;

        public string BuildIntentRepairPrompt(string invalidOutput) => "repair:" + invalidOutput;

        public string BuildExplanationPrompt(string selectedPartsJson) => "explain:" + selectedPartsJson;

        public string BuildCustomerReplyPrompt(string customerMessage) => "reply:" + customerMessage;

        public string BuildIntentJsonSchema() => """{"type":"object"}""";
    }

    private sealed class ManualClock : IAiClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class SilentLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
