namespace AvailabilityView.Application.Connection;

/// <summary>Údaje o připojeném účtu merchanta pro průvodce připojením.</summary>
public interface IMerchantInfo
{
    /// <summary>Název účtu merchanta, prázdný text, když ho merchant nemá vyplněný.</summary>
    /// <param name="ct">Zrušení požadavku.</param>
    /// <exception cref="Availability.ReservationSystemException">Čtení z API selhalo.</exception>
    Task<string> GetNameAsync(CancellationToken ct);
}
