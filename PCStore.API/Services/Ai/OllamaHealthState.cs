using PCStore.Core.Ai;

namespace PCStore.API.Services.Ai;

/// <summary>Lưu kết quả health-check Ollama gần nhất trong bộ nhớ.</summary>
public sealed class OllamaHealthState : IOllamaHealthState
{
    private readonly object _gate = new();
    private AiHealthResult? _lastResult;

    public AiHealthResult? LastResult
    {
        get
        {
            lock (_gate)
                return _lastResult;
        }
    }

    public void Record(AiHealthResult result)
    {
        lock (_gate)
            _lastResult = result;
    }
}
