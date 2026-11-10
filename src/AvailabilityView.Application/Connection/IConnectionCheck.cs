namespace AvailabilityView.Application.Connection;

/// <summary>Ověření připojení proti API (<c>docs/SPEC.md</c> sekce 6 krok 4).</summary>
public interface IConnectionCheck
{
    /// <summary>Zjistí, co instance z účtu merchanta vidí. Selhání API vrací jako stav, ne jako výjimku.</summary>
    /// <param name="ct">Zrušení požadavku.</param>
    Task<ConnectionCheckResult> CheckAsync(CancellationToken ct);
}

/// <summary>Stav připojení z pohledu správce.</summary>
public enum ConnectionState
{
    /// <summary>V konfiguraci není token.</summary>
    NotConnected,

    /// <summary>API token přijalo a vrátilo data.</summary>
    Connected,

    /// <summary>API token odmítlo.</summary>
    TokenRejected,

    /// <summary>Token platí, ale nemá všechna práva.</summary>
    MissingRights,

    /// <summary>API teď neodpovídá.</summary>
    Unavailable,
}

/// <summary>Výsledek ověření.</summary>
/// <param name="State">Stav připojení.</param>
/// <param name="PublicSegments">Počet veřejných zaměření.</param>
/// <param name="PublicLocations">Počet veřejných provozoven.</param>
/// <param name="MerchantName">Název připojeného účtu merchanta. Jediný údaj merchanta, který se zobrazí.</param>
public sealed record ConnectionCheckResult(ConnectionState State, int PublicSegments = 0, int PublicLocations = 0, string MerchantName = "")
{
    /// <summary>Token smí číst rezervace a hodinová osa ukáže obsazenost. Jen u <see cref="ConnectionState.Connected"/>.</summary>
    public bool ShowsOccupancy { get; init; }
}
