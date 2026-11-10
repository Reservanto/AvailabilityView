using System.Globalization;
using AvailabilityView.Application.Availability;

namespace AvailabilityView.Web.Availability;

/// <summary>
/// Týden s hodinovou osou. Pozice jsou v řádcích po <see cref="MinutesPerRow"/>
/// minutách od začátku osy, styl je převede na mřížku: na široké stránce svisle, na úzké vodorovně. Inline styly CSP
/// nedovolí, proto pozice nesou třídy.
/// </summary>
/// <param name="Nav">Navigace týdnů.</param>
/// <param name="Rows">Počet řádků osy.</param>
/// <param name="Hours">Celé hodiny na ose.</param>
/// <param name="Days">Sedm dní od pondělí.</param>
/// <param name="Legend">Stavy úseků, které týden obsahuje, v pořadí volno, obsazeno, nedostupné.</param>
public sealed record TimegridModel(
    RangeNav Nav,
    int Rows,
    IReadOnlyList<TimegridHour> Hours,
    IReadOnlyList<TimegridDay> Days,
    IReadOnlyList<TimeBandState> Legend)
{
    /// <summary>Jemnost osy. API vrací časy po pěti minutách, kratší úsek se zaokrouhlí.</summary>
    public const int MinutesPerRow = 5;

    /// <summary>Nejvíc souběžných termínů vedle sebe, další se překrývají s posledním.</summary>
    public const int MaxLanes = 4;

    // Osa bez úseků ukáže běžnou pracovní dobu, aby prázdný týden nebyl bez měřítka.
    private const int DefaultFirstHour = 8;
    private const int DefaultLastHour = 17;

    internal static TimegridModel Of(
        VisitorRequest request,
        AvailabilityPeriod period,
        AvailabilityResult result,
        VisitorFormats formats,
        Func<TimeBandState, string> stateText)
    {
        CultureInfo culture = formats.Culture;
        DateOnly[] days = [.. period.ShownDays()];

        // Lekce nemají pracovní dobu, na ose jsou samy termíny. Individuální typy mají úseky, úsek přes půlnoc nese
        // v každém dni svou část (rozhodnutí uživatele 2026-11-09).
        List<Piece> pieces = result.Kind is SlotKind.FreeTime
            ? [.. result.Bands.SelectMany(band => Split(band.Start, band.End, byDuration: false)
                .Select(part => new Piece(part.Day, part.From, part.To, StateClass.Of(band.State), band.State == TimeBandState.Free
                    ? TimeText.Range(part.From, part.To, culture)
                    : stateText(band.State), $"{stateText(band.State)} {TimeText.Range(part.From, part.To, culture)}", band.State == TimeBandState.Free)))]
            : [.. result.Slots.SelectMany(slot => Split(slot.Start, slot.End, byDuration: true)
                .Select(part => new Piece(part.Day, part.From, part.To, slot.IsFull ? "lesson-full" : "lesson", Lesson(slot, culture), slot.IsFull ? $"{Lesson(slot, culture)}, {formats.FullText}" : Lesson(slot, culture), true)))];

        // Lekce přes půlnoc by jinak část ukázala i v dni mimo nabídku.
        pieces.RemoveAll(piece => !period.IsOffered(piece.Day));

        int first = pieces.Count > 0 ? Math.Min(pieces.Min(piece => piece.From) / 60, DefaultFirstHour) : DefaultFirstHour;
        int last = pieces.Count > 0 ? Math.Max((pieces.Max(piece => piece.To) + 59) / 60, DefaultLastHour) : DefaultLastHour;
        int rows = (last - first) * 60 / MinutesPerRow;
        int Row(int minute) => Math.Clamp((minute - (first * 60)) / MinutesPerRow, 0, rows);

        return new TimegridModel(
            RangeNav.Of(request, period, ShownRange.Week, formats),
            rows,
            [.. Enumerable.Range(first, last - first + 1).Select(hour => new TimegridHour(
                new TimeOnly(hour % 24, 0).ToString("t", culture), Row(hour * 60)))],
            [.. days.Select(day =>
            {
                Piece[] own = [.. pieces.Where(piece => piece.Day == day).OrderBy(piece => piece.From)];
                int[] lanes = Lanes(own);
                int laneCount = lanes.Length == 0 ? 1 : lanes.Max() + 1;
                return new TimegridDay(
                    formats.Format(day, formats.CalendarDay),
                    day == period.Today,
                    period.IsOffered(day),
                    laneCount,
                    [.. own.Select((piece, index) => new TimegridBlock(Row(piece.From), Row(piece.To), lanes[index], piece.Kind, piece.Text, piece.Description))
                        .Where(block => block.To > block.From)],
                    [.. own.Where(piece => piece.Listed).Select(piece => new TimegridItem(piece.Kind, piece.Text))]);
            })],
            [.. Enum.GetValues<TimeBandState>().Where(state => result.Bands.Any(band => band.State == state))]);
    }

    private static string Lesson(AvailabilitySlot slot, CultureInfo culture) =>
        string.Join(" ", new[] { TimeText.Range(slot.Start, slot.End, culture), slot.ServiceName, slot.ResourceName, slot.OccupancyText }.Where(text => !string.IsNullOrEmpty(text)));

    // Úsek přes půlnoc patří do dvou dnů. Minuty se počítají podle místních hodin, den s přechodem času tak sedí na osu.
    // Lekce uvnitř opakované hodiny při podzimním přechodu (2:30 letního až 2:30 zimního času) má na místních hodinách
    // nulovou délku, kreslí se proto podle skutečné délky. Souběžné lekce jdou vedle sebe, překryv nevadí. Úseky
    // individuálních typů na sebe navazují: delší kreslení by překrylo sousední úsek jiného stavu, proto zůstávají
    // na místních hodinách a úsek celý uvnitř opakované hodiny se nenakreslí.
    private static IEnumerable<(DateOnly Day, int From, int To)> Split(DateTimeOffset start, DateTimeOffset end, bool byDuration)
    {
        var day = DateOnly.FromDateTime(start.DateTime);
        var lastDay = DateOnly.FromDateTime(end.DateTime);
        int from = (start.Hour * 60) + start.Minute;
        while (day < lastDay)
        {
            yield return (day, from, 24 * 60);
            day = day.AddDays(1);
            from = 0;
        }

        int to = (end.Hour * 60) + end.Minute;
        if (byDuration && to <= from && day == DateOnly.FromDateTime(start.DateTime) && end > start)
        {
            to = Math.Min(24 * 60, from + (int)Math.Ceiling((end - start).TotalMinutes));
        }

        if (to > from)
        {
            yield return (day, from, to);
        }
    }

    // Souběžné termíny jdou vedle sebe. Úseky individuálních typů se nepřekrývají, dostanou jediný pruh.
    private static int[] Lanes(Piece[] pieces)
    {
        int[] lanes = new int[pieces.Length];
        List<int> laneEnds = [];
        for (int index = 0; index < pieces.Length; index++)
        {
            int lane = laneEnds.FindIndex(end => end <= pieces[index].From);
            if (lane < 0)
            {
                lane = Math.Min(laneEnds.Count, MaxLanes - 1);
                if (lane == laneEnds.Count)
                {
                    laneEnds.Add(0);
                }
            }

            laneEnds[lane] = Math.Max(laneEnds[lane], pieces[index].To);
            lanes[index] = lane;
        }

        return lanes;
    }

    private sealed record Piece(DateOnly Day, int From, int To, string Kind, string Text, string Description, bool Listed);
}

