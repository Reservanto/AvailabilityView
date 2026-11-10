using System.Text;
using System.Text.RegularExpressions;
using Reservanto.ApiClient.Core;

namespace Reservanto.ApiClient.Authorization;

/// <summary>
/// Připojení účtu merchanta, jednorázově v průvodci (<c>docs/SPEC.md</c> sekce 6). Správce otevře souhlas v Reservantu
/// a Reservanto ho vrátí na návratovou adresu s parametry <c>accepted</c> a <c>token</c>.
/// </summary>
public static partial class MerchantConnect
{
    // Není v OpenAPI, je to stránka pro prohlížeč.
    private const string ConsentPath = "v1/Authorize/GetLongTimeToken";

    /// <summary>Práva jen pro obsazenost z rezervací. Žádají se, jen když o to správce požádá.</summary>
    public static IReadOnlyList<string> OccupancyRights { get; } = [.. ApiEndpoints.EventGetList.Rights.Order(StringComparer.Ordinal)];

    /// <summary>Práva, o která se žádá vždy. Odvozená z katalogu endpointů, testy hlídají shodu se SPEC.</summary>
    public static IReadOnlyList<string> BaseRights { get; } =
        [.. ApiEndpoints.All.SelectMany(endpoint => endpoint.Rights).Except(OccupancyRights, StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>Práva, o která se žádá.</summary>
    /// <param name="occupancy">Správce chce ukazovat obsazenost z rezervací.</param>
    public static IReadOnlyList<string> RightsFor(bool occupancy) =>
        occupancy ? [.. BaseRights.Concat(OccupancyRights).Order(StringComparer.Ordinal)] : BaseRights;

    /// <summary>Adresa souhlasu v Reservantu.</summary>
    /// <param name="baseUrl">Adresa API.</param>
    /// <param name="clientId">Id aplikace přidělené Reservantem.</param>
    /// <param name="redirectUrl">Kam Reservanto vrátí výsledek.</param>
    /// <param name="occupancy">Správce chce ukazovat obsazenost z rezervací.</param>
    public static Uri BuildConsentUrl(Uri baseUrl, string clientId, Uri redirectUrl, bool occupancy)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(redirectUrl);

        StringBuilder query = new StringBuilder("clientId=").Append(Uri.EscapeDataString(clientId.Trim()));
        foreach (string right in RightsFor(occupancy))
        {
            query.Append("&rights=").Append(Uri.EscapeDataString(right));
        }

        query.Append("&redirectUrl=").Append(Uri.EscapeDataString(redirectUrl.AbsoluteUri));
        var address = new Uri(ReservantoApiServiceCollectionExtensions.EnsureTrailingSlash(baseUrl), ConsentPath);
        return new UriBuilder(address) { Query = query.ToString() }.Uri;
    }

    /// <summary>
    /// Hodnota má tvar tokenu. Stránka ji vkládá do příkazu a do JSON, které správce kopíruje, a přišla z adresy,
    /// kterou může poslat kdokoli. Proto jen znaky, které v konzoli ani v JSON nic neznamenají.
    /// </summary>
    /// <param name="token">Hodnota parametru <c>token</c>.</param>
    public static bool HasLongTimeTokenShape(string? token) => token is not null && LongTimeTokenShape().IsMatch(token);

    [GeneratedRegex(@"\A[A-Za-z0-9_\-.]{16,512}\z")]
    private static partial Regex LongTimeTokenShape();
}
