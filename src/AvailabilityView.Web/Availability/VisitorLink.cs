using System.Globalization;
using AvailabilityView.Application.Availability;

namespace AvailabilityView.Web.Availability;

/// <summary>
/// Pohled sestavený v generátoru. <see cref="Query"/> je adresa, kterou
/// <see cref="VisitorRequest.From"/> přečte na stejný pohled, hlídá to test oběhu. Výchozí hodnoty se do adresy
/// nepíšou, aby byl odkaz krátký.
/// </summary>
/// <param name="Views">Zobrazení, první je výchozí. Aspoň jedno, bez opakování.</param>
/// <param name="SegmentId">Zaměření.</param>
/// <param name="LocationId">Provozovna.</param>
/// <param name="ServiceId">Služba, <c>null</c> = všechny.</param>
/// <param name="ResourceId">Zdroj, <c>null</c> = všechny.</param>
/// <param name="Pickers">Filtry, které si návštěvník přepíná sám.</param>
/// <param name="Days">Kolik dní dopředu.</param>
/// <param name="Language">Jazyk, <c>null</c> = výchozí jazyk instance.</param>
public sealed record VisitorLink(
    IReadOnlyList<DisplayView> Views,
    int? SegmentId,
    int? LocationId,
    int? ServiceId,
    int? ResourceId,
    IReadOnlyList<AvailabilityFilter> Pickers,
    int Days,
    string? Language)
{
    /// <summary>Parametry adresy včetně otazníku, prázdné bez parametrů. Pořadí je pevné.</summary>
    public string Query()
    {
        (string Name, IEnumerable<string>? Values)[] parameters =
        [
            (VisitorParameters.View, Views is [DisplayView.Agenda] ? null : Views.Select(VisitorParameters.NameOf)),
            (VisitorParameters.NameOf(AvailabilityFilter.Segment), Id(SegmentId)),
            (VisitorParameters.NameOf(AvailabilityFilter.Location), Id(LocationId)),
            (VisitorParameters.NameOf(AvailabilityFilter.Service), Id(ServiceId)),
            (VisitorParameters.NameOf(AvailabilityFilter.Resource), Id(ResourceId)),
            (VisitorParameters.Pickers, Pickers.Count == 0 ? null : Pickers.Order().Select(VisitorParameters.NameOf)),
            (VisitorParameters.Days, Days == VisitorRequest.DefaultDays ? null : [Days.ToString(CultureInfo.InvariantCulture)]),
            (VisitorParameters.Language, Language is null ? null : [Language]),
        ];

        // Čárka v seznamu zůstane čitelná, hodnoty se kódují každá zvlášť.
        string[] written = [.. parameters
            .Where(parameter => parameter.Values is not null)
            .Select(parameter => $"{parameter.Name}={string.Join(',', parameter.Values!.Select(Uri.EscapeDataString))}")];
        return written.Length == 0 ? string.Empty : "?" + string.Join('&', written);
    }

    private static string[]? Id(int? id) => id is { } value ? [value.ToString(CultureInfo.InvariantCulture)] : null;
}
