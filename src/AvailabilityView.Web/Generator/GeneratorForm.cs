using System.Globalization;
using AvailabilityView.Application.Availability;
using AvailabilityView.Web.Availability;
using Microsoft.Extensions.Primitives;

namespace AvailabilityView.Web.Generator;

/// <summary>
/// Volby generátoru z adresy. Formulář je GET na stejnou stránku, funguje tak bez JavaScriptu
/// i pod podadresářem IIS. Parametry se jmenují jako u stránky návštěvníka, kde to jde. Generátor je nástroj správce:
/// neplatná nebo chybějící hodnota dá výchozí, ne hlášku.
/// </summary>
/// <param name="View">Výchozí zobrazení.</param>
/// <param name="Also">Další zobrazení, na která návštěvník přepne, v pořadí <see cref="DisplayView"/>.</param>
/// <param name="SegmentId">Zaměření, <c>null</c> = první vhodné.</param>
/// <param name="LocationId">Provozovna, <c>null</c> = první vhodná.</param>
/// <param name="ServiceId">Služba, <c>null</c> = všechny.</param>
/// <param name="ResourceId">Zdroj, <c>null</c> = všechny.</param>
/// <param name="Pickers">Filtry, které si návštěvník přepíná sám, v pořadí <see cref="AvailabilityFilter"/>.</param>
/// <param name="Days">Kolik dní dopředu, od 1 do <see cref="AvailabilitySettings.MaxDays"/>.</param>
/// <param name="Language">Jazyk stránky.</param>
public sealed record GeneratorForm(
    DisplayView View,
    IReadOnlyList<DisplayView> Also,
    int? SegmentId,
    int? LocationId,
    int? ServiceId,
    int? ResourceId,
    IReadOnlyList<AvailabilityFilter> Pickers,
    int Days,
    string Language)
{
    /// <summary>Parametr s dalšími zobrazeními, každé zvlášť jako zaškrtávací pole.</summary>
    public const string AlsoParameter = "also";

    /// <summary>Volby z adresy generátoru.</summary>
    /// <param name="query">Parametry adresy.</param>
    /// <param name="settings">Nastavení instance.</param>
    public static GeneratorForm From(IQueryCollection query, AvailabilitySettings settings)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(settings);

        DisplayView view = Known(query[VisitorParameters.View], VisitorParameters.Views).FirstOrDefault(DisplayView.Agenda);
        string? language = query[VisitorParameters.Language].FirstOrDefault(AvailabilitySettings.Languages.Contains);
        int days = Number(query, VisitorParameters.Days) ?? VisitorRequest.DefaultDays;
        return new GeneratorForm(
            view,
            [.. Known(query[AlsoParameter], VisitorParameters.Views).Where(item => item != view).Distinct().Order()],
            Number(query, VisitorParameters.NameOf(AvailabilityFilter.Segment)),
            Number(query, VisitorParameters.NameOf(AvailabilityFilter.Location)),
            Number(query, VisitorParameters.NameOf(AvailabilityFilter.Service)),
            Number(query, VisitorParameters.NameOf(AvailabilityFilter.Resource)),
            [.. Known(query[VisitorParameters.Pickers], VisitorParameters.Filters).Distinct().Order()],
            Math.Clamp(days, 1, settings.MaxDays),
            language ?? settings.DefaultLanguage);
    }

    /// <summary>
    /// Dotaz na nabídku. Všechny filtry jako filtry návštěvníka: nabídka každého filtru tak závisí jen na vyšších
    /// a hodnota, která k vyšším nepatří, se zahodí a doplní znovu (<c>docs/SPEC.md</c> sekce 5).
    /// </summary>
    public AvailabilityQuery OptionsQuery() => new(SegmentId, LocationId, ServiceId, ResourceId, Days)
    {
        Pickers = Enum.GetValues<AvailabilityFilter>().ToHashSet(),
    };

    /// <summary>Některý filtr má hodnotu.</summary>
    public bool HasChoice() => SegmentId is not null || LocationId is not null || ServiceId is not null || ResourceId is not null;

    /// <summary>Stejné volby bez hodnot filtrů.</summary>
    public GeneratorForm WithoutChoice() => this with { SegmentId = null, LocationId = null, ServiceId = null, ResourceId = null };

    /// <summary>
    /// Odkaz na pohled. Zaměření a provozovna jsou vždy pevné podle doplněných voleb, odkaz se tak nezmění, když
    /// merchant přidá další zaměření. Výchozí jazyk instance se do odkazu nepíše.
    /// </summary>
    /// <param name="selection">Volby po doplnění.</param>
    /// <param name="settings">Nastavení instance.</param>
    public VisitorLink LinkTo(AvailabilitySelection selection, AvailabilitySettings settings)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(settings);

        return new VisitorLink(
            [View, .. Also],
            selection.Segment.Id,
            selection.Location.Id,
            selection.Service?.Id,
            selection.Resource?.Id,
            Pickers,
            Days,
            Language == settings.DefaultLanguage ? null : Language);
    }

    private static IEnumerable<T> Known<T>(StringValues values, IReadOnlyDictionary<string, T> known) =>
        values.Where(value => value is not null && known.ContainsKey(value)).Select(value => known[value!]);

    private static int? Number(IQueryCollection query, string name) =>
        int.TryParse(query[name].FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out int id) ? id : null;
}
