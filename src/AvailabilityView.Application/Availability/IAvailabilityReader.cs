namespace AvailabilityView.Application.Availability;

/// <summary>Čtení volné kapacity merchanta pro zobrazení (<c>docs/SPEC.md</c> sekce 3 a 4).</summary>
public interface IAvailabilityReader
{
    /// <summary>Doplní chybějící volby a vrátí termíny. Selhání API vrací jako výsledek, ne jako výjimku.</summary>
    /// <param name="query">Co se má zobrazit.</param>
    /// <param name="ct">Zrušení požadavku.</param>
    Task<AvailabilityResult> ReadAsync(AvailabilityQuery query, CancellationToken ct);

    /// <summary>
    /// Doplní chybějící volby a vrátí nabídku filtrů z <see cref="AvailabilityQuery.Pickers"/> bez čtení dostupnosti
    /// (generátor). Nabídka je stejná jako u <see cref="ReadAsync"/>. Selhání API vrací jako výsledek, ne jako výjimku.
    /// </summary>
    /// <param name="query">Co se má zobrazit.</param>
    /// <param name="ct">Zrušení požadavku.</param>
    Task<AvailabilityOptions> OptionsAsync(AvailabilityQuery query, CancellationToken ct);
}

/// <summary>Nabídka filtrů bez termínů. Jde do prohlížeče: jen Id a názvy veřejných objektů.</summary>
/// <param name="Outcome">
/// <see cref="AvailabilityOutcome.Available"/> = pohled je v pořádku. <see cref="AvailabilityOutcome.NothingFree"/> se
/// nevrací, termíny se nečtou.
/// </param>
/// <param name="Selection">Co se zobrazuje po doplnění voleb.</param>
/// <param name="Filters">Možnosti filtrů v pořadí <see cref="AvailabilityFilter"/>.</param>
/// <param name="Failure">Proč data nejdou načíst, jen u <see cref="AvailabilityOutcome.SourceUnavailable"/>.</param>
public sealed record AvailabilityOptions(
    AvailabilityOutcome Outcome,
    AvailabilitySelection? Selection,
    IReadOnlyList<FilterChoices> Filters,
    Connection.ConnectionState? Failure = null);

/// <summary>Co se má zobrazit. Hodnoty přišly od návštěvníka, nic se o nich nepředpokládá.</summary>
/// <param name="SegmentId">Zaměření, <c>null</c> = první vhodné.</param>
/// <param name="LocationId">Provozovna, <c>null</c> = první vhodná.</param>
/// <param name="ServiceId">Služba, <c>null</c> = všechny veřejné služby pohledu.</param>
/// <param name="ResourceId">Zdroj, <c>null</c> = všechny.</param>
/// <param name="Days">Kolik dní dopředu včetně dneška.</param>
public sealed record AvailabilityQuery(int? SegmentId, int? LocationId, int? ServiceId, int? ResourceId, int Days)
{
    /// <summary>
    /// Nabízené období se ukáže po rozsazích, třeba po týdnech (kalendář). <c>null</c> = celé období najednou (agenda).
    /// </summary>
    public RangeQuery? Range { get; init; }

    /// <summary>
    /// Hodinová osa: výsledek nese i úseky volna, obsazenosti a nedostupnosti za nabízené dny zobrazeného týdne.
    /// Platí jen s rozsahem <see cref="ShownRange.Week"/>.
    /// </summary>
    public bool Timeline { get; init; }

    /// <summary>
    /// Filtry, které si návštěvník přepíná sám. Jejich hodnota je jen výchozí: když k vyšším volbám nepatří, zahodí se
    /// a doplní znovu. Hodnota filtru mimo seznam je pevná a nesouvisející dá <see cref="AvailabilityOutcome.ViewNotAvailable"/>.
    /// </summary>
    public IReadOnlySet<AvailabilityFilter> Pickers { get; init; } = new HashSet<AvailabilityFilter>();

    /// <summary>Některá hodnota z adresy nemá platný tvar. Výsledek je stejný jako u neexistujícího objektu.</summary>
    public bool IsMalformed { get; init; }

    /// <summary>Dotaz za adresu, ve které některá hodnota nemá platný tvar.</summary>
    public static AvailabilityQuery Malformed { get; } = new(null, null, null, null, 1) { IsMalformed = true };
}

/// <summary>Jednotka zobrazeného rozsahu.</summary>
public enum ShownRange
{
    /// <summary>Týden od pondělí.</summary>
    Week,

    /// <summary>Kalendářní měsíc. Výsledek nese i stav každého nabízeného dne.</summary>
    Month,
}

