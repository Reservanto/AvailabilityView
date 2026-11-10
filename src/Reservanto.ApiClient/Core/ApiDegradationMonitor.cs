using Microsoft.Extensions.Options;

namespace Reservanto.ApiClient.Core;

/// <summary>
/// Ochrana API při výpadku: po sérii technických selhání volání přejde klient na
/// krátkou dobu do stavu „API degraded“ a čtení okamžitě odmítá bez volání API. Jednoduchý stav v paměti —
/// pro 1 instanci = 1 merchant víc netřeba (žádná circuit-breaker knihovna).
/// </summary>
internal sealed class ApiDegradationMonitor(IOptionsMonitor<ReservantoApiOptions> options, TimeProvider timeProvider)
{
    private readonly Lock _lock = new();
    private readonly Queue<DateTimeOffset> _failures = new();
    private DateTimeOffset? _degradedUntil;

    /// <summary>Je API právě v degradovaném stavu?</summary>
    public bool IsDegraded
    {
        get
        {
            lock (_lock)
            {
                return _degradedUntil is { } until && timeProvider.GetUtcNow() < until;
            }
        }
    }

    /// <summary>Zaznamená technické selhání volání (timeout, spojení, HTTP 5xx).</summary>
    public void RecordFailure()
    {
        DegradationOptions settings = options.CurrentValue.Degradation;
        DateTimeOffset now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            _failures.Enqueue(now);
            while (_failures.Peek() <= now - settings.FailureWindow)
            {
                _failures.Dequeue();
            }

            if (_failures.Count >= settings.FailureThreshold)
            {
                _degradedUntil = now + settings.DegradedDuration;
                _failures.Clear();
            }
        }
    }

    /// <summary>Úspěšná odpověď API: série selhání se přerušila.</summary>
    public void RecordSuccess()
    {
        lock (_lock)
        {
            _failures.Clear();
        }
    }
}
