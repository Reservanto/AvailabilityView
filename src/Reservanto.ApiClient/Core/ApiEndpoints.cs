namespace Reservanto.ApiClient.Core;

/// <summary>
/// Katalog endpointů, které klient volá. Projekt v Reservantu jen čte: testy hlídají, že tu nic jiného není
/// a že cesty, pole i práva odpovídají snímku OpenAPI.
/// </summary>
public static class ApiEndpoints
{
    /// <summary>Získání krátkodobého tokenu, jediný endpoint bez autorizace.</summary>
    public static readonly ApiEndpoint AuthorizeGetShortTimeToken = new("/v1/Authorize/GetShortTimeToken");

    /// <summary>Zaměření.</summary>
    public static readonly ApiEndpoint SegmentGetList = new("/v1/Segment/GetList", "Segment_r");

    /// <summary>Provozovny.</summary>
    public static readonly ApiEndpoint LocationGetList = new("/v1/Location/GetList", "Location_r");

    /// <summary>Služby.</summary>
    public static readonly ApiEndpoint BookingServiceGetList = new("/v1/BookingService/GetList", "BookingService_r");

    /// <summary>Zdroje.</summary>
    public static readonly ApiEndpoint BookingResourceGetList = new("/v1/BookingResource/GetList", "BookingResource_r");

    /// <summary>Volné začátky pro provozovnu a službu.</summary>
    public static readonly ApiEndpoint OneToOneGetAvailableStartsForLocation = new("/v1/OneToOne/GetAvailableStartsForLocation", "FreeSpace_r");

    /// <summary>Vypsané termíny lekcí.</summary>
    public static readonly ApiEndpoint ClassesGetAvailableAppointments = new("/v1/Classes/GetAvailableAppointments", "FreeSpace_r");

    /// <summary>Základní údaje o merchantovi. Aplikace z nich bere jen název.</summary>
    public static readonly ApiEndpoint MerchantGetInfo = new("/v1/Merchant/GetInfo", "Merchant_r");

    /// <summary>Rezervace v období. Aplikace z nich bere jen obsazené časy, údaje zákazníků zahazuje.</summary>
    public static readonly ApiEndpoint EventGetList = new("/v1/Event/GetList", "Event_r") { CarriesCustomerData = true };

    /// <summary>Pracovní doba zdroje v období, pro hodinovou osu.</summary>
    public static readonly ApiEndpoint WorkingHoursGetForPeriod = new("/v1/WorkingHours/GetForPeriod", "Location_r", "BookingResource_r");

    /// <summary>Všechny endpointy katalogu.</summary>
    public static IReadOnlyList<ApiEndpoint> All { get; } =
    [
        AuthorizeGetShortTimeToken, SegmentGetList, LocationGetList, BookingServiceGetList, BookingResourceGetList,
        OneToOneGetAvailableStartsForLocation, ClassesGetAvailableAppointments, MerchantGetInfo, EventGetList,
        WorkingHoursGetForPeriod,
    ];
}
