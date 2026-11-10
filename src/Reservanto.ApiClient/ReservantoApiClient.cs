using Reservanto.ApiClient.Core;
using Reservanto.ApiClient.Endpoints;

namespace Reservanto.ApiClient;

/// <summary>
/// Klient Reservanto API, jen čtení. Volání jsou seskupená podle controlleru API a pojmenovaná podle akce:
/// <c>/v1/Segment/GetList</c> = <c>client.Segment.GetListAsync(…)</c>. Žádná business logika.
/// </summary>
/// <remarks>
/// Úspěch = vrácená hodnota; každá chyba = <see cref="ReservantoApiException"/> (technická nedostupnost
/// <see cref="ReservantoApiUnavailableException"/>). Krátkodobý token, opakování a ochranu API při výpadku řeší
/// klient sám. Registrace: <see cref="ReservantoApiServiceCollectionExtensions.AddReservantoApiClient"/>.
/// </remarks>
public sealed class ReservantoApiClient
{
    internal ReservantoApiClient(ApiTransport transport)
    {
        Segment = new SegmentEndpoints(transport);
        Location = new LocationEndpoints(transport);
        BookingService = new BookingServiceEndpoints(transport);
        BookingResource = new BookingResourceEndpoints(transport);
        OneToOne = new OneToOneEndpoints(transport);
        Classes = new ClassesEndpoints(transport);
        Merchant = new MerchantEndpoints(transport);
        Event = new EventEndpoints(transport);
        WorkingHours = new WorkingHoursEndpoints(transport);
    }

    /// <summary><c>/v1/Segment/*</c></summary>
    public SegmentEndpoints Segment { get; }

    /// <summary><c>/v1/Location/*</c></summary>
    public LocationEndpoints Location { get; }

    /// <summary><c>/v1/BookingService/*</c></summary>
    public BookingServiceEndpoints BookingService { get; }

    /// <summary><c>/v1/BookingResource/*</c></summary>
    public BookingResourceEndpoints BookingResource { get; }

    /// <summary><c>/v1/OneToOne/*</c></summary>
    public OneToOneEndpoints OneToOne { get; }

    /// <summary><c>/v1/Classes/*</c></summary>
    public ClassesEndpoints Classes { get; }

    /// <summary><c>/v1/Merchant/*</c></summary>
    public MerchantEndpoints Merchant { get; }

    /// <summary><c>/v1/Event/*</c></summary>
    public EventEndpoints Event { get; }

    /// <summary><c>/v1/WorkingHours/*</c></summary>
    public WorkingHoursEndpoints WorkingHours { get; }
}
