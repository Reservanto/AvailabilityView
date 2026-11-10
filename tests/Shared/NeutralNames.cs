namespace AvailabilityView.Tests.Shared;

/// <summary>
/// Názvy, popisy a adresy v nahrávkách API se nahrazují neutrálním textem podle typu a Id (<c>Zdroj 10173</c>).
/// Nahrávky jsou ve veřejném repozitáři a skutečný účet merchanta v nich nemá být poznat. Id, časy a stavy zůstávají,
/// testy na nich stojí. Tabulku používá nahrávač i kontrola nahrávek.
/// </summary>
internal static class NeutralNames
{
    /// <summary>Jedno pole k nahrazení.</summary>
    /// <param name="Endpoint">Endpoint nahrávky, třeba <c>Location/GetList</c>.</param>
    /// <param name="Path">Cesta od těla odpovědi k objektům s polem. Seznam se prochází po prvcích.</param>
    /// <param name="Field">Pole s textem, u vymazaného pole i s číslem.</param>
    /// <param name="IdField">Pole s Id ve stejném objektu. Bez Id je náhradou samotný text.</param>
    /// <param name="Text">Neutrální text před Id. <c>null</c> = pole se vymaže, hodnota v nahrávce je <c>null</c>.</param>
    /// <param name="IsNumber">Pole je číslo a dostane číselnou hodnotu z <paramref name="Text"/>.</param>
    internal sealed record Rule(string Endpoint, string[] Path, string Field, string? IdField, string? Text, bool IsNumber = false)
    {
        /// <summary>Neutrální hodnota pro objekt s tímto Id. Prázdný text zůstává prázdný.</summary>
        public string? Value(string? id) => Text is null || IdField is null || string.IsNullOrEmpty(id) ? Text : $"{Text} {id}";
    }

    /// <summary>Vymyšlené číslo účtu místo skutečného.</summary>
    public const string ExampleAccountId = "1234";

    public static IReadOnlyList<Rule> Rules { get; } =
    [
        new("BookingResource/GetList", ["Items"], "Name", "Id", "Zdroj"),
        new("BookingResource/GetList", ["Items"], "Description", "Id", "Popis zdroje"),
        new("BookingService/GetList", ["BookingServices"], "Name", "Id", "Služba"),
        new("BookingService/GetList", ["BookingServices"], "Description", "Id", "Popis služby"),
        new("BookingService/GetList", ["BookingServices"], "SegmentName", "SegmentId", "Zaměření"),
        new("BookingService/GetList", ["BookingServices"], "CategoryName", "CategoryId", "Kategorie"),
        new("BookingService/GetList", ["BookingServices", "AvailablePriceLevels"], "Name", "Id", "Cenová hladina"),
        new("Classes/GetAvailableAppointments", ["Items"], "BookingResourceName", "BookingResourceId", "Zdroj"),
        new("Classes/GetAvailableAppointments", ["Items"], "BookingServiceName", "BookingServiceId", "Služba"),
        new("Classes/GetAvailableAppointments", ["Items"], "CourseName", "CourseId", "Kurz"),
        // Id kalendáře je číslo účtu a vede na jeho veřejnou rezervační stránku, proto je v nahrávce jen příklad.
        // Pravidlo je před názvem kalendáře, který z Id vychází.
        new("Classes/GetAvailableAppointments", ["Items"], "CalendarId", null, ExampleAccountId, IsNumber: true),
        new("Classes/GetAvailableAppointments", ["Items"], "CalendarName", "CalendarId", "Kalendář"),
        new("Classes/GetAvailableAppointments", ["Items"], "BookingUrl", null, $"https://booking.reservanto.cz/Modal?id={ExampleAccountId}"),
        new("Location/GetList", ["Items"], "Name", "Id", "Provozovna"),
        new("Location/GetList", ["Items"], "Description", "Id", "Popis provozovny"),
        new("Location/GetList", ["Items", "Address"], "Street", null, "Ulice 1"),
        new("Location/GetList", ["Items", "Address"], "City", null, "Město"),
        new("Location/GetList", ["Items", "Address"], "ZipCode", null, "000 00"),
        new("Location/GetList", ["Items", "Address"], "Name", null, null),
        new("Location/GetList", ["Items", "Address"], "State", null, null),
        new("Location/GetList", ["Items", "Address"], "Latitude", null, null),
        new("Location/GetList", ["Items", "Address"], "Longitude", null, null),
        new("Segment/GetList", ["Items"], "CustomizedName", "Id", "Zaměření"),
    ];

    /// <summary>Pole v dotazu, který klient poslal. Cesta vede od dotazu.</summary>
    public static IReadOnlyList<Rule> RequestRules { get; } =
    [
        new("Classes/GetAvailableAppointments", [], "CalendarId", null, ExampleAccountId, IsNumber: true),
    ];

    public static IEnumerable<Rule> For(string endpoint) => Rules.Where(rule => rule.Endpoint == endpoint);

    public static IEnumerable<Rule> ForRequest(string endpoint) => RequestRules.Where(rule => rule.Endpoint == endpoint);
}
