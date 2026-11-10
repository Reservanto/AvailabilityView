using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace AvailabilityView.Web.E2E.Tests.Support;

/// <summary>
/// Web merchanta na vlastním originu: skutečný server na loopback s jiným portem než aplikace. Podvržená odpověď
/// z Playwrightu nestačí: Chromium ji bere jako stránku z veřejné adresy a vložení stránky z lokální adresy zablokuje
/// kontrolou lokální sítě (Local Network Access) dřív, než se uplatní CSP. Test by pak prošel i bez hlavičky.
/// </summary>
public sealed class MerchantSite : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, (string Content, string Type, bool Cors)> _pages = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _requests = new(StringComparer.Ordinal);
    private WebApplication? _server;

    /// <summary>Origin webu, třeba <c>https://127.0.0.1:50123</c>.</summary>
    public string Origin { get; private set; } = null!;

    public static async Task<MerchantSite> StartAsync()
    {
        var site = new MerchantSite();
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrelHttpsConfiguration();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps()));
        site._server = builder.Build();
        site._server.MapGet("/{name}", (string name, HttpResponse response) =>
        {
            site._requests.AddOrUpdate(name, 1, (_, count) => count + 1);
            if (!site._pages.TryGetValue(name, out (string Content, string Type, bool Cors) file))
            {
                return Results.NotFound();
            }

            if (file.Cors)
            {
                response.Headers.AccessControlAllowOrigin = "*";
            }

            return Results.Content(file.Content, file.Type);
        });
        await site._server.StartAsync();
        site.Origin = new Uri(site._server.Urls.First()).GetLeftPart(UriPartial.Authority);
        return site;
    }

    /// <summary>
    /// Vystaví stránku nebo jiný soubor a vrátí jeho adresu. Jméno má být v rámci testů jedinečné, server je sdílený.
    /// Písmo z cizího originu prohlížeč použije jen s hlavičkou CORS (<paramref name="cors"/>).
    /// </summary>
    public string Serve(string name, string content, string type = "text/html; charset=utf-8", bool cors = false)
    {
        _pages[name] = (content, type, cors);
        return $"{Origin}/{name}";
    }

    /// <summary>Kolikrát si prohlížeč soubor vyžádal. Požadavek zablokovaný CSP sem nedojde.</summary>
    public int RequestsOf(string name) => _requests.TryGetValue(name, out int count) ? count : 0;

    public async ValueTask DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }
}
