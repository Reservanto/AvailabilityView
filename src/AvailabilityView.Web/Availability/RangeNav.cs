using AvailabilityView.Application.Availability;

namespace AvailabilityView.Web.Availability;

/// <summary>
/// Navigace po týdnech nebo měsících, společná pro všechna zobrazení s rozsahem. Texty jsou pod
/// <c>Visitor.{Unit}.Navigation</c>, <c>.Previous</c> a <c>.Next</c>.
/// </summary>
/// <param name="Unit">Jednotka rozsahu.</param>
/// <param name="Title">Zobrazený rozsah v jazyce stránky.</param>
/// <param name="PreviousUrl">Odkaz na předchozí nabízený rozsah, <c>null</c> = žádný není.</param>
/// <param name="NextUrl">Odkaz na další nabízený rozsah, <c>null</c> = žádný není.</param>
public sealed record RangeNav(ShownRange Unit, string Title, string? PreviousUrl, string? NextUrl)
{
    internal static RangeNav Of(VisitorRequest request, AvailabilityPeriod period, ShownRange unit, VisitorFormats formats) => new(
        unit,
        unit is ShownRange.Month
            ? formats.Format(period.ShownStart, formats.Month)
            : $"{formats.Format(period.ShownStart, formats.WeekStart)} – {formats.Format(period.ShownEnd.AddDays(-1), formats.WeekEnd)}",
        period.Previous is { } previous ? request.LinkToRange(unit, previous) : null,
        period.Next is { } next ? request.LinkToRange(unit, next) : null);
}
