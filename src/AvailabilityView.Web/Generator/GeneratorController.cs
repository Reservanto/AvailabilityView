using System.Resources;
using AvailabilityView.Application.Availability;
using AvailabilityView.Web.Availability;
using AvailabilityView.Web.Resources;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace AvailabilityView.Web.Generator;

/// <summary>
/// Generátor odkazu a kódu iframe (<c>docs/SPEC.md</c> sekce 7). Česky, bez přihlášení: nic
/// neukládá a pracuje jen s veřejnými daty. Není stránka návštěvníka, vložit do iframe ho tak nejde.
/// </summary>
public sealed class GeneratorController(IAvailabilityReader reader, AvailabilitySettings settings) : Controller
{
    private static readonly ResourceManager _texts = new(typeof(SharedResource).FullName!, typeof(SharedResource).Assembly);

    /// <summary>Formulář, náhled, odkaz a kód iframe.</summary>
    /// <param name="ct">Zrušení požadavku.</param>
    [HttpGet("/Generator")]
    public async Task<IActionResult> IndexAsync(CancellationToken ct)
    {
        var form = GeneratorForm.From(Request.Query, settings);
        AvailabilityOptions options = await reader.OptionsAsync(form.OptionsQuery(), ct);

        // Hodnota z dřívější nabídky mohla zmizet (merchant zdroj skryl) nebo ji správce přepsal v adrese.
        // Generátor pak začne od výchozích voleb, místo aby ukázal, že pohled není dostupný.
        if (options.Outcome is AvailabilityOutcome.ViewNotAvailable or AvailabilityOutcome.UnsupportedSegment && form.HasChoice())
        {
            form = form.WithoutChoice();
            options = await reader.OptionsAsync(form.OptionsQuery(), ct);
        }

        string instance = UriHelper.BuildAbsolute(Request.Scheme, Request.Host, Request.PathBase, "/");
        return View("Index", GeneratorPage.Of(form, options, settings, instance, Url.Content("~/"), FrameTitle));
    }

    // Název iframe je v jazyce vložené stránky, generátor sám je česky.
    private static string FrameTitle(string language) =>
        _texts.GetString("Page.Title", VisitorLanguageProvider.CultureOf(language)) ?? string.Empty;
}
