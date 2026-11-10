using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AvailabilityView.Web.E2E.Tests.Support;

/// <summary>
/// Sdílený Chromium, nepřipojená aplikace na Kestrelu a falešný souhlas Reservanta. Souhlas je skutečný server, ne
/// odchycený požadavek: Playwright přesměrování odchytit neumí a CSP se musí ověřit na skutečném přesměrování.
/// Chová se jako Reservanto: z adresy API přesměruje na přihlášení na jiné doméně (ověřeno proti produkci).
/// Chybějící Chromium je selhání, ne přeskočený test.
/// </summary>
public sealed class BrowserFixture : IAsyncLifetime
{
    public const string Token = "ltt-testu-0123456789";

    private WebApplication? _consent;
    private WebApplicationFactory<Program>? _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public Uri Address { get; private set; } = null!;

    /// <summary>Cizí web, který stránky vložit nesmí.</summary>
    public MerchantSite Foreign { get; private set; } = null!;

    /// <summary>Parametry požadavků, které došly na souhlas.</summary>
    public ConcurrentQueue<string> ConsentRequests { get; } = new();

    public async ValueTask InitializeAsync()
    {
        Uri consentAddress = await StartConsentAsync();
        Foreign = await MerchantSite.StartAsync();
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Reservanto:BaseUrl"] = consentAddress.AbsoluteUri }));
        });
        _app.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps()));
        _app.StartServer();
        using HttpClient client = _app.CreateClient();
        Address = client.BaseAddress!;

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    /// <summary>Nový izolovaný kontext. Vývojový certifikát nemusí být na stroji důvěryhodný.</summary>
    public Task<IBrowserContext> NewContextAsync() =>
        _browser!.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true, Locale = "cs-CZ" });

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();
        await Foreign.DisposeAsync();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        if (_consent is not null)
        {
            await _consent.DisposeAsync();
        }
    }

    private async Task<Uri> StartConsentAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrelHttpsConfiguration();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps()));
        _consent = builder.Build();
        _consent.MapGet("/v1/Authorize/GetLongTimeToken", (HttpContext context) =>
        {
            ConsentRequests.Enqueue(context.Request.QueryString.Value!);
            string redirectUrl = Uri.EscapeDataString(context.Request.Query["redirectUrl"].ToString());
            return Results.Redirect($"https://localhost:{context.Request.Host.Port}/Login?redirectUrl={redirectUrl}");
        });
        _consent.MapGet("/Login", (HttpContext context) =>
            Results.Redirect($"{context.Request.Query["redirectUrl"]}?accepted=1&token={Token}"));
        await _consent.StartAsync();
        return new Uri(_consent.Urls.First());
    }
}

/// <summary>Kolekce testů se sdíleným prohlížečem.</summary>
[CollectionDefinition(Name)]
public sealed class BrowserTestGroup : ICollectionFixture<BrowserFixture>
{
    public const string Name = "E2E";
}
