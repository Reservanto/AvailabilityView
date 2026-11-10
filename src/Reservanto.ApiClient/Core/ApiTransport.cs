using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Reservanto.ApiClient.Core;

/// <summary>
/// Volání endpointu: 1× opakuje při timeoutu nebo chybě spojení a v degradovaném stavu API nevolá. Všechna volání
/// jsou čtení, takže opakování je bezpečné. Na odpověď s chybou (4xx/5xx, <c>IsError</c>) se neopakuje.
/// Krátkodobý token a 401 řeší <see cref="Authorization.ShortTimeTokenHandler"/> pod tímto HttpClientem.
/// </summary>
internal sealed class ApiTransport(
    HttpClient http,
    ApiDegradationMonitor degradation,
    IOptionsMonitor<ReservantoApiOptions> options,
    TimeProvider timeProvider,
    ILogger<ApiTransport> logger)
{
    private const int MaxAttempts = 2;

    // Pojistka proti nekonečné smyčce, kdyby API stránkování ignorovalo. 10 000 položek žádný merchant nemá.
    private const int MaxPages = 100;

    /// <summary>Stránkovaný endpoint s odpovědí <c>ListResponseOf…</c>: vrátí položky ze všech stránek.</summary>
    public Task<IReadOnlyList<T>> SendForAllItemsAsync<T>(ApiEndpoint endpoint, PagedApiRequest request, CancellationToken cancellationToken) =>
        SendForAllPagesAsync<ListResponse<T>, T>(endpoint, request, response => response.Items, cancellationToken);

    /// <summary>
    /// Projde stránky, dokud nepřijde neúplná. API celkový počet nevrací, poslední plná stránka proto stojí
    /// jedno volání navíc.
    /// </summary>
    public async Task<IReadOnlyList<TItem>> SendForAllPagesAsync<TResponse, TItem>(
        ApiEndpoint endpoint,
        PagedApiRequest request,
        Func<TResponse, IReadOnlyList<TItem>?> items,
        CancellationToken cancellationToken)
        where TResponse : ApiResponse
    {
        List<TItem> all = [];
        for (int page = 1; page <= MaxPages; page++)
        {
            request.Page = page;
            TResponse response = await SendAsync<TResponse>(endpoint, request, cancellationToken);
            IReadOnlyList<TItem> current = Collection(endpoint, response, items(response), () => []);
            all.AddRange(current);
            if (current.Count < request.PageSize)
            {
                return all;
            }
        }

        throw new ReservantoApiException(endpoint, $"odpověď má víc než {MaxPages} stránek");
    }

    /// <summary>
    /// Kolekce z úspěšné odpovědi. OpenAPI všechny kolekce značí jako nullable, proto chybějící kolekce v provozu
    /// = prázdná. V přísném režimu je chybějící kolekce chyba: tak se ověří, že „prázdná“ neschovává jiný stav.
    /// </summary>
    public T Collection<T>(ApiEndpoint endpoint, ApiResponse response, T? collection, Func<T> empty)
        where T : class =>
        collection
        ?? (options.CurrentValue.StrictDeserialization
            ? throw new ReservantoApiException(endpoint, "odpověď neobsahuje kolekci (přísný režim)", HttpStatusCode.OK, response)
            : empty());

    public async Task<TResponse> SendAsync<TResponse>(ApiEndpoint endpoint, ApiRequest request, CancellationToken cancellationToken)
        where TResponse : ApiResponse
    {
        if (degradation.IsDegraded)
        {
            throw new ReservantoApiUnavailableException(endpoint, "API je dočasně ve stavu degraded, volání se neprovádí");
        }

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                TResponse response = await ApiHttp.PostAsync<TResponse>(
                    http, endpoint, request, timeProvider, ApiJson.For(options.CurrentValue), cancellationToken);
                degradation.RecordSuccess();
                return response;
            }
            catch (ReservantoApiUnavailableException exception) when (attempt < MaxAttempts && !degradation.IsDegraded)
            {
                ApiClientLog.RetryingRead(logger, exception, endpoint.Path, exception.Message);
            }
            catch (ReservantoApiUnavailableException exception)
            {
                // Selhání se počítá za volání, ne za pokus: práh „5 selhání / 30 s“ = 5 neúspěšných volání.
                degradation.RecordFailure();
                ApiClientLog.Unavailable(logger, exception, endpoint.Path);
                throw;
            }
            catch (ReservantoApiException exception)
            {
                // 5xx = technické selhání; jiná chybová odpověď znamená, že API žije a spojení je v pořádku.
                if (exception.StatusCode >= HttpStatusCode.InternalServerError)
                {
                    degradation.RecordFailure();
                }
                else
                {
                    degradation.RecordSuccess();
                }

                if (exception.IsErrorResponse)
                {
                    // Texty API mohou obsahovat osobní údaje: do výchozího logu jen parametry, texty na Debug.
                    ApiClientLog.ErrorResponse(logger, endpoint.Path, exception.ErrorParameters);
                    ApiClientLog.ErrorResponseTexts(logger, endpoint.Path, exception.ErrorMessages);
                }
                else
                {
                    ApiClientLog.Failed(logger, exception, endpoint.Path, (int?)exception.StatusCode);
                }

                throw;
            }
        }
    }
}
