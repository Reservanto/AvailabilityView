using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.RateLimiting;

namespace AvailabilityView.Web.Security;

/// <summary>
/// Omezení počtu požadavků podle IP adresy pro všechny stránky aplikace. Veřejná stránka by jinak
/// každým zobrazením mohla volat API merchanta a pět návštěv průvodce přepnulo klienta do výpadku.
/// Statické soubory obslouží middleware před omezením, nepočítají se. Za reverzní proxy mají všichni návštěvníci
/// jednu IP (rozhodnuto 2026-11-09, klíč pro proxy nepřidáváme).
/// </summary>
internal static class RequestLimits
{
    /// <summary>Kolik požadavků smí jedna IP adresa poslat za <see cref="Window"/>. Pevně v kódu, klíč konfigurace zatím nikdo nepotřebuje.</summary>
    public const int PermitLimit = 120;

    private const string TooManyRequestsView = "~/Views/Shared/TooManyRequests.cshtml";

    /// <summary>Okno, za které se požadavky počítají.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>Zaregistruje omezení. Middleware se zařazuje až za určení jazyka, aby hláška mluvila jazykem stránky.</summary>
    /// <param name="services">Služby aplikace.</param>
    public static IServiceCollection AddRequestLimits(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionOf(context.Connection.RemoteIpAddress), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = PermitLimit,
                    Window = Window,
                    QueueLimit = 0,
                }));
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = RejectAsync;
        });

    // IPv4 přes dvojí zásobník přichází jako ::ffff:a.b.c.d, je to stejná adresa.
    private static string PartitionOf(IPAddress? address) =>
        address is null ? string.Empty
        : address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString()
        : address.ToString();

    // Stránka se stejným layoutem jako stránka návštěvníka. Do API ani do kontroléru se nejde.
    private static async ValueTask RejectAsync(OnRejectedContext rejected, CancellationToken _)
    {
        HttpContext context = rejected.HttpContext;
        if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        var page = new ViewResult { ViewName = TooManyRequestsView };
        await page.ExecuteResultAsync(new ActionContext(context, context.GetRouteData(), new ActionDescriptor()));
    }
}
