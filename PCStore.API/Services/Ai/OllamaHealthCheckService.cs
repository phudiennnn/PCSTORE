using Microsoft.Extensions.Options;
using PCStore.Core.Ai;

namespace PCStore.API.Services.Ai;

/// <summary>Warm-up Ollama lúc khởi động và kiểm tra sức khỏe định kỳ.</summary>
public sealed class OllamaHealthCheckService : BackgroundService
{
    private readonly OllamaProvider _ollamaProvider;
    private readonly IOllamaHealthState _healthState;
    private readonly IOptionsMonitor<AiOptions> _options;
    private readonly ILogger<OllamaHealthCheckService> _logger;

    public OllamaHealthCheckService(
        OllamaProvider ollamaProvider,
        IOllamaHealthState healthState,
        IOptionsMonitor<AiOptions> options,
        ILogger<OllamaHealthCheckService> logger)
    {
        _ollamaProvider = ollamaProvider;
        _healthState = healthState;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var warmup = await _ollamaProvider.WarmUpAsync(stoppingToken);
            _healthState.Record(warmup);
            if (warmup.IsHealthy)
                _logger.LogInformation("Ollama warm-up: {Message}", warmup.Message);
            else
                _logger.LogWarning("Ollama warm-up: {Message}", warmup.Message);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Ollama warm-up thất bại: {Message}", exception.Message);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await _ollamaProvider.CheckHealthAsync(stoppingToken);
                _healthState.Record(result);
                if (!result.IsHealthy)
                    _logger.LogWarning("Ollama health-check: {Message}", result.Message);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning("Ollama health-check thất bại: {Message}", exception.Message);
            }

            var seconds = Math.Max(5, _options.CurrentValue.HealthCheckIntervalSeconds);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
