using System.Globalization;
using AvailabilityView.Application.Availability;

namespace AvailabilityView.Web.Availability;

/// <summary>Hláška místo termínů. Každá má text v obou jazycích pod klíčem <c>Visitor.Message.{hodnota}</c>.</summary>
public enum PageMessage
{
    /// <summary>V období nic není.</summary>
    NothingFree,

    /// <summary>Hodnota z adresy neexistuje, není veřejná, nepatří k ostatním nebo nemá platný tvar.</summary>
    ViewNotAvailable,

    /// <summary>Typ zaměření první verze nepodporuje.</summary>
    UnsupportedSegment,

    /// <summary>Instance není připojená, stránka odkáže na průvodce.</summary>
    NotConnected,

    /// <summary>Data teď nejde načíst. Bez podrobností chyby.</summary>
    SourceUnavailable,
}

/// <summary>
/// Model stránky návštěvníka. Celý jde do prohlížeče, proto nese jen vyjmenovaná pole z veřejných objektů
/// (<c>docs/SPEC.md</c> sekce 10) a hotové texty. Nové pole je produktové rozhodnutí, seznam hlídá test.
/// </summary>
/// <param name="Message">Hláška, <c>null</c> = termíny jsou. U kalendáře může být spolu s ním, když v týdnu nic není.</param>
/// <param name="Views">Přepínač zobrazení, prázdný při jediném zobrazení.</param>
/// <param name="Agenda">Termíny po dnech.</param>
/// <param name="Calendar">Týden ve sloupcích dnů.</param>
public sealed record VisitorPage(PageMessage? Message, IReadOnlyList<ViewLink> Views, AgendaModel? Agenda, CalendarModel? Calendar)
{
    /// <summary>
    /// Název služby jednou nad termíny: služba je zvolená v adrese a termíny ji nenesou, to jsou volné úseky
    /// individuálních typů. <c>null</c> = nic nad termíny.
    /// </summary>
    public string? ServiceHeading { get; init; }

    /// <summary>Týden s hodinovou osou.</summary>
    public TimegridModel? Timegrid { get; init; }

    /// <summary>Formulář filtrů z <c>pickers</c>, <c>null</c> = návštěvník žádný filtr nepřepíná.</summary>
    public FilterForm? Filters { get; init; }

    /// <summary>Měsíční přehled se stavem dnů.</summary>
    public MonthModel? Month { get; init; }

    /// <summary>Věta s nejbližším volným termínem.</summary>
    public NextModel? Next { get; init; }

    /// <summary>Sestaví model z výsledku čtení. Časy a dny už jsou v pásmu merchanta, tady se jen formátují.</summary>
    /// <param name="request">Co návštěvník chce vidět.</param>
    /// <param name="result">Výsledek čtení.</param>
    /// <param name="formats">Kultura a formáty jazyka stránky.</param>
    public static VisitorPage From(VisitorRequest request, AvailabilityResult result, VisitorFormats formats)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(formats);

        PageMessage? message = MessageOf(result.Outcome);
        if (result.Period is not { } period || message is not (null or PageMessage.NothingFree))
        {
            return new VisitorPage(message ?? PageMessage.ViewNotAvailable, [], null, null);
        }

        // Filtry i u hlášky „nic volného“: návštěvník se musí mít jak vrátit k jiné volbě.
        return Shown(request, result, formats, message, period) with { Filters = FilterForm.Of(request, result.Filters) };
    }

    private static VisitorPage Shown(VisitorRequest request, AvailabilityResult result, VisitorFormats formats, PageMessage? message, AvailabilityPeriod period)
    {
        ViewLink[] views = request.Views.Count < 2
            ? []
            : [.. Enum.GetValues<DisplayView>().Where(request.Views.Contains).Select(view => new ViewLink(view, request.LinkTo(view), view == request.View))];
        // Lekce nesou službu u každého termínu, nad nimi by se jen opakovala.
        string? heading = result.Kind is SlotKind.FreeTime && result.Selection?.Service is { } service ? service.Name : null;
        ILookup<DateOnly, SlotModel> slots = result.Slots.ToLookup(
            slot => DateOnly.FromDateTime(slot.Start.DateTime),
            slot => SlotModel.Of(slot, formats.Culture));

        if (request.View is DisplayView.Agenda)
        {
            return message is not null
                ? new VisitorPage(message, views, null, null) { ServiceHeading = heading }
                : new VisitorPage(null, views, new AgendaModel([.. slots.Select(day => new AgendaDay(formats.Format(day.Key, formats.Day), day.Key == period.Today, [.. day]))]), null) { ServiceHeading = heading };
        }

        if (request.View is DisplayView.Next)
        {
            // Věta sama řekne i to, že nic volného není, hláška by ji opakovala.
            return new VisitorPage(null, views, null, null) { ServiceHeading = heading, Next = NextModel.Of(result, period, formats) };
        }

        if (request.View is DisplayView.Month)
        {
            return new VisitorPage(message, views, null, null) { ServiceHeading = heading, Month = MonthModel.Of(request, period, result, formats) };
        }

        if (request.View is DisplayView.Timegrid)
        {
            return new VisitorPage(message, views, null, null)
            {
                ServiceHeading = heading,
                Timegrid = TimegridModel.Of(request, period, result, formats, formats.StateText),
            };
        }

        return new VisitorPage(message, views, null, CalendarModel.Of(request, period, slots, formats)) { ServiceHeading = heading };
    }

    private static PageMessage? MessageOf(AvailabilityOutcome outcome) => outcome switch
    {
        AvailabilityOutcome.Available => null,
        AvailabilityOutcome.NothingFree => PageMessage.NothingFree,
        AvailabilityOutcome.UnsupportedSegment => PageMessage.UnsupportedSegment,
        AvailabilityOutcome.NotConnected => PageMessage.NotConnected,
        AvailabilityOutcome.SourceUnavailable => PageMessage.SourceUnavailable,
        _ => PageMessage.ViewNotAvailable,
    };
}