/// <summary>Celá hodina na ose.</summary>
/// <param name="Label">Čas v jazyce stránky.</param>
/// <param name="Row">Řádek osy.</param>
public sealed record TimegridHour(string Label, int Row);

/// <summary>Den hodinové osy.</summary>
/// <param name="Heading">Nadpis dne v jazyce stránky.</param>
/// <param name="IsToday">Den je dnešek v pásmu merchanta.</param>
/// <param name="IsOffered">Den patří do nabízeného období. Den mimo něj nemá úseky a je označený.</param>
/// <param name="Lanes">Počet souběžných pruhů.</param>
/// <param name="Blocks">Úseky a termíny podle času.</param>
/// <param name="Listed">Volné úseky nebo termíny textem pro úzkou šířku, kde časy v pruhu nejdou číst.</param>
public sealed record TimegridDay(string Heading, bool IsToday, bool IsOffered, int Lanes, IReadOnlyList<TimegridBlock> Blocks, IReadOnlyList<TimegridItem> Listed);

/// <summary>Volný úsek nebo termín textem pod pruhem dne.</summary>
/// <param name="Kind">Druh pro třídu <c>av-timegrid__item--{Kind}</c>, jako u <see cref="TimegridBlock.Kind"/>.</param>
/// <param name="Text">Text.</param>
public sealed record TimegridItem(string Kind, string Text);

/// <summary>Úsek nebo termín na ose.</summary>
/// <param name="From">Řádek začátku.</param>
/// <param name="To">Řádek konce, už do úseku nepatří.</param>
/// <param name="Lane">Pruh od 0.</param>
/// <param name="Kind">Druh pro třídu <c>av-block--{Kind}</c>: free, busy, unavailable, lesson, lesson-full.</param>
/// <param name="Text">Viditelný text.</param>
/// <param name="Description">Úplný popis pro čtečky a nápovědu.</param>
public sealed record TimegridBlock(int From, int To, int Lane, string Kind, string Text, string Description);
