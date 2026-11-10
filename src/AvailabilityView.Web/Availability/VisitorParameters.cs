using System.Globalization;
using AvailabilityView.Application.Availability;

namespace AvailabilityView.Web.Availability;

/// <summary>
/// Parametry adresy stránky návštěvníka na jednom místě: názvy, hodnoty seznamů a formáty data.
/// Čte je <see cref="VisitorRequest"/>, generátor podle nich skládá odkaz (<see cref="VisitorLink"/>). Jsou veřejné
/// rozhraní pro merchanty (<c>docs/SPEC.md</c> sekce 5), změna rozbije jejich odkazy.
/// </summary>
public static class VisitorParameters
{
    /// <summary>Zobrazení, první je výchozí.</summary>
    public const string View = "view";

    /// <summary>Filtry, které si návštěvník přepíná sám.</summary>
    public const string Pickers = "pickers";

    /// <summary>Kolik dní dopředu.</summary>
    public const string Days = "days";

    /// <summary>Den v zobrazeném týdnu.</summary>
    public const string Week = "week";

    /// <summary>Zobrazený měsíc.</summary>
    public const string Month = "month";

    /// <summary>Jazyk stránky.</summary>
    public const string Language = "lang";

    /// <summary>Tvar <see cref="Week"/>.</summary>
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>Tvar <see cref="Month"/>.</summary>
    public const string MonthFormat = "yyyy-MM";

    /// <summary>Hodnoty <see cref="View"/>.</summary>
    internal static IReadOnlyDictionary<string, DisplayView> Views { get; } = new Dictionary<string, DisplayView>(StringComparer.Ordinal)
    {
        ["agenda"] = DisplayView.Agenda,
        ["calendar"] = DisplayView.Calendar,
        ["timegrid"] = DisplayView.Timegrid,
        ["month"] = DisplayView.Month,
        ["next"] = DisplayView.Next,
    };

    /// <summary>Hodnoty <see cref="Pickers"/>. Stejný název má parametr s hodnotou filtru.</summary>
    internal static IReadOnlyDictionary<string, AvailabilityFilter> Filters { get; } = new Dictionary<string, AvailabilityFilter>(StringComparer.Ordinal)
    {
        ["segment"] = AvailabilityFilter.Segment,
        ["location"] = AvailabilityFilter.Location,
        ["service"] = AvailabilityFilter.Service,
        ["resource"] = AvailabilityFilter.Resource,
    };

    /// <summary>Hodnota zobrazení v <see cref="View"/>.</summary>
    /// <param name="view">Zobrazení.</param>
    public static string NameOf(DisplayView view) => Views.First(pair => pair.Value == view).Key;

    /// <summary>Název parametru s hodnotou filtru, zároveň hodnota v <see cref="Pickers"/>.</summary>
    /// <param name="filter">Filtr.</param>
    public static string NameOf(AvailabilityFilter filter) => Filters.First(pair => pair.Value == filter).Key;

    /// <summary>Den v tvaru <see cref="DateFormat"/>.</summary>
    /// <param name="day">Den.</param>
    public static string FormatDate(DateOnly day) => day.ToString(DateFormat, CultureInfo.InvariantCulture);

    /// <summary>Měsíc dne v tvaru <see cref="MonthFormat"/>.</summary>
    /// <param name="day">Den v měsíci.</param>
    public static string FormatMonth(DateOnly day) => day.ToString(MonthFormat, CultureInfo.InvariantCulture);
}
