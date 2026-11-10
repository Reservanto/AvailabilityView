using Microsoft.Extensions.Options;
using Reservanto.ApiClient;

namespace AvailabilityView.Infrastructure.Availability;

/// <summary>
/// Cache čtení z API v paměti (<c>docs/SPEC.md</c> sekce 9). Souběžné stejné dotazy zavolají API jednou, chyba se
/// neukládá a po vypršení se stará data nevrací. Obsah patří k jednomu LongTimeTokenu: po jeho výměně se celý zahodí
/// a výsledek načítání, které běželo se starým tokenem, se k novému nedostane.
/// </summary>
internal sealed class ReadCache(IOptionsMonitor<ReservantoApiOptions> options, TimeProvider timeProvider)
{
    // Klíčů je omezeně, cizí Id se sem nedostanou. Limit je pojistka proti neomezenému růstu paměti.
    internal const int MaxEntries = 2000;

    private readonly Lock _lock = new();
    private Generation _generation = new(null, timeProvider);

    /// <summary>Hodnota z cache, nebo načtená. Zrušení volajícího načítání nezastaví, můžou na něj čekat další.</summary>
    public Task<T> GetAsync<T>(string key, TimeSpan lifetime, Func<Task<T>> load, CancellationToken ct)
        where T : class =>
        GetAsync(key, _ => lifetime, load, ct);

    /// <summary>Jako jiné přetížení, jen doba platnosti záleží na načtené hodnotě.</summary>
    public async Task<T> GetAsync<T>(string key, Func<T, TimeSpan> lifetime, Func<Task<T>> load, CancellationToken ct)
        where T : class =>
        (T)await Current().GetAsync(key, value => lifetime((T)value), async () => await load(), ct);

    private Generation Current()
    {
        string? token = options.CurrentValue.LongTimeToken;
        lock (_lock)
        {
            if (_generation.LongTimeToken != token)
            {
                _generation = new Generation(token, timeProvider);
            }

            return _generation;
        }
    }

    // Záměrně ne record: vygenerovaný ToString() by vypsal token.
    private sealed class Generation(string? longTimeToken, TimeProvider timeProvider)
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<string, (object Value, DateTimeOffset ExpiresAt)> _entries = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Task<object>> _loads = new(StringComparer.Ordinal);

        public string? LongTimeToken { get; } = longTimeToken;

        public async Task<object> GetAsync(string key, Func<object, TimeSpan> lifetime, Func<Task<object>> load, CancellationToken ct)
        {
            Task<object>? running;
            lock (_lock)
            {
                if (_entries.TryGetValue(key, out (object Value, DateTimeOffset ExpiresAt) entry) && timeProvider.GetUtcNow() < entry.ExpiresAt)
                {
                    return entry.Value;
                }

                if (!_loads.TryGetValue(key, out running))
                {
                    running = LoadAsync(key, lifetime, load);
                    _loads[key] = running;
                }
            }

            return await running.WaitAsync(ct);
        }

        private async Task<object> LoadAsync(string key, Func<object, TimeSpan> lifetime, Func<Task<object>> load)
        {
            // Volá se pod zámkem: zbytek musí běžet až po zápisu do _loads.
            await Task.Yield();
            try
            {
                object value = await load();
                lock (_lock)
                {
                    Store(key, value, timeProvider.GetUtcNow() + lifetime(value));
                }

                return value;
            }
            finally
            {
                lock (_lock)
                {
                    _loads.Remove(key);
                }
            }
        }

        private void Store(string key, object value, DateTimeOffset expiresAt)
        {
            if (_entries.Count >= MaxEntries)
            {
                DateTimeOffset now = timeProvider.GetUtcNow();
                foreach (string expired in _entries.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
                {
                    _entries.Remove(expired);
                }
            }

            if (_entries.Count < MaxEntries || _entries.ContainsKey(key))
            {
                _entries[key] = (value, expiresAt);
            }
        }
    }
}