/// <summary>Stav dne v měsíčním přehledu (rozhodnutí uživatele 2026-11-09).</summary>
public enum DayState
{
    /// <summary>Den má aspoň jeden volný termín nebo úsek.</summary>
    Free,

    /// <summary>U lekcí jsou všechny lekce dne plné. U individuálních typů zdroje pracují, ale volno nemají.</summary>
    Full,

    /// <summary>U lekcí den bez lekcí. U individuálních typů žádný zdroj pohledu nepracuje.</summary>
    Closed,
}

/// <summary>Stav jednoho nabízeného dne. Jde do prohlížeče: nese jen den a stav.</summary>
/// <param name="Day">Den v pásmu merchanta.</param>
/// <param name="State">Stav.</param>
public sealed record DayAvailability(DateOnly Day, DayState State);

/// <summary>Který rozsah ukázat.</summary>
/// <param name="Unit">Jednotka rozsahu.</param>
/// <param name="Containing">
/// Den v rozsahu, <c>null</c> = aktuální rozsah. Den mimo nabízené období se posune na nejbližší nabízený rozsah.
/// </param>
public sealed record RangeQuery(ShownRange Unit, DateOnly? Containing);

/// <summary>Filtr, který si návštěvník může přepínat. Pořadí je pořadí závislosti: nižší filtr se řídí vyššími.</summary>
public enum AvailabilityFilter
{
    /// <summary>Zaměření.</summary>
    Segment,

    /// <summary>Provozovna.</summary>
    Location,

    /// <summary>Služba.</summary>
    Service,

    /// <summary>Zdroj.</summary>
    Resource,
}

/// <summary>Jak čtení dopadlo.</summary>
public enum AvailabilityOutcome
{
    /// <summary>Výsledek obsahuje aspoň jeden termín.</summary>
    Available,

    /// <summary>Pohled je v pořádku, v období nic není.</summary>
    NothingFree,

    /// <summary>Hodnota neexistuje, není veřejná nebo k ostatním nepatří. Tyto stavy se záměrně nerozlišují.</summary>
    ViewNotAvailable,

    /// <summary>Typ zaměření první verze nepodporuje.</summary>
    UnsupportedSegment,

    /// <summary>Instance není připojená k účtu.</summary>
    NotConnected,

    /// <summary>Data teď nejde načíst.</summary>
    SourceUnavailable,
}

/// <summary>Výsledek čtení.</summary>
/// <param name="Outcome">Jak čtení dopadlo.</param>
/// <param name="Selection">Co se zobrazuje po doplnění voleb.</param>
/// <param name="Slots">Termíny podle času.</param>
/// <param name="Failure">Proč data nejdou načíst, jen u <see cref="AvailabilityOutcome.SourceUnavailable"/>. Pro průvodce, ne pro návštěvníka.</param>
public sealed record AvailabilityResult(
    AvailabilityOutcome Outcome,
    AvailabilitySelection? Selection,
    IReadOnlyList<AvailabilitySlot> Slots,
    Connection.ConnectionState? Failure = null)
{
    /// <summary>Druh termínů, je u každého výsledku s <see cref="Selection"/>, i bez termínů.</summary>
    public SlotKind? Kind { get; init; }

    /// <summary>Jaké dny výsledek pokrývá, v pásmu merchanta. Je u každého výsledku s <see cref="Selection"/>.</summary>
    public AvailabilityPeriod? Period { get; init; }

    /// <summary>Možnosti filtrů z <see cref="AvailabilityQuery.Pickers"/> v pořadí <see cref="AvailabilityFilter"/>.</summary>
    public IReadOnlyList<FilterChoices> Filters { get; init; } = [];

    /// <summary>
    /// Úseky hodinové osy podle času, jen s <see cref="AvailabilityQuery.Timeline"/> a jen u druhů zaměření s pracovní
    /// dobou. Prázdné = osa ukáže jen termíny.
    /// </summary>
    public IReadOnlyList<TimeBand> Bands { get; init; } = [];

    /// <summary>Stav každého nabízeného dne zobrazeného měsíce podle dne, jen s rozsahem <see cref="ShownRange.Month"/>.</summary>
    public IReadOnlyList<DayAvailability> Days { get; init; } = [];

    /// <summary>Výsledek bez termínů.</summary>
    /// <param name="outcome">Jak čtení dopadlo.</param>
    public static AvailabilityResult Without(AvailabilityOutcome outcome) => new(outcome, null, []);
}

