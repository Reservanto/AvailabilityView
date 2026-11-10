using System.Text.Encodings.Web;
using System.Text.Unicode;
using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Connection;
using AvailabilityView.Web.Availability;

namespace AvailabilityView.Web.Generator;

/// <summary>
/// Stránka generátoru. Jde do prohlížeče: volby správce, Id a názvy veřejných objektů z nabídky
/// filtrů a odkaz, který z nich vznikl. Seznam polí hlídá test.
/// </summary>
/// <param name="Message">Proč nabídka není, <c>null</c> = nabídka je.</param>
/// <param name="Failure">Proč data nejdou načíst, jen u <see cref="AvailabilityOutcome.SourceUnavailable"/>.</param>
/// <param name="Form">Volby správce. Hodnoty filtrů ukazují pole z <see cref="Filters"/>, ne tento záznam.</param>
/// <param name="Filters">Filtry v pořadí závislosti.</param>
/// <param name="MaxDays">Nejdelší období instance.</param>
/// <param name="Output">Náhled, odkaz a kód iframe, jen s nabídkou.</param>
public sealed record GeneratorPage(
    AvailabilityOutcome? Message,
    ConnectionState? Failure,
    GeneratorForm Form,
    IReadOnlyList<FilterField> Filters,
    int MaxDays,
    GeneratorOutput? Output)
{
    /// <summary>Výška iframe bez skriptu, který ji přizpůsobí obsahu.</summary>
    public const int FrameHeight = 600;

    // Kód iframe se ukáže jako text a merchant ho vloží do svého HTML: hodnoty atributů musí být kódované.
    private static readonly HtmlEncoder _attribute = HtmlEncoder.Create(UnicodeRanges.All);

    /// <summary>Stránka z voleb správce a nabídky.</summary>
    /// <param name="form">Volby správce.</param>
    /// <param name="options">Nabídka filtrů pro volby.</param>
    /// <param name="settings">Nastavení instance.</param>
    /// <param name="instance">Absolutní adresa stránky návštěvníka této instance, končí lomítkem.</param>
    /// <param name="root">Adresa stránky návštěvníka od kořene webu pro náhled, končí lomítkem.</param>
    /// <param name="frameTitle">Název iframe pro čtečky v jazyce stránky.</param>
    internal static GeneratorPage Of(
        GeneratorForm form,
        AvailabilityOptions options,
        AvailabilitySettings settings,
        string instance,
        string root,
        Func<string, string> frameTitle)
    {
        if (options is not { Outcome: AvailabilityOutcome.Available, Selection: { } selection })
        {
            return new GeneratorPage(options.Outcome, options.Failure, form, [], settings.MaxDays, null);
        }

        // Pole formuláře i odkaz vycházejí z doplněných voleb, ne z adresy generátoru: náhled, odkaz i pole souhlasí.
        string query = form.LinkTo(selection, settings).Query();
        string link = instance + query;
        string code =
            $"<iframe src=\"{_attribute.Encode(link)}\" title=\"{_attribute.Encode(frameTitle(form.Language))}\" " +
            $"width=\"100%\" height=\"{FrameHeight}\" style=\"border: 0;\"></iframe>\n" +
            $"<script src=\"{_attribute.Encode(instance)}embed.js\" async></script>";
        return new GeneratorPage(
            null,
            null,
            form,
            [.. options.Filters.Select(FilterField.Of)],
            settings.MaxDays,
            new GeneratorOutput(root + query, link, code));
    }
}

/// <summary>Co generátor vytvořil.</summary>
/// <param name="PreviewUrl">Adresa náhledu od kořene webu.</param>
/// <param name="Link">Absolutní odkaz na pohled.</param>
/// <param name="Code">Kód pro web merchanta: iframe a skript, který mu nastaví výšku podle obsahu.</param>
public sealed record GeneratorOutput(string PreviewUrl, string Link, string Code);