/// <summary>Kultura a formáty data jazyka stránky. Formáty jsou v textech, překladatel je upraví s jazykem.</summary>
/// <param name="Culture">Kultura jazyka stránky.</param>
/// <param name="Day">Nadpis dne v agendě.</param>
/// <param name="CalendarDay">Nadpis sloupce dne v kalendáři.</param>
/// <param name="WeekStart">Začátek týdne v nadpisu kalendáře.</param>
/// <param name="WeekEnd">Konec týdne v nadpisu kalendáře.</param>
public sealed record VisitorFormats(CultureInfo Culture, string Day, string CalendarDay, string WeekStart, string WeekEnd)
{
    /// <summary>Název stavu úseku hodinové osy v jazyce stránky.</summary>
    public Func<TimeBandState, string> StateText { get; init; } = state => state.ToString();

    /// <summary>Označení plně obsazeného termínu v jazyce stránky.</summary>
    public string FullText { get; init; } = "full";

    /// <summary>Text pod klíčem v jazyce stránky. Výchozí vrací klíč, stačí testům, které texty nekontrolují.</summary>
    public Func<string, string> Text { get; init; } = key => key;

    /// <summary>Měsíc a rok v nadpisu měsíčního přehledu.</summary>
    public string Month => Text("Visitor.Format.Month");

    /// <summary>Den ve formátu jazyka stránky.</summary>
    /// <param name="day">Den.</param>
    /// <param name="format">Formát.</param>
    public string Format(DateOnly day, string format) => day.ToString(format, Culture);
}

/// <summary>Odkaz přepínače zobrazení.</summary>
/// <param name="View">Zobrazení, jeho název je v textech pod <c>Visitor.View.{hodnota}</c>.</param>
/// <param name="Url">Relativní odkaz se stejnými ostatními parametry.</param>
/// <param name="IsCurrent">Zobrazení je právě vidět.</param>
public sealed record ViewLink(DisplayView View, string Url, bool IsCurrent);

/// <summary>Týden ve sloupcích dnů od pondělí.</summary>
/// <param name="Nav">Navigace týdnů.</param>
/// <param name="Days">Sedm dní od pondělí.</param>
public sealed record CalendarModel(RangeNav Nav, IReadOnlyList<CalendarDay> Days)
{
    internal static CalendarModel Of(VisitorRequest request, AvailabilityPeriod period, ILookup<DateOnly, SlotModel> slots, VisitorFormats formats) => new(
        RangeNav.Of(request, period, ShownRange.Week, formats),
        [.. period.ShownDays().Select(day => new CalendarDay(
            formats.Format(day, formats.CalendarDay),
            day == period.Today,
            period.IsOffered(day),
            [.. slots[day]]))]);
}

/// <summary>Den kalendáře.</summary>
/// <param name="Heading">Nadpis dne v jazyce stránky.</param>
/// <param name="IsToday">Den je dnešek v pásmu merchanta.</param>
/// <param name="IsOffered">Den patří do nabízeného období. Den mimo něj nemá termíny a je označený.</param>
/// <param name="Slots">Termíny podle času.</param>
public sealed record CalendarDay(string Heading, bool IsToday, bool IsOffered, IReadOnlyList<SlotModel> Slots);

/// <summary>Agenda: termíny po dnech, dny bez termínů vynechané.</summary>
/// <param name="Days">Dny v pořadí.</param>
public sealed record AgendaModel(IReadOnlyList<AgendaDay> Days);

/// <summary>Den agendy.</summary>
/// <param name="Heading">Nadpis dne v jazyce stránky.</param>
/// <param name="IsToday">Den je dnešek v pásmu merchanta.</param>
/// <param name="Slots">Termíny podle času.</param>
public sealed record AgendaDay(string Heading, bool IsToday, IReadOnlyList<SlotModel> Slots);

/// <summary>Termín pro návštěvníka.</summary>
/// <param name="Time">Čas od–do v jazyce stránky.</param>
/// <param name="ServiceName">Název služby, <c>null</c> u volného úseku zdroje.</param>
/// <param name="ResourceName">Název zdroje.</param>
/// <param name="OccupancyText">Obsazenost ve formátu merchanta.</param>
/// <param name="IsFull">Termín je plně obsazený.</param>
public sealed record SlotModel(string Time, string? ServiceName, string ResourceName, string? OccupancyText, bool IsFull)
{
    internal static SlotModel Of(AvailabilitySlot slot, CultureInfo culture) => new(
        TimeText.Range(slot.Start, slot.End, culture),
        slot.ServiceName,
        slot.ResourceName,
        slot.OccupancyText,
        slot.IsFull);
}
