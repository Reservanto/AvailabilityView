namespace AvailabilityView.Application.Availability;

/// <summary>
/// Obsazené časy zdrojů z rezervací pro hodinovou osu (<c>docs/SPEC.md</c> sekce 3). Nese jen zdroj a čas, údaje
/// zákazníků se zahodí už při čtení odpovědi API. Obsazenost je volba správce (SPEC sekce 6): token bez práva číst
/// rezervace není chyba.
/// </summary>
public interface IResourceOccupancy
{
    /// <summary>Rezervace, které v období drží čas zdroje. Zrušené, zamítnuté a náhradníci se nepočítají.</summary>
    /// <param name="interval">Období.</param>
    /// <param name="ct">Zrušení požadavku.</param>
    /// <exception cref="ReservationSystemException">Data nejde načíst. Chybějící právo k rezervacím sem nepatří.</exception>
    Task<OccupancyReading> ReadAsync(AvailabilityInterval interval, CancellationToken ct);
}

/// <summary>Obsazenost v období.</summary>
/// <param name="IsPermitted">Token smí číst rezervace. Když ne, obsazenost se neukazuje a <see cref="Busy"/> je prázdné.</param>
/// <param name="Busy">Časy, které drží rezervace.</param>
public sealed record OccupancyReading(bool IsPermitted, IReadOnlyList<ResourceInterval> Busy)
{
    /// <summary>Token nesmí číst rezervace, správce obsazenost nezapnul.</summary>
    public static OccupancyReading NotPermitted { get; } = new(false, []);
}
