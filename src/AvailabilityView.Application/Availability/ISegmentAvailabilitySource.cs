namespace AvailabilityView.Application.Availability;

/// <summary>
/// Čtení termínů jednoho druhu zaměření (pravidlo A3 konvencí): nový druh je nová třída, sdílený kód se podle typu
/// nevětví. Volba třídy podle <see cref="SegmentTypes"/> je jediné místo, které typ zaměření rozlišuje.
/// </summary>
public interface ISegmentAvailabilitySource
{
    /// <summary>Typy zaměření z API, které třída čte.</summary>
    IReadOnlyList<string> SegmentTypes { get; }

    /// <summary>Co čtení vrací: lekce, nebo volné úseky zdrojů.</summary>
    SlotKind Kind { get; }

    /// <summary>Termíny v období. Veřejnost a uplynulý čas hlídá volající.</summary>
    /// <param name="request">Co číst.</param>
    /// <param name="ct">Zrušení požadavku.</param>
    /// <exception cref="ReservationSystemException">Data nejde načíst.</exception>
    Task<IReadOnlyList<SourceSlot>> ReadAsync(SourceRequest request, CancellationToken ct);

    /// <summary>
    /// Pracovní doba a obsazenost zdrojů pohledu pro hodinovou osu. <c>null</c> = druh zaměření je nemá a osa ukáže
    /// jen termíny. Veřejnost hlídá volající.
    /// </summary>
    /// <param name="request">Co číst.</param>
    /// <param name="week">Zobrazený týden.</param>
    /// <param name="ct">Zrušení požadavku.</param>
    /// <exception cref="ReservationSystemException">Data nejde načíst.</exception>
    Task<SourceTimeline?> ReadTimelineAsync(SourceRequest request, AvailabilityInterval week, CancellationToken ct);

    /// <summary>
    /// Pracovní doba zdrojů pohledu pro stav dnů měsíce, bez rezervací. <c>null</c> = druh zaměření ji nemá.
    /// Veřejnost hlídá volající.
    /// </summary>
    /// <param name="request">Co číst.</param>
    /// <param name="interval">Nabízené dny zobrazeného měsíce.</param>
    /// <param name="ct">Zrušení požadavku.</param>
    /// <exception cref="ReservationSystemException">Data nejde načíst.</exception>
    Task<IReadOnlyList<ResourceInterval>?> ReadWorkingHoursAsync(SourceRequest request, AvailabilityInterval interval, CancellationToken ct);
}

/// <summary>Druh termínů ve výsledku. Zobrazení se podle něj řídí místo odvozování z tvaru dat.</summary>
public enum SlotKind
{
    /// <summary>Lekce s pevným termínem, službou a obsazeností.</summary>
    Lesson,

    /// <summary>Volné úseky zdrojů sjednocené přes služby pohledu, bez názvu služby.</summary>
    FreeTime,
}

/// <summary>Co číst.</summary>
/// <param name="Selection">Volby.</param>
/// <param name="Services">
/// Veřejné služby pohledu: zvolená služba, nebo všechny veřejné služby zaměření, které poskytuje některý veřejný zdroj
/// pohledu. Neveřejná služba se nečte.
/// </param>
/// <param name="Resources">Veřejné zdroje pohledu. Pracovní doba neveřejného zdroje se nečte.</param>
/// <param name="Interval">Období.</param>
/// <param name="NotBefore">Začátky před tímto okamžikem už neplatí. Volné úseky se z nich nesestaví.</param>
public sealed record SourceRequest(
    AvailabilitySelection Selection,
    IReadOnlyList<PublicService> Services,
    IReadOnlyList<PublicResource> Resources,
    AvailabilityInterval Interval,
    DateTimeOffset NotBefore);

/// <summary>Pracovní doba a obsazenost zdrojů.</summary>
/// <param name="Working">Úseky pracovní doby po zdrojích.</param>
/// <param name="Busy">Časy, které drží rezervace. Prázdné, když token nesmí číst rezervace.</param>
public sealed record SourceTimeline(IReadOnlyList<ResourceInterval> Working, IReadOnlyList<ResourceInterval> Busy);

/// <summary>Úsek času jednoho zdroje.</summary>
/// <param name="ResourceId">Zdroj.</param>
/// <param name="Start">Začátek.</param>
/// <param name="End">Konec.</param>
public sealed record ResourceInterval(int ResourceId, DateTimeOffset Start, DateTimeOffset End);

/// <summary>Termín nebo volný úsek zdroje z rezervačního systému, zatím bez kontroly veřejnosti.</summary>
/// <param name="Start">Začátek.</param>
/// <param name="End">Konec.</param>
/// <param name="ServiceId">Služba, <c>null</c> = volný úsek zdroje přes služby pohledu, služba se neukazuje.</param>
/// <param name="ResourceId">Zdroj.</param>
/// <param name="OccupancyText">Obsazenost ve formátu merchanta.</param>
/// <param name="IsFull">Termín je plně obsazený.</param>
public sealed record SourceSlot(DateTimeOffset Start, DateTimeOffset End, int? ServiceId, int ResourceId, string? OccupancyText, bool IsFull);

/// <summary>Období od začátku místního dne do začátku jiného místního dne.</summary>
/// <param name="Start">Začátek včetně.</param>
/// <param name="End">Konec, už nepatří do období.</param>
public sealed record AvailabilityInterval(DateTimeOffset Start, DateTimeOffset End);

/// <summary>Nastavení čtení a zobrazení.</summary>
/// <param name="TimeZone">Časové pásmo merchanta, API ho nevrací.</param>
/// <param name="MaxDays">Nejdelší období.</param>
/// <param name="DefaultLanguage">Jazyk stránky, když ho adresa neurčí. Jedna z <see cref="Languages"/>.</param>
public sealed record AvailabilitySettings(TimeZoneInfo TimeZone, int MaxDays, string DefaultLanguage = "cs")
{
    /// <summary>Jazyky stránky návštěvníka (<c>docs/SPEC.md</c> sekce 5). Texty k nim má Web.</summary>
    public static IReadOnlyList<string> Languages { get; } = ["cs", "en"];
}
