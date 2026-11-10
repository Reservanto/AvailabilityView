namespace AvailabilityView.Application.Availability;

/// <summary>Pracovní doba zdrojů pro hodinovou osu (<c>docs/SPEC.md</c> sekce 3).</summary>
public interface IWorkingHours
{
    /// <summary>Úseky pracovní doby zdrojů v období, včetně výjimek, třeba svátků.</summary>
    /// <param name="resourceIds">Veřejné zdroje pohledu.</param>
    /// <param name="interval">Období.</param>
    /// <param name="ct">Zrušení požadavku.</param>
    /// <exception cref="ReservationSystemException">Data nejde načíst.</exception>
    Task<IReadOnlyList<ResourceInterval>> ReadAsync(IReadOnlyList<int> resourceIds, AvailabilityInterval interval, CancellationToken ct);
}
