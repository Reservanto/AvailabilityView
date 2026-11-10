using Reservanto.ApiClient.Core;
using Reservanto.ApiClient.Models;

namespace Reservanto.ApiClient.Endpoints;

/// <summary>Rezervace v období.</summary>
public sealed class EventListRequest : PagedApiRequest
{
    /// <summary>Konec období.</summary>
    public DateTimeOffset EndDate { get; init; }

    /// <summary>Začátek období.</summary>
    public DateTimeOffset StartDate { get; init; }
}

/// <summary>Rezervace jedné stránky.</summary>
public sealed class EventListResponse : ApiResponse
{
    /// <summary>Rezervace bez údajů zákazníků.</summary>
    public IReadOnlyList<EventApiModel>? Events { get; init; }
}

/// <summary><c>/v1/Event/*</c></summary>
public sealed class EventEndpoints
{
    private readonly ApiTransport _transport;

    internal EventEndpoints(ApiTransport transport) => _transport = transport;

    /// <summary><c>/v1/Event/GetList</c> — rezervace v období ze všech stránek, bez údajů zákazníků.</summary>
    public Task<IReadOnlyList<EventApiModel>> GetListAsync(EventListRequest request, CancellationToken cancellationToken) =>
        _transport.SendForAllPagesAsync<EventListResponse, EventApiModel>(ApiEndpoints.EventGetList, request, response => response.Events, cancellationToken);
}
