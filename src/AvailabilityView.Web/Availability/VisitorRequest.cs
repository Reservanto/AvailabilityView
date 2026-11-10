using System.Globalization;
using AvailabilityView.Application.Availability;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Primitives;

namespace AvailabilityView.Web.Availability;

/// <summary>Zobrazení stránky návštěvníka (<c>docs/SPEC.md</c> sekce 3).</summary>
public enum DisplayView
{
    /// <summary>Seznam termínů po dnech.</summary>
    Agenda,

    /// <summary>Týden ve sloupcích dnů.</summary>
    Calendar,

    /// <summary>Týden s hodinovou osou.</summary>
    Timegrid,

    /// <summary>Měsíční přehled se stavem dnů.</summary>
    Month,

    /// <summary>Věta s nejbližším volným termínem.</summary>
    Next,
}

/// <summary>Co se má zobrazit, převedené z adresy.</summary>
/// <param name="Query">Dotaz na čtení. Pro adresu s hodnotou v neplatném tvaru <see cref="AvailabilityQuery.Malformed"/>.</param>
/// <param name="Views">Zobrazení, která merchant nabízí. Aspoň jedno, první se ukáže.</param>
/// <param name="Language">Jazyk stránky, jeden z <see cref="AvailabilitySettings.Languages"/>.</param>
public sealed record VisitorRequest(AvailabilityQuery Query, IReadOnlyList<DisplayView> Views, string Language)
{
    /// <summary>Zobrazení, které se ukáže.</summary>
    public DisplayView View => Views[0];

    /// <summary>Kolik dní se nabízí, když adresa neříká (<c>docs/SPEC.md</c> sekce 5).</summary>
    public const int DefaultDays = 14;

    /// <summary>
    /// Jediné místo, které čte parametry URL, názvy a formáty jsou v <see cref="VisitorParameters"/>. Chybějící nebo prázdná hodnota dá výchozí, prázdnou
    /// posílá formulář filtrů za volbu „všechny“. Hodnota v neplatném tvaru dá stejný výsledek jako neexistující objekt.
    /// Neznámé parametry se nechají být, weby merchantů do adresy přidávají vlastní (třeba měření kampaní).
    /// </summary>
    /// <param name="query">Parametry adresy.</param>
    /// <param name="settings">Nastavení instance.</param>
    public static VisitorRequest From(IQueryCollection query, AvailabilitySettings settings)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(settings);

        string language = LanguageFrom(query, settings);
        var reader = new Reader(query);
        DisplayView[] views = reader.List(VisitorParameters.View, VisitorParameters.Views) ?? [DisplayView.Agenda];
        AvailabilityFilter[] pickers = reader.List(VisitorParameters.Pickers, VisitorParameters.Filters) ?? [];
        int? segment = reader.Id(VisitorParameters.NameOf(AvailabilityFilter.Segment));
        int? location = reader.Id(VisitorParameters.NameOf(AvailabilityFilter.Location));
        int? service = reader.Id(VisitorParameters.NameOf(AvailabilityFilter.Service));
        int? resource = reader.Id(VisitorParameters.NameOf(AvailabilityFilter.Resource));
        int days = reader.Days(VisitorParameters.Days) ?? DefaultDays;
        DateOnly? week = reader.Date(VisitorParameters.Week);
        DateOnly? month = reader.Month(VisitorParameters.Month);
        if (reader.IsMalformed)
        {
            return new VisitorRequest(AvailabilityQuery.Malformed, views, language) { Parameters = ParametersOf(query) };
        }