/// <summary>Dny, které výsledek pokrývá, v pásmu merchanta. Konce do období už nepatří.</summary>
/// <param name="Today">Dnešek.</param>
/// <param name="OfferedEnd">První den za nabízeným obdobím. Nabízí se dny od dneška do tohoto dne.</param>
/// <param name="ShownStart">První zobrazený den. U rozsahu jeho první den (u týdne pondělí), může být před dneškem.</param>
/// <param name="ShownEnd">První den za zobrazeným obdobím. U rozsahu může být za nabízeným obdobím.</param>
/// <param name="Previous">První den předchozího nabízeného rozsahu, <c>null</c> = žádný není nebo se nezobrazuje po rozsazích.</param>
/// <param name="Next">První den dalšího nabízeného rozsahu, <c>null</c> = žádný není nebo se nezobrazuje po rozsazích.</param>
public sealed record AvailabilityPeriod(
    DateOnly Today,
    DateOnly OfferedEnd,
    DateOnly ShownStart,
    DateOnly ShownEnd,
    DateOnly? Previous,
    DateOnly? Next)
{
    /// <summary>Den patří do nabízeného období. Dny mimo něj se zobrazí jako mimo nabídku a nemají termíny.</summary>
    /// <param name="day">Den.</param>
    public bool IsOffered(DateOnly day) => day >= Today && day < OfferedEnd;

    /// <summary>Zobrazené dny v pořadí, i ty mimo nabídku.</summary>
    public IEnumerable<DateOnly> ShownDays() =>
        Enumerable.Range(0, ShownEnd.DayNumber - ShownStart.DayNumber).Select(ShownStart.AddDays);
}

/// <summary>Možnosti jednoho filtru. Jde do prohlížeče: jen Id a název veřejných objektů.</summary>
/// <param name="Filter">Filtr.</param>
/// <param name="SelectedId">Zvolená možnost, <c>null</c> = všechny.</param>
/// <param name="AllowsAll">Filtr jde nechat bez volby, pak platí pro všechny možnosti.</param>
/// <param name="Options">Veřejné možnosti, které patří k vyšším volbám a k pevným hodnotám z adresy, v pořadí z API.</param>
public sealed record FilterChoices(AvailabilityFilter Filter, int? SelectedId, bool AllowsAll, IReadOnlyList<FilterOption> Options);

/// <summary>Možnost filtru.</summary>
/// <param name="Id">Id veřejného objektu.</param>
/// <param name="Name">Název.</param>
public sealed record FilterOption(int Id, string Name);

/// <summary>Volby po doplnění chybějících hodnot.</summary>
/// <param name="Segment">Zaměření.</param>
/// <param name="Location">Provozovna.</param>
/// <param name="Service">Služba, <c>null</c> = všechny služby zaměření.</param>
/// <param name="Resource">Zdroj, <c>null</c> = všechny.</param>
public sealed record AvailabilitySelection(PublicSegment Segment, PublicLocation Location, PublicService? Service, PublicResource? Resource);

/// <summary>
/// Termín pro návštěvníka. Tento typ jde do prohlížeče: nové pole je rozhodnutí uživatele, seznam hlídá test.
/// </summary>
/// <param name="Start">Začátek v časovém pásmu merchanta.</param>
/// <param name="End">Konec v časovém pásmu merchanta.</param>
/// <param name="ServiceName">Název služby, <c>null</c> u volného úseku zdroje (služba se neukazuje, SPEC sekce 3).</param>
/// <param name="ResourceName">Název zdroje.</param>
/// <param name="OccupancyText">Obsazenost ve formátu merchanta, jen u lekcí.</param>
/// <param name="IsFull">Termín je plně obsazený.</param>
public sealed record AvailabilitySlot(
    DateTimeOffset Start,
    DateTimeOffset End,
    string? ServiceName,
    string ResourceName,
    string? OccupancyText,
    bool IsFull);

/// <summary>Stav úseku hodinové osy.</summary>
public enum TimeBandState
{
    /// <summary>Aspoň jeden zdroj pohledu má volno.</summary>
    Free,

    /// <summary>Zdroj má v pracovní době rezervaci. Jen když token smí číst rezervace.</summary>
    Busy,

    /// <summary>Zbytek pracovní doby: přestávka, krátká mezera, pauza po rezervaci nebo rezervace bez práva je číst.</summary>
    Unavailable,
}

/// <summary>Úsek hodinové osy. Jde do prohlížeče: nese jen čas a stav, žádný údaj rezervace.</summary>
/// <param name="Start">Začátek v časovém pásmu merchanta.</param>
/// <param name="End">Konec v časovém pásmu merchanta.</param>
/// <param name="State">Stav.</param>
public sealed record TimeBand(DateTimeOffset Start, DateTimeOffset End, TimeBandState State);
