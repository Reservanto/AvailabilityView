using System.Net;
using AvailabilityView.Application.Availability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AvailabilityView.Web.Tests;

/// <summary>
/// Vykreslená stránka nad produkčním API s tokenem z user-secrets. Pro každý podporovaný typ
/// zaměření projde všech pět zobrazení. Aplikace API jen čte. Běží v <c>eng/check.ps1 -Mode Smoke</c>.
/// </summary>
[Trait("Category", "Smoke")]
public sealed class PageSmokeTests
{
    // Texty hlášek, které na stránce se skutečnými daty být nesmí. Hláška „nic volného“ smí.
    private static readonly string[] _failures =
    [
        "Tento pohled není dostupný.",
        "Tento druh nabídky stránka zatím neumí zobrazit.",
        "Tato stránka ještě není připojená k Reservantu.",
        "Dostupnost teď nejde načíst.",
    ];

    private const string NothingFree = "V tomto období nejsou žádné volné termíny.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> SupportedTypes => ["Classes", "OneToOne", "AutoClasses", "EmsLike"];

    // Zobrazení a značka, podle které se pozná, že se vykreslilo.
    private static readonly (string View, string Marker)[] _views =
    [
        ("agenda", "class=\"av-agenda\""),
        ("calendar", "class=\"av-calendar\""),
        ("timegrid", "class=\"av-timegrid\""),
        ("month", "class=\"av-month\""),
        ("next", "class=\"av-next"),
    ];

    [Theory]
    [MemberData(nameof(SupportedTypes))]
    public async Task EveryView_RendersFromProductionApi(string type)
    {
        await using WebApplicationFactory<Program> app = Live();
        using HttpClient client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        PublicCatalog catalog = await app.Services.GetRequiredService<IPublicCatalog>().GetAsync(Ct);
        PublicSegment segment = Assert.Single(catalog.Segments.Where(item => item.Type == type).Take(1));
        string? token = app.Services.GetRequiredService<IConfiguration>()["Reservanto:LongTimeToken"];
        Assert.False(string.IsNullOrWhiteSpace(token), "Smoke testy potřebují LongTimeToken v user-secrets, připojte účet v průvodci.");

        foreach ((string view, string marker) in _views)
        {
            using HttpResponseMessage response = await client.GetAsync(new Uri($"/?segment={segment.Id}&view={view}", UriKind.Relative), Ct);
            string page = await response.Content.ReadAsStringAsync(Ct);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.All(_failures, failure => Assert.DoesNotContain(failure, page, StringComparison.Ordinal));
            Assert.True(page.Contains(marker, StringComparison.Ordinal) || page.Contains(NothingFree, StringComparison.Ordinal), $"Zobrazení {view} zaměření {segment.Id} se nevykreslilo.");
            Assert.DoesNotContain(token, page, StringComparison.Ordinal);
        }
    }

    // Prostředí Testing jako ostatní testy, token a ClientId z user-secrets aplikace. Adresa API je výchozí produkční.
    private static WebApplicationFactory<Program> Live() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddUserSecrets(typeof(Program).Assembly, optional: false));
        });
}