        // Agenda a nejbližší termín ukazují celé období, týden a měsíc jen svůj rozsah.
        return new VisitorRequest(
            new AvailabilityQuery(segment, location, service, resource, days)
            {
                Range = views[0] switch
                {
                    DisplayView.Calendar or DisplayView.Timegrid => new RangeQuery(ShownRange.Week, week),
                    DisplayView.Month => new RangeQuery(ShownRange.Month, month),
                    _ => null,
                },
                Timeline = views[0] is DisplayView.Timegrid,
                Pickers = pickers.ToHashSet(),
            },
            views,
            language)
        {
            Parameters = ParametersOf(query),
        };
    }

    /// <summary>
    /// Odkaz na stejný pohled v jiném zobrazení. Zvolené zobrazení jde na začátek seznamu, pořadí
    /// ostatních i všechny ostatní parametry zůstanou.
    /// </summary>
    /// <param name="view">Zobrazení z <see cref="Views"/>.</param>
    public string LinkTo(DisplayView view) => Link((VisitorParameters.View, ViewsStartingWith(view)));

    /// <summary>Odkaz na týden nebo měsíc se stejnými ostatními parametry.</summary>
    /// <param name="unit">Jednotka rozsahu.</param>
    /// <param name="start">První den rozsahu.</param>
    public string LinkToRange(ShownRange unit, DateOnly start) => unit is ShownRange.Month
        ? Link((VisitorParameters.Month, VisitorParameters.FormatMonth(start)))
        : Link((VisitorParameters.Week, VisitorParameters.FormatDate(start)));

    /// <summary>
    /// Odkaz ze dne měsíce na jeho týden v jiném zobrazení. Zobrazení jde na začátek seznamu jako
    /// u <see cref="LinkTo"/>.
    /// </summary>
    /// <param name="view">Týdenní zobrazení z <see cref="Views"/>.</param>
    /// <param name="day">Den v týdnu.</param>
    public string LinkToWeekIn(DisplayView view, DateOnly day) =>
        Link((VisitorParameters.View, ViewsStartingWith(view)), (VisitorParameters.Week, VisitorParameters.FormatDate(day)));

    private string ViewsStartingWith(DisplayView view) =>
        string.Join(',', Views.Where(item => item == view).Concat(Views.Where(item => item != view)).Select(VisitorParameters.NameOf));

    // Parametry tak, jak přišly, i neznámé: odkaz má vést na stejný pohled, jen s jinou jednou hodnotou.
    private IReadOnlyList<KeyValuePair<string, string>> Parameters { get; init; } = [];

    private static KeyValuePair<string, string>[] ParametersOf(IQueryCollection query) =>
        [.. query.SelectMany(pair => pair.Value.Select(value => KeyValuePair.Create(pair.Key, value ?? string.Empty)))];

    /// <summary>
    /// Parametry adresy kromě vyjmenovaných, i neznámé. Formulář filtrů je pošle skryté, aby po odeslání zůstal stejný
    /// pohled, jen s jinými hodnotami filtrů.
    /// </summary>
    /// <param name="names">Parametry, které formulář posílá sám.</param>
    internal IReadOnlyList<KeyValuePair<string, string>> ParametersExcept(IReadOnlyCollection<string> names) =>
        [.. Parameters.Where(pair => !names.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))];

    // Relativní odkaz jen s parametry: platí i pod podadresářem IIS aplikace.
    private string Link(params (string Name, string Value)[] changes)
    {
        var builder = new QueryBuilder(Parameters.Where(pair =>
            !changes.Any(change => string.Equals(pair.Key, change.Name, StringComparison.OrdinalIgnoreCase))));
        foreach ((string name, string value) in changes)
        {
            builder.Add(name, value);
        }

        return builder.ToQueryString().Value!;
    }

    /// <summary>
    /// Jazyk z parametru <c>lang</c>. Neznámý dá výchozí, jazyk prohlížeče se nebere v úvahu.
    /// Volá ho i <see cref="VisitorLanguageProvider"/>, aby jazyk textů a formát data určovalo stejné pravidlo.
    /// </summary>
    /// <param name="query">Parametry adresy.</param>
    /// <param name="settings">Nastavení instance.</param>
    public static string LanguageFrom(IQueryCollection query, AvailabilitySettings settings)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(settings);

        return Single(query, VisitorParameters.Language) is { } lang && AvailabilitySettings.Languages.Contains(lang, StringComparer.Ordinal)
            ? lang
            : settings.DefaultLanguage;
    }

    private static string? Single(IQueryCollection query, string name) =>
        query.TryGetValue(name, out StringValues values) && values.Count == 1 && !string.IsNullOrEmpty(values[0]) ? values[0] : null;

    private sealed class Reader(IQueryCollection query)
    {
        private readonly HashSet<string> _invalid = new(StringComparer.Ordinal);

        public bool IsMalformed => _invalid.Count > 0;

        public int? Id(string name) =>
            Text(name) is not { } text ? null
            : int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int id) ? id
            : Invalid<int?>(name);

        // Číslo nad rozsah je pořád počet dní nad MaxDays a zkrátí se jako ostatní.
        public int? Days(string name) =>
            Text(name) is not { } text ? null
            : int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int days) ? days
            : text.All(char.IsAsciiDigit) ? int.MaxValue
            : Invalid<int?>(name);

        public DateOnly? Date(string name) =>
            Text(name) is not { } text ? null
            : DateOnly.TryParseExact(text, VisitorParameters.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date) ? date
            : Invalid<DateOnly?>(name);

        // Měsíc jako první den měsíce. Den navíc je neplatný tvar, ne přesnější měsíc.
        public DateOnly? Month(string name) =>
            Text(name) is not { } text ? null
            : DateOnly.TryParseExact(text + "-01", VisitorParameters.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly first)
                ? first
            : Invalid<DateOnly?>(name);

        /// <summary>Seznam oddělený čárkou. Neznámá, prázdná i opakovaná položka je neplatná.</summary>
        public T[]? List<T>(string name, IReadOnlyDictionary<string, T> known)
        {
            if (Text(name) is not { } text)
            {
                return null;
            }

            string[] items = text.Split(',');
            return items.All(known.ContainsKey) && items.Distinct(StringComparer.Ordinal).Count() == items.Length
                ? [.. items.Select(item => known[item])]
                : Invalid<T[]>(name);
        }

        // Opakovaný parametr je neplatný: nejde poznat, kterou hodnotu merchant myslel.
        private string? Text(string name)
        {
            if (!query.TryGetValue(name, out StringValues values) || (values.Count == 1 && string.IsNullOrEmpty(values[0])))
            {
                return null;
            }

            return values.Count == 1 ? values[0] : Invalid<string>(name);
        }

        private T? Invalid<T>(string name)
        {
            _invalid.Add(name);
            return default;
        }
    }
}
