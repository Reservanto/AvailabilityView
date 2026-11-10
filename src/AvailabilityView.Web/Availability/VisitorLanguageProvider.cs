using System.Globalization;
using AvailabilityView.Application.Availability;
using Microsoft.AspNetCore.Localization;

namespace AvailabilityView.Web.Availability;

/// <summary>
/// Jediný zdroj jazyka požadavku. Jazyk z adresy platí jen pro stránky s <see cref="VisitorPageAttribute"/>, ostatní
/// zůstávají česky. Cookie ani <c>Accept-Language</c> se nečtou: o jazyce rozhoduje merchant odkazem
/// a stránka nesmí cookie vytvořit.
/// </summary>
/// <param name="settings">Nastavení instance s výchozím jazykem.</param>
public sealed class VisitorLanguageProvider(AvailabilitySettings settings) : RequestCultureProvider
{
    /// <summary>Kultura stránek bez jazyka návštěvníka.</summary>
    public const string SetupCulture = "cs-CZ";

    // Angličtina britsky: 24hodinový čas, den před měsícem.
    private static readonly Dictionary<string, string> _cultures = new(StringComparer.Ordinal)
    {
        ["cs"] = "cs-CZ",
        ["en"] = "en-GB",
    };

    /// <summary>Kultury, které stránka používá.</summary>
    public static IList<CultureInfo> Cultures { get; } = [.. _cultures.Values.Select(CultureInfo.GetCultureInfo)];

    /// <summary>Kultura jazyka stránky.</summary>
    /// <param name="language">Jeden z <see cref="AvailabilitySettings.Languages"/>.</param>
    public static CultureInfo CultureOf(string language) => CultureInfo.GetCultureInfo(_cultures[language]);

    /// <inheritdoc />
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (!VisitorPageAttribute.Applies(httpContext))
        {
            return NullProviderCultureResult;
        }

        CultureInfo culture = CultureOf(VisitorRequest.LanguageFrom(httpContext.Request.Query, settings));
        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(culture.Name));
    }
}
