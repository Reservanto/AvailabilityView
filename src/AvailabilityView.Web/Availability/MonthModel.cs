using System.Globalization;
using AvailabilityView.Application.Availability;

namespace AvailabilityView.Web.Availability;

/// <summary>
/// Měsíční přehled: mřížka od pondělí, řádek na týden, v buňce číslo dne a stav. Stav nese třída
/// a text pro čtečky. Den s volnem odkazuje na svůj týden v prvním týdenním zobrazení z <c>view</c>, bez něj odkaz nemá.
/// </summary>
/// <param name="Nav">Navigace měsíců.</param>
/// <param name="Weekdays">Dny týdne od pondělí.</param>
/// <param name="Weeks">Týdny měsíce.</param>
/// <param name="Legend">Vysvětlivky stavů v pořadí volno, plno, zavřeno.</param>
public sealed record MonthModel(RangeNav Nav, IReadOnlyList<MonthWeekday> Weekdays, IReadOnlyList<MonthWeek> Weeks, IReadOnlyList<MonthKey> Legend)
{
    internal static MonthModel Of(VisitorRequest request, AvailabilityPeriod period, AvailabilityResult result, VisitorFormats formats)
    {
        var states = result.Days.ToDictionary(item => item.Day, item => item.State);
        DisplayView? weekView = request.Views.Cast<DisplayView?>().FirstOrDefault(view => view is DisplayView.Calendar or DisplayView.Timegrid);
        DateTimeFormatInfo names = formats.Culture.DateTimeFormat;
        DateOnly first = period.ShownStart;
        int leading = ((int)first.DayOfWeek + 6) % 7;
        int cells = (leading + period.ShownDays().Count() + 6) / 7 * 7;

        string StateText(DayState state) => state is DayState.Closed && result.Kind is SlotKind.Lesson
            ? formats.Text("Visitor.Day.NoLessons")
            : formats.Text($"Visitor.Day.{state}");

        MonthDay? Cell(int index)
        {
            DateOnly day = first.AddDays(index - leading);
            if (day < period.ShownStart || day >= period.ShownEnd)
            {
                return null;
            }

            DayState? state = period.IsOffered(day) && states.TryGetValue(day, out DayState found) ? found : null;
            string kind = state is { } offered ? StateClass.Of(offered) : "outside";
            string stateText = state is { } known ? StateText(known) : formats.Text("Visitor.Outside");
            return new MonthDay(
                day.Day.ToString(formats.Culture),
                kind,
                $"{formats.Format(day, formats.Day)}, {stateText}",
                day == period.Today,
                kind == "free" && weekView is { } view ? request.LinkToWeekIn(view, day) : null);
        }

        return new MonthModel(
            RangeNav.Of(request, period, ShownRange.Month, formats),
            [.. Enumerable.Range(1, 7).Select(offset => (DayOfWeek)(offset % 7))
                .Select(day => new MonthWeekday(names.GetAbbreviatedDayName(day), names.GetDayName(day)))],
            [.. Enumerable.Range(0, cells / 7).Select(week => new MonthWeek([.. Enumerable.Range(week * 7, 7).Select(Cell)]))],
            [.. Enum.GetValues<DayState>().Select(state => new MonthKey(StateClass.Of(state), StateText(state)))]);
    }
}

/// <summary>Vysvětlivka stavu dne.</summary>
/// <param name="Kind">Stav jako u <see cref="MonthDay.Kind"/>.</param>
/// <param name="Text">Název stavu v jazyce stránky.</param>
public sealed record MonthKey(string Kind, string Text);

/// <summary>Den týdne v záhlaví měsíce.</summary>
/// <param name="Abbreviation">Zkratka.</param>
/// <param name="Full">Celý název pro čtečky.</param>
public sealed record MonthWeekday(string Abbreviation, string Full);

/// <summary>Týden měsíce.</summary>
/// <param name="Days">Sedm dní od pondělí, <c>null</c> = den jiného měsíce.</param>
public sealed record MonthWeek(IReadOnlyList<MonthDay?> Days);

/// <summary>Den měsíce.</summary>
/// <param name="Number">Číslo dne.</param>
/// <param name="Kind">Stav pro třídu <c>av-month__day--{Kind}</c>: free, full, closed, outside.</param>
/// <param name="Description">Den a stav slovy pro čtečky.</param>
/// <param name="IsToday">Den je dnešek v pásmu merchanta.</param>
/// <param name="Url">Odkaz na týden dne, <c>null</c> = bez odkazu.</param>
public sealed record MonthDay(string Number, string Kind, string Description, bool IsToday, string? Url);

/// <summary>Věta s nejbližším volným termínem (rozhodnutí uživatele 2026-11-09).</summary>
/// <param name="Text">Hotová věta v jazyce stránky.</param>
/// <param name="Found">Volný termín v období je.</param>
public sealed record NextModel(string Text, bool Found)
{
    internal static NextModel Of(AvailabilityResult result, AvailabilityPeriod period, VisitorFormats formats)
    {
        CultureInfo culture = formats.Culture;
        if (result.Slots.FirstOrDefault(slot => !slot.IsFull) is not { } slot)
        {
            string until = formats.Format(period.OfferedEnd.AddDays(-1), formats.Text("Visitor.Format.Until"));
            return new NextModel(string.Format(culture, formats.Text("Visitor.Next.None"), until), false);
        }

        var day = DateOnly.FromDateTime(slot.Start.DateTime);
        string when = day == period.Today ? formats.Text("Visitor.Next.Today")
            : day == period.Today.AddDays(1) ? formats.Text("Visitor.Next.Tomorrow")
            : formats.Format(day, formats.Text("Visitor.Format.NextDay"));

        // U lekcí název služby, pokud ji neurčuje adresa. Volný úsek službu nemá (SPEC sekce 3).
        string service = result.Kind is SlotKind.Lesson && result.Selection?.Service is null && slot.ServiceName is { } name ? $", {name}" : string.Empty;
        return new NextModel(string.Format(culture, formats.Text("Visitor.Next.Found"), $"{when} {slot.Start.ToString("t", culture)}{service}"), true);
    }
}
