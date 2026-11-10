using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reservanto.ApiClient.Core;

namespace Reservanto.ApiClient.Authorization;

/// <summary>
/// Krátkodobý token merchanta: žije jen v paměti procesu a nikdy neopouští server. Získá se líně při prvním volání
/// API a obnoví se po vypršení nebo po 401. Najednou běží nejvýš jedno získání tokenu, ostatní volání čekají na jeho
/// výsledek. Po selhání exponenciální backoff: neplatný LongTimeToken nesmí vyvolat bouři požadavků na token.
/// LongTimeToken se čte při každém použití, správce ho může vyměnit za běhu (<c>docs/SPEC.md</c> sekce 6 krok 4).
/// Krátkodobý token i backoff patří k jednomu LongTimeTokenu, po výměně neplatí.
/// </summary>
internal sealed class ShortTimeTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<ReservantoApiOptions> options,
    TimeProvider timeProvider,
    ILogger<ShortTimeTokenProvider> logger) : IDisposable
{
    /// <summary>Název HttpClientu pro získání tokenu, bez <see cref="ShortTimeTokenHandler"/> (endpoint je bez autorizace).</summary>
    public const string HttpClientName = "Reservanto.ApiClient.Authorize";

    // Token se přestane používat chvíli před koncem životnosti, aby nevypršel cestou k API.
    private static readonly TimeSpan _expirySafetyMargin = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _firstBackoff = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _maxBackoff = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _fetchLock = new(1, 1);
    private volatile TokenState? _token;
    private int _consecutiveFailures;
    private DateTimeOffset _nextAttemptAt = DateTimeOffset.MinValue;
    private string? _failedLongTimeToken;
    private bool _lastFailureWasRejection;

    /// <summary>Počet selhání získání tokenu po sobě.</summary>
    public int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);

    /// <summary>Platný token z paměti, nebo nově získaný.</summary>
    public async ValueTask<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (Usable(_token) is { } cached)
        {
            return cached;
        }

        await _fetchLock.WaitAsync(cancellationToken);
        try
        {
            return Usable(_token) ?? await FetchAsync();
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    /// <summary>
    /// API odmítlo <paramref name="rejectedToken"/> (401). Pokud ho mezitím jiné volání už nahradilo, vrátí
    /// nový token bez dalšího požadavku; jinak získá nový.
    /// </summary>
    public async Task<string> RefreshAfterRejectionAsync(string rejectedToken, CancellationToken cancellationToken)
    {
        await _fetchLock.WaitAsync(cancellationToken);
        try
        {
            if (Usable(_token) is { } current && current != rejectedToken)
            {
                return current;
            }

            _token = null;
            return await FetchAsync();
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _fetchLock.Dispose();

    private string? Usable(TokenState? token)
    {
        ReservantoApiOptions current = options.CurrentValue;
        return token is not null
            && token.LongTimeToken == current.LongTimeToken
            && timeProvider.GetUtcNow() - token.IssuedAt < current.ShortTimeTokenLifetime - _expirySafetyMargin
                ? token.Value
                : null;
    }

    // Získání tokenu nesleduje zrušení volajícího: zaseknutý endpoint by jinak skončil zrušením, které se nezapočítá
    // do selhání, a backoff by se nespustil. Požadavek omezuje vlastní timeout jeho HttpClientu.
    // Volá se jen pod _fetchLock.
    private async Task<string> FetchAsync()
    {
        ApiEndpoint endpoint = ApiEndpoints.AuthorizeGetShortTimeToken;
        string? longTimeToken = options.CurrentValue.LongTimeToken;
        if (string.IsNullOrWhiteSpace(longTimeToken))
        {
            throw new ReservantoApiUnavailableException(endpoint, "instance není připojená, chybí LongTimeToken");
        }

        if (_failedLongTimeToken is not null && _failedLongTimeToken != longTimeToken)
        {
            ForgetFailures();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (now < _nextAttemptAt)
        {
            throw LastFailure(endpoint);
        }

        try
        {
            HttpClient http = httpClientFactory.CreateClient(HttpClientName);
            var request = new ShortTimeTokenRequest { LongTimeToken = longTimeToken };
            ShortTimeTokenResponse response = await ApiHttp.PostAsync<ShortTimeTokenResponse>(
                http, endpoint, request, timeProvider, ApiJson.For(options.CurrentValue), CancellationToken.None);
            if (string.IsNullOrEmpty(response.ShortTimeToken))
            {
                throw new ReservantoApiException(endpoint, "odpověď neobsahuje token", System.Net.HttpStatusCode.OK, response);
            }

            _token = new TokenState(response.ShortTimeToken, longTimeToken, now);
            ForgetFailures();
            ApiClientLog.TokenAcquired(logger);

            // Správce mezitím token vyměnil nebo smazal: krátkodobý token patří starému účtu a nesmí odejít ani teď.
            return options.CurrentValue.LongTimeToken == longTimeToken ? response.ShortTimeToken : await FetchAsync();
        }
        catch (ReservantoApiException exception)
        {
            int failures = Interlocked.Increment(ref _consecutiveFailures);
            TimeSpan backoff = Backoff(failures);
            // Od okamžiku selhání, ne od začátku požadavku: po timeoutu by krátký backoff byl vypršelý hned.
            _nextAttemptAt = timeProvider.GetUtcNow() + backoff;
            _failedLongTimeToken = longTimeToken;
            _lastFailureWasRejection = IsRejection(exception);
            // Původní výjimku nelogujeme ani nepředáváme dál: chyba čtení odpovědi nese v textu její obsah, tedy i token.
            ApiClientLog.TokenFailed(logger, (int?)exception.StatusCode, failures, backoff);
            throw LastFailure(endpoint);
        }
    }

    private void ForgetFailures()
    {
        Volatile.Write(ref _consecutiveFailures, 0);
        _nextAttemptAt = DateTimeOffset.MinValue;
        _failedLongTimeToken = null;
    }

    // Během backoffu se API nevolá, volající ale musí dál poznat, proč token není.
    private ReservantoApiException LastFailure(ApiEndpoint endpoint) =>
        _lastFailureWasRejection
            ? new ReservantoTokenRejectedException(endpoint, statusCode: null)
            : new ReservantoApiUnavailableException(endpoint, "token se nepodařilo získat");

    // Odmítnutí API hlásí chybou s parametrem LongTimeToken (ověřeno na produkci 2026-11-06), případně stavem 401 nebo 403.
    // Jiná chyba o tokenu nic neříká a radit správci smazání platného tokenu by bylo horší než hlásit výpadek.
    private static bool IsRejection(ReservantoApiException exception) => exception switch
    {
        ReservantoApiUnavailableException => false,
        { IsErrorResponse: true } => exception.ErrorParameters.Contains(nameof(ShortTimeTokenRequest.LongTimeToken), StringComparer.Ordinal),
        _ => exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
    };

    private static TimeSpan Backoff(int failures)
    {
        double seconds = _firstBackoff.TotalSeconds * Math.Pow(2, Math.Min(failures - 1, 16));
        return TimeSpan.FromSeconds(Math.Min(seconds, _maxBackoff.TotalSeconds));
    }

    // Záměrně ne record: vygenerovaný ToString() by token vypsal, kdyby se stav někde zalogoval.
    private sealed class TokenState(string value, string longTimeToken, DateTimeOffset issuedAt)
    {
        public string Value { get; } = value;

        public string LongTimeToken { get; } = longTimeToken;

        public DateTimeOffset IssuedAt { get; } = issuedAt;
    }
}
