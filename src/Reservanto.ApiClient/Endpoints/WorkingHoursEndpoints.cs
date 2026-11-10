using Reservanto.ApiClient.Core;
using Reservanto.ApiClient.Models;

namespace Reservanto.ApiClient.Endpoints;

/// <summary>Pracovní doba zdroje nebo provozovny v období.</summary>
public sealed class WorkingHoursForPeriodRequest : PagedApiRequest
{
    /// <summary>Zdroj. Bez něj se čte pracovní doba provozovny.</summary>
    public int? BookingResourceId { get; init; }

    /// <summary>Provozovna.</summary>
    public int? LocationId { get; init; }

    /// <summary>Konec období.</summary>
    public DateTimeOffset IntervalEnd { get; init; }

    /// <summary>Začátek období.</summary>
    public DateTimeOffset IntervalStart { get; init; }
}

/// <summary>Pracovní doba jedné stránky.</summary>
public sealed class WorkingHoursResponse : ApiResponse
{
    /// <summary>Zdroj z požadavku.</summary>
    public int? BookingResourceId { get; init; }

    /// <summary>Provozovna z požadavku.</summary>
    public int? LocationId { get; init; }

    /// <summary>Úseky pracovní doby.</summary>
    public IReadOnlyList<WorkingHourApiModel>? Items { get; init; }
}

/// <summary><c>/v1/WorkingHours/*</c></summary>
public sealed class WorkingHoursEndpoints
{
    private readonly ApiTransport _transport;

    internal WorkingHoursEndpoints(ApiTransport transport) => _transport = transport;

    /// <summary>
    /// <c>/v1/WorkingHours/GetForPeriod</c> — konkrétní úseky pracovní doby v období ze všech stránek, včetně
    /// výjimek, třeba svátků.
    /// </summary>
    public Task<IReadOnlyList<WorkingHourApiModel>> GetForPeriodAsync(WorkingHoursForPeriodRequest request, CancellationToken cancellationToken) =>
        _transport.SendForAllPagesAsync<WorkingHoursResponse, WorkingHourApiModel>(ApiEndpoints.WorkingHoursGetForPeriod, request, response => response.Items, cancellationToken);
}
