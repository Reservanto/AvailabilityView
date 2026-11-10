using Reservanto.ApiClient.Core;
using Reservanto.ApiClient.Models;

namespace Reservanto.ApiClient.Endpoints;

// Zdroje a dostupnost. Cachování je věc konzumenta, klient necachuje nic.

/// <summary>Volné začátky pro provozovnu a službu.</summary>
public sealed class AvailableStartsForLocationRequest : ApiRequest
{
    /// <summary>Služba.</summary>
    public int BookingServiceId { get; init; }

    /// <summary>Provozovna.</summary>
    public int LocationId { get; init; }

    /// <summary>Konec intervalu.</summary>
    public DateTimeOffset IntervalEnd { get; init; }

    /// <summary>Začátek intervalu.</summary>
    public DateTimeOffset IntervalStart { get; init; }
}

/// <summary>Volné začátky po zdrojích provozovny.</summary>
public sealed class AvailableStartsForLocationResponse : ApiResponse
{
    /// <summary>Id zdroje → možné začátky. OpenAPI u zdroje dovoluje <c>null</c>.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<DateTimeOffset>?>? Starts { get; init; }
}

/// <summary>Vypsané termíny Classes pro zaměření a provozovnu.</summary>
public sealed class AvailableClassesRequest : PagedApiRequest
{
    /// <summary>Volitelně jen jeden kalendář.</summary>
    public int? CalendarId { get; init; }

    /// <summary>Provozovna.</summary>
    public int LocationId { get; init; }

    /// <summary>Zaměření.</summary>
    public int SegmentId { get; init; }

    /// <summary>Konec intervalu.</summary>
    public DateTimeOffset IntervalEnd { get; init; }

    /// <summary>Začátek intervalu.</summary>
    public DateTimeOffset IntervalStart { get; init; }
}

/// <summary><c>/v1/BookingResource/*</c></summary>
public sealed class BookingResourceEndpoints
{
    private readonly ApiTransport _transport;

    internal BookingResourceEndpoints(ApiTransport transport) => _transport = transport;

    /// <summary><c>/v1/BookingResource/GetList</c></summary>
    public Task<IReadOnlyList<BookingResourceApiModel>> GetListAsync(BookingResourceGetListRequest request, CancellationToken cancellationToken) =>
        _transport.SendForAllItemsAsync<BookingResourceApiModel>(ApiEndpoints.BookingResourceGetList, request, cancellationToken);
}

/// <summary><c>/v1/OneToOne/*</c></summary>
public sealed class OneToOneEndpoints
{
    private readonly ApiTransport _transport;

    internal OneToOneEndpoints(ApiTransport transport) => _transport = transport;

    /// <summary><c>/v1/OneToOne/GetAvailableStartsForLocation</c> — Id zdroje → možné začátky.</summary>
    public async Task<IReadOnlyDictionary<int, IReadOnlyList<DateTimeOffset>>> GetAvailableStartsForLocationAsync(
        AvailableStartsForLocationRequest request, CancellationToken cancellationToken)
    {
        AvailableStartsForLocationResponse response = await _transport.SendAsync<AvailableStartsForLocationResponse>(
            ApiEndpoints.OneToOneGetAvailableStartsForLocation, request, cancellationToken);
        IReadOnlyDictionary<int, IReadOnlyList<DateTimeOffset>?> starts = _transport.Collection<IReadOnlyDictionary<int, IReadOnlyList<DateTimeOffset>?>>(
            ApiEndpoints.OneToOneGetAvailableStartsForLocation, response, response.Starts, () => new Dictionary<int, IReadOnlyList<DateTimeOffset>?>());
        return starts.ToDictionary(pair => pair.Key, pair => pair.Value ?? (IReadOnlyList<DateTimeOffset>)[]);
    }
}

/// <summary><c>/v1/Classes/*</c></summary>
public sealed class ClassesEndpoints
{
    private readonly ApiTransport _transport;

    internal ClassesEndpoints(ApiTransport transport) => _transport = transport;

    /// <summary><c>/v1/Classes/GetAvailableAppointments</c></summary>
    public Task<IReadOnlyList<ExtendedAppointmentClassApiModel>> GetAvailableAppointmentsAsync(AvailableClassesRequest request, CancellationToken cancellationToken) =>
        _transport.SendForAllItemsAsync<ExtendedAppointmentClassApiModel>(ApiEndpoints.ClassesGetAvailableAppointments, request, cancellationToken);
}
