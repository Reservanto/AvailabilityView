using Reservanto.ApiClient.Core;
using Reservanto.ApiClient.Models;

namespace Reservanto.ApiClient.Endpoints;

// Katalog merchanta: zaměření, provozovny, služby, zdroje. Requesty 1:1 podle OpenAPI.

/// <summary>Výběr objektů podle viditelnosti.</summary>
public sealed class ObjectStateListRequest : PagedApiRequest
{
    /// <summary>Jen veřejné objekty (viditelné zákazníkům).</summary>
    public bool OnlyPublic { get; init; }
}

/// <summary>Výběr objektů podle viditelnosti, volitelně jen změněné po datu.</summary>
public sealed class ObjectStateModifiedAfterListRequest : PagedApiRequest
{
    /// <summary>Jen objekty změněné po tomto čase.</summary>
    public DateTimeOffset? ModifiedAfter { get; init; }

    /// <summary>Jen veřejné objekty.</summary>
    public bool OnlyPublic { get; init; }
}

/// <summary>Seznam zdrojů.</summary>
public sealed class BookingResourceGetListRequest : PagedApiRequest
{
    /// <summary>Jen zdroje provozovny.</summary>
    public int? LocationId { get; init; }

    /// <summary>Jen veřejné zdroje.</summary>
    public bool OnlyPublic { get; init; }
}

/// <summary>Seznam všech služeb (<c>BookingService/GetList</c> — kolekce v poli <c>BookingServices</c>, ne <c>Items</c>).</summary>
public sealed class LoadAllBookingServiceResponse : ApiResponse
{
    /// <summary>Služby.</summary>
    public IReadOnlyList<BookingServiceApiModel>? BookingServices { get; init; }
}

/// <summary><c>/v1/Segment/*</c></summary>
public sealed class SegmentEndpoints
{
    private readonly ApiTransport _transport;

    internal SegmentEndpoints(ApiTransport transport) => _transport = transport;

    /// <summary><c>/v1/Segment/GetList</c> — zaměření merchanta.</summary>
    public Task<IReadOnlyList<SegmentApiModel>> GetListAsync(ObjectStateListRequest request, CancellationToken cancellationToken) =>
        _transport.SendForAllItemsAsync<SegmentApiModel>(ApiEndpoints.SegmentGetList, request, cancellationToken);
}

/// <summary><c>/v1/Location/*</c></summary>
public sealed class LocationEndpoints
{
    private readonly ApiTransport _transport;

    internal LocationEndpoints(ApiTransport transport) => _transport = transport;

    /// <summary><c>/v1/Location/GetList</c> — provozovny (vazbu na zaměření nenesou, je u zdrojů).</summary>
    public Task<IReadOnlyList<LocationApiModel>> GetListAsync(ObjectStateListRequest request, CancellationToken cancellationToken) =>
        _transport.SendForAllItemsAsync<LocationApiModel>(ApiEndpoints.LocationGetList, request, cancellationToken);
}

/// <summary><c>/v1/BookingService/*</c></summary>
public sealed class BookingServiceEndpoints
{
    private readonly ApiTransport _transport;

    internal BookingServiceEndpoints(ApiTransport transport) => _transport = transport;

    /// <summary><c>/v1/BookingService/GetList</c></summary>
    public Task<IReadOnlyList<BookingServiceApiModel>> GetListAsync(ObjectStateModifiedAfterListRequest request, CancellationToken cancellationToken) =>
        _transport.SendForAllPagesAsync<LoadAllBookingServiceResponse, BookingServiceApiModel>(
            ApiEndpoints.BookingServiceGetList, request, response => response.BookingServices, cancellationToken);
}
