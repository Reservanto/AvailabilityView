using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AvailabilityView.Web.Security;

/// <summary>
/// Jak stránka návštěvníka zapadne do webu merchanta, sekce <c>AvailabilityView</c> (<c>docs/SPEC.md</c> sekce 5 a 8):
/// vložení přes iframe a vlastní CSS. Patří do Web: jde jen o hlavičky a stránku, čtení dat se netýká. Změna platí po
/// restartu aplikace.
/// </summary>
public sealed class EmbeddingOptions
{
    /// <summary>
    /// Originy webů, které smí stránku návštěvníka vložit přes iframe, ve tvaru <c>https://www.example.cz</c>: schéma,
    /// doména, volitelně port, bez cesty a bez hvězdičky.
    /// </summary>
    public IReadOnlyList<string> AllowedFrameOrigins { get; set; } = [];

    /// <summary>
    /// Vlastní CSS merchanta: absolutní adresa <c>https://…</c> nebo cesta od kořene instance
    /// (<c>/custom/web.css</c>). Prázdné = bez vlastního stylu.
    /// </summary>
    public string? CustomCssUrl { get; set; }

    /// <summary>Povolené originy v podobě pro hlavičku. Platí jen po úspěšné kontrole.</summary>
    public IReadOnlyList<string> FrameOrigins() => [.. AllowedFrameOrigins.Select(origin => FrameOrigin.Normalize(origin)!).Distinct(StringComparer.Ordinal)];

    /// <summary>Vlastní CSS, <c>null</c> = není nastavené. Platí jen po úspěšné kontrole.</summary>
    public CustomStyle? Style() => string.IsNullOrEmpty(CustomCssUrl) ? null : CustomStyle.Parse(CustomCssUrl);
}

/// <summary>
/// Vlastní CSS merchanta. Načte se za styly aplikace jen na stránce návštěvníka (i se stránkou 429), CSP povolí jen jeho
/// origin. Styl z cesty instance povoluje už <c>style-src 'self'</c>.
/// </summary>
/// <param name="Url">Absolutní adresa, nebo cesta od kořene instance.</param>
/// <param name="Origin">Origin pro <c>style-src</c>, u cesty instance <c>null</c>.</param>
public sealed record CustomStyle(string Url, string? Origin)
{
    /// <summary>
    /// Rozebere hodnotu z konfigurace, <c>null</c> = neplatná. Cesta začíná jedním lomítkem, adresa je <c>https</c>.
    /// Jméno a heslo v adrese odmítne kontrola originu (<see cref="FrameOrigin.Normalize"/>). Mezery, řídicí znaky
    /// a zpětné lomítko odmítne, prohlížeč by je opravoval jinak než server.
    /// </summary>
    /// <param name="value">Hodnota <see cref="EmbeddingOptions.CustomCssUrl"/>.</param>
    public static CustomStyle? Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0 || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '\\'))
        {
            return null;
        }

        // Dvě lomítka by prohlížeč četl jako adresu jiného webu.
        if (value[0] == '/')
        {
            return value.StartsWith("//", StringComparison.Ordinal) ? null : new CustomStyle(value, null);
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme != "https"
            || FrameOrigin.Normalize(uri.GetLeftPart(UriPartial.Authority)) is not { } origin)
        {
            return null;
        }

        // Doména v ASCII jako v hlavičce: AbsoluteUri by mezinárodní doménu nechal v Unicode.
        return new CustomStyle(origin + uri.PathAndQuery, origin);
    }

    /// <summary>Adresa pro značku <c>link</c>. Cesta instance leží pod cestou aplikace, třeba pod podadresářem IIS.</summary>
    /// <param name="pathBase">Cesta aplikace požadavku.</param>
    public string Href(PathString pathBase) => Origin is null ? pathBase.ToUriComponent() + Url : Url;
}

/// <summary>Tvar položky <see cref="EmbeddingOptions.AllowedFrameOrigins"/>.</summary>
public static class FrameOrigin
{
    /// <summary>
    /// Origin v podobě pro hlavičku <c>frame-ancestors</c>, <c>null</c> = neplatná položka. Výsledek se skládá jen ze
    /// schématu, doménového jména v ASCII a čísla portu, do hlavičky se tak nedostane nic jiného. Hvězdičku, středník
    /// nebo čárku odmítne už rozbor adresy, hlídá to test. Mezery na okraji by rozbor tiše ořízl, proto se kontrolují.
    /// </summary>
    /// <param name="value">Položka z konfigurace.</param>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Any(char.IsWhiteSpace)
            || !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("https" or "http")
            || uri.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4)
            || uri.UserInfo.Length > 0
            || uri.PathAndQuery != "/"
            || uri.Fragment.Length > 0)
        {
            return null;
        }

        return uri.IsDefaultPort ? $"{uri.Scheme}://{uri.IdnHost}" : $"{uri.Scheme}://{uri.IdnHost}:{uri.Port}";
    }
}

/// <summary>Neplatná položka domén nebo neplatná adresa stylu nedovolí start aplikace.</summary>
internal sealed class EmbeddingOptionsValidator : IValidateOptions<EmbeddingOptions>
{
    public ValidateOptionsResult Validate(string? name, EmbeddingOptions options)
    {
        List<string> failures = [];
        string[] invalid = [.. options.AllowedFrameOrigins.Where(origin => FrameOrigin.Normalize(origin) is null)];
        if (invalid.Length > 0)
        {
            failures.Add(
                $"AvailabilityView:AllowedFrameOrigins má neplatné položky: {string.Join(", ", invalid)}. " +
                "Zapište je jako https://www.example.cz: schéma a doména, volitelně port, bez cesty a bez hvězdičky.");
        }

        if (!string.IsNullOrEmpty(options.CustomCssUrl) && CustomStyle.Parse(options.CustomCssUrl) is null)
        {
            failures.Add(
                $"AvailabilityView:CustomCssUrl má neplatnou hodnotu: {options.CustomCssUrl}. " +
                "Zapište adresu https://www.example.cz/web.css nebo cestu od kořene instance, třeba /custom/web.css.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>Registrace nastavení vložení.</summary>
public static class EmbeddingServiceCollectionExtensions
{
    /// <summary>Naváže <see cref="EmbeddingOptions"/> na sekci <c>AvailabilityView</c> s kontrolou při startu.</summary>
    /// <param name="services">Kolekce služeb.</param>
    /// <param name="configuration">Konfigurace aplikace.</param>
    public static IServiceCollection AddEmbedding(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmbeddingOptions>().Bind(configuration.GetSection("AvailabilityView")).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<EmbeddingOptions>, EmbeddingOptionsValidator>());
        return services;
    }
}
