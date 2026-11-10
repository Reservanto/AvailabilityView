using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;

namespace AvailabilityView.Web.Setup;

/// <summary>
/// Odebere parametry z adresy návratu ze souhlasu dřív, než požadavek uvidí cokoli jiného. Reservanto v nich vrací
/// LongTimeToken a ASP.NET Core vypisuje adresu požadavku do logu ještě před první middlewarou, proto filtr logu nestačí
/// (merchant si ho může v konfiguraci přepsat). Co adresu nevidí, nemůže ji zalogovat (<c>docs/SPEC.md</c> sekce 10).
/// </summary>
internal sealed class CallbackQueryProtection(IHttpContextFactory inner) : IHttpContextFactory
{
    public const string CallbackPath = "/Auth/Callback";

    public HttpContext Create(IFeatureCollection featureCollection)
    {
        IHttpRequestFeature? request = featureCollection.Get<IHttpRequestFeature>();
        if (request is not null && IsCallback(request.Path) && request.QueryString.Length > 0)
        {
            featureCollection.Set(new CallbackQuery(request.QueryString));
            request.QueryString = string.Empty;
            request.RawTarget = request.Path;
        }

        return inner.Create(featureCollection);
    }

    public void Dispose(HttpContext httpContext) => inner.Dispose(httpContext);

    // Konec cesty a bez ohledu na velikost písmen: pod IIS může být před cestou název aplikace a routování
    // velikost písmen nerozlišuje. Širší shoda nevadí, chráněná adresa by jen přišla o parametry.
    private static bool IsCallback(string path) =>
        path.TrimEnd('/').EndsWith(CallbackPath, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Parametry odebrané z adresy návratu. Záměrně ne record: vygenerovaný <c>ToString()</c> by vypsal token.</summary>
internal sealed class CallbackQuery(string queryString)
{
    private readonly Dictionary<string, Microsoft.Extensions.Primitives.StringValues> _values = QueryHelpers.ParseQuery(queryString);

    public string? Accepted => Single("accepted");

    public string? Token => Single("token");

    // Hodnota uvedená víckrát není z Reservanta, nebere se.
    private string? Single(string name) =>
        _values.TryGetValue(name, out Microsoft.Extensions.Primitives.StringValues value) && value.Count == 1 ? value[0] : null;
}
