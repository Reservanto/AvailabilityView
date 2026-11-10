using System.Security.Cryptography;
using AvailabilityView.Web.Availability;
using Microsoft.Extensions.Options;

namespace AvailabilityView.Web.Security;

/// <summary>
/// Bezpečnostní hlavičky všech odpovědí (<c>docs/SPEC.md</c> sekce 10). Do iframe jde vložit jen stránka návštěvníka
/// (<see cref="VisitorPageAttribute"/>, i s hláškami a stránkou 429), a to z vlastní instance kvůli náhledu
/// v generátoru a z povolených domén. Průvodce, generátor a návrat ze souhlasu vložit nejde nikdy.
/// Hlavičku X-Frame-Options nepřidává: povolené domény neumí a prohlížeče s CSP ji ignorují.
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next, IOptions<EmbeddingOptions> embedding)
{
    private readonly string _visitorAncestors = string.Join(' ', ["'self'", .. embedding.Value.FrameOrigins()]);

    // Vlastní CSS merchanta jen na stránce návštěvníka, povolený je jen jeho origin. Ze stejných
    // míst jdou i písma, aby merchant mohl použít písmo svého webu (rozhodnutí 2026-11-09). Ostatní stránky písma nemají.
    private readonly string _visitorSources = embedding.Value.Style()?.Origin is { } origin ? $"'self' {origin}" : "'self'";

    private const string NonceKey = "AvailabilityView.CspNonce";

    public Task InvokeAsync(HttpContext context)
    {
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        context.Items[NonceKey] = nonce;

        // Až těsně před odesláním: stránka s chybou hlavičky nastavené dřív vymaže. Stránka je v té chvíli vybraná.
        context.Response.OnStarting(() =>
        {
            bool visitor = VisitorPageAttribute.Applies(context);
            string ancestors = visitor ? _visitorAncestors : "'none'";
            string styles = visitor ? $"style-src {_visitorSources}; font-src {_visitorSources}" : "style-src 'self'";
            IHeaderDictionary headers = context.Response.Headers;

            // frame-src 'self': generátor ukazuje náhled stránky návštěvníka. Cizí stránku žádná stránka nevkládá.
            headers.ContentSecurityPolicy =
                $"default-src 'none'; script-src 'nonce-{nonce}'; {styles}; img-src 'self'; frame-src 'self'; " +
                $"form-action 'self'; base-uri 'none'; frame-ancestors {ancestors}";
            // Adresa návratu ze souhlasu nese token, nesmí odejít v hlavičce Referer.
            headers["Referrer-Policy"] = "no-referrer";
            headers.XContentTypeOptions = "nosniff";
            return Task.CompletedTask;
        });
        return next(context);
    }

    /// <summary>Nonce požadavku pro značku <c>script</c>.</summary>
    public static string NonceOf(HttpContext context) => (string)context.Items[NonceKey]!;
}
