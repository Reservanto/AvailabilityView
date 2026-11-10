namespace AvailabilityView.Application.Availability;

/// <summary>Veřejné objekty merchanta. Neveřejné se sem nedostanou, e-maily a telefony také ne.</summary>
public interface IPublicCatalog
{
    /// <summary>Veřejné objekty v pořadí z API.</summary>
    /// <param name="ct">Zrušení požadavku.</param>
    /// <exception cref="ReservationSystemException">Data nejde načíst.</exception>
    Task<PublicCatalog> GetAsync(CancellationToken ct);
}

/// <summary>Veřejné objekty merchanta.</summary>
/// <param name="Segments">Zaměření.</param>
/// <param name="Locations">Provozovny.</param>
/// <param name="Services">Služby.</param>
/// <param name="Resources">Zdroje.</param>
public sealed record PublicCatalog(
    IReadOnlyList<PublicSegment> Segments,
    IReadOnlyList<PublicLocation> Locations,
    IReadOnlyList<PublicService> Services,
    IReadOnlyList<PublicResource> Resources);

/// <summary>Veřejné zaměření.</summary>
/// <param name="Id">Id.</param>
/// <param name="Name">Název.</param>
/// <param name="Type">Typ zaměření z API.</param>
public sealed record PublicSegment(int Id, string Name, string Type);

/// <summary>Veřejná provozovna.</summary>
/// <param name="Id">Id.</param>
/// <param name="Name">Název.</param>
public sealed record PublicLocation(int Id, string Name);

/// <summary>Veřejná služba.</summary>
/// <param name="Id">Id.</param>
/// <param name="Name">Název.</param>
/// <param name="SegmentId">Zaměření.</param>
/// <param name="Duration">Trvání.</param>
/// <param name="ResourceIds">Veřejné zdroje, které službu poskytují.</param>
public sealed record PublicService(int Id, string Name, int SegmentId, TimeSpan Duration, IReadOnlyList<int> ResourceIds);

/// <summary>Veřejný zdroj.</summary>
/// <param name="Id">Id.</param>
/// <param name="Name">Název.</param>
/// <param name="LocationId">Provozovna.</param>
/// <param name="SegmentIds">Zaměření, ve kterých zdroj vystupuje.</param>
public sealed record PublicResource(int Id, string Name, int LocationId, IReadOnlyList<int> SegmentIds);

/// <summary>Rezervační systém neposkytl data.</summary>
/// <param name="reason">Proč, jako stav připojení.</param>
public sealed class ReservationSystemException(Connection.ConnectionState reason) : Exception("Rezervační systém neposkytl data.")
{
    /// <summary>Proč, jako stav připojení.</summary>
    public Connection.ConnectionState Reason { get; } = reason;
}
