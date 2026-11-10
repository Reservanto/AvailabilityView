using System.Globalization;
using AvailabilityView.Application.Availability;
using AvailabilityView.Web.Resources;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace AvailabilityView.Web.Availability;

/// <summary>Hlavní stránka: volná kapacita merchanta pro návštěvníka (<c>docs/SPEC.md</c> sekce 3).</summary>
[Route("")]
[VisitorPage]
public sealed class VisitorController(IAvailabilityReader reader, AvailabilitySettings settings, IStringLocalizer<SharedResource> text) : Controller
{
    /// <summary>
    /// Každý stav vrací 200. Hláška je obsah stránky ve webu merchanta a IIS by stránku s chybovým kódem mohl
    /// nahradit vlastní. Stejný kód má i „pohled není dostupný“, odpověď tak nic neprozradí.
    /// </summary>
    /// <param name="ct">Zrušení požadavku.</param>
    [HttpGet("")]
    public async Task<IActionResult> IndexAsync(CancellationToken ct)
    {
        var request = VisitorRequest.From(Request.Query, settings);
        AvailabilityResult result = await reader.ReadAsync(request.Query, ct);
        var formats = new VisitorFormats(
            CultureInfo.CurrentCulture,
            text["Visitor.Format.Day"],
            text["Visitor.Format.CalendarDay"],
            text["Visitor.Format.WeekStart"],
            text["Visitor.Format.WeekEnd"])
        {
            StateText = state => text[$"Visitor.State.{state}"],
            FullText = text["Visitor.Full"],
            Text = key => text[key],
        };
        return View("Index", VisitorPage.From(request, result, formats));
    }
}
