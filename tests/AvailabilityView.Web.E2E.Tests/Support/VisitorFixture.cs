using System.Net;
using AvailabilityView.Application.Availability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace AvailabilityView.Web.E2E.Tests.Support;

/// <summary>
/// Připojená aplikace na Kestrelu s ručním katalogem a termíny, bez sítě, a sdílený Chromium. Je pátek 6. 11. 2026
/// v 9:00 v Praze. Zaměření 1 masáže (individuální, Praha, Jana), 2 lekce (Brno, Sál). Lekce dnes v 17:00, v sobotu
/// plně obsazená, v úterý 10. 11. dvě souběžně. Jana má dnes a v pondělí pracovní dobu 9–17, dnes je 13–14 obsazená.
/// </summary>
public sealed class VisitorFixture : IAsyncLifetime
{
    private static readonly PublicCatalog _catalog = new(
        [new PublicSegment(1, "Masáže", "OneToOne"), new PublicSegment(2, "Lekce", "Classes")],
        [new PublicLocation(10, "Praha"), new PublicLocation(20, "Brno")],
        [
            new PublicService(1001, "Sportovní masáž", 1, TimeSpan.FromMinutes(30), [100]),
            new PublicService(1002, "Jóga", 2, TimeSpan.FromMinutes(60), [102]),
        ],
        [new PublicResource(100, "Jana", 10, [1]), new PublicResource(102, "Sál", 20, [2])]);

    /// <summary>Web merchanta, který smí stránku vložit (AllowedFrameOrigins).</summary>
    public MerchantSite Allowed { get; private set; } = null!;

    /// <summary>
    /// Barva zvýraznění z vlastního CSS merchanta (CustomCssUrl na povoleném webu). Platí pro všechny testy s touto
    /// aplikací, takže kontrola přístupnosti a konzole běží i s ním. Kontrast na bílé je přes 9:1.
    /// </summary>
    public const string MerchantAccent = "rgb(91, 33, 182)";

    /// <summary>Adresa vlastního CSS merchanta na povoleném webu.</summary>
    public string MerchantStyle { get; private set; } = null!;

    /// <summary>Jiný web, který stránku vložit nesmí.</summary>
    public MerchantSite Foreign { get; private set; } = null!;

    private WebApplicationFactory<Program>? _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public Uri Address { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Allowed = await MerchantSite.StartAsync();
        Foreign = await MerchantSite.StartAsync();
        // Písmo se stáhne, jen když si ho test vyžádá: žádný text ho nepoužívá a obsah není platné písmo.
        Allowed.Serve("pismo.woff2", "neni-pismo", "font/woff2", cors: true);
        MerchantStyle = Allowed.Serve(
            "vzhled.css",
            "@font-face { font-family: AvMerchant; src: url(pismo.woff2) format(\"woff2\"); } .av-root { --av-color-accent: #5b21b6; }",
            "text/css; charset=utf-8");
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Reservanto:BaseUrl"] = "https://api.test.invalid/",
                    ["Reservanto:LongTimeToken"] = "ltt-testu",
                    ["AvailabilityView:AllowedFrameOrigins:0"] = Allowed.Origin,
                    ["AvailabilityView:CustomCssUrl"] = MerchantStyle,
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISegmentAvailabilitySource>();
                services.AddSingleton<ISegmentAvailabilitySource>(new FakeSource(["Classes"], SlotKind.Lesson, Lessons(), null));
                services.AddSingleton<ISegmentAvailabilitySource>(new FakeSource(["OneToOne"], SlotKind.FreeTime, FreeTime(), Timeline()));
                services.AddSingleton<IPublicCatalog>(new FixedCatalog());
                services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero)));
            });
        });
        _app.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps()));
        _app.StartServer();
        using HttpClient client = _app.CreateClient();
        Address = client.BaseAddress!;

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    /// <summary>
    /// Nový izolovaný kontext s danou šířkou. Vývojový certifikát nemusí být na stroji důvěryhodný. Ikonu, kterou si
    /// Chromium žádá u každé stránky, obslouží kontext sám: aplikace by ji počítala do limitu 120 požadavků za minutu
    /// a sdílená instance by na něj narazila. Pásmo prohlížeče je výchozí pražské, aby výsledek nezávisel na stroji.
    /// </summary>
    public async Task<IBrowserContext> NewContextAsync(int width, bool javaScript = true, string timeZone = "Europe/Prague")
    {
        IBrowserContext context = await _browser!.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            JavaScriptEnabled = javaScript,
            Locale = "cs-CZ",
            TimezoneId = timeZone,
            ViewportSize = new ViewportSize { Width = width, Height = 900 },
        });
        await context.RouteAsync("**/favicon.ico", route => route.FulfillAsync(new RouteFulfillOptions { Status = 404 }));
        return context;
    }

    public string Url(string path) => new Uri(Address, path).AbsoluteUri;

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();
        await Allowed.DisposeAsync();
        await Foreign.DisposeAsync();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private static DateTimeOffset Local(int day, int hour) => new(2026, 11, day, hour, 0, 0, TimeSpan.FromHours(1));

    private static SourceSlot[] Lessons() =>
    [
        new(Local(6, 17), Local(6, 18), 1002, 102, "6/12", false),
        new(Local(7, 10), Local(7, 11), 1002, 102, "obsazeno", true),
        new(Local(10, 17), Local(10, 18), 1002, 102, "6/12", false),
        new(Local(10, 17), Local(10, 18), 1002, 102, "obsazeno", true),
    ];

    private static SourceSlot[] FreeTime() =>
    [
        new(Local(6, 10), Local(6, 12), null, 100, null, false),
        new(Local(6, 14), Local(6, 17), null, 100, null, false),
        new(Local(9, 9), Local(9, 12), null, 100, null, false),
    ];

    private static SourceTimeline Timeline() => new(
        [new ResourceInterval(100, Local(6, 9), Local(6, 17)), new ResourceInterval(100, Local(9, 9), Local(9, 17))],
        [new ResourceInterval(100, Local(6, 13), Local(6, 14))]);

    private sealed class FixedCatalog : IPublicCatalog
    {
        public Task<PublicCatalog> GetAsync(CancellationToken ct) => Task.FromResult(_catalog);
    }

    private sealed class FakeSource(IReadOnlyList<string> types, SlotKind kind, IReadOnlyList<SourceSlot> slots, SourceTimeline? timeline) : ISegmentAvailabilitySource
    {
        public IReadOnlyList<string> SegmentTypes => types;

        public SlotKind Kind => kind;

        public Task<IReadOnlyList<SourceSlot>> ReadAsync(SourceRequest request, CancellationToken ct) => Task.FromResult(slots);

        public Task<SourceTimeline?> ReadTimelineAsync(SourceRequest request, AvailabilityInterval week, CancellationToken ct) =>
            Task.FromResult(timeline);

        public Task<IReadOnlyList<ResourceInterval>?> ReadWorkingHoursAsync(SourceRequest request, AvailabilityInterval interval, CancellationToken ct) =>
            Task.FromResult(timeline?.Working);
    }
}

/// <summary>Kolekce testů stránky návštěvníka se sdíleným prohlížečem.</summary>
[CollectionDefinition(Name)]
public sealed class VisitorTestGroup : ICollectionFixture<VisitorFixture>
{
    public const string Name = "E2E návštěvník";
}

/// <summary>
/// Kolekce testů generátoru s vlastní aplikací. Generátor s náhledem posílá dva požadavky na stránku a se sdílenou
/// aplikací by testy narazily na limit 120 požadavků za minutu.
/// </summary>
[CollectionDefinition(Name)]
public sealed class GeneratorTestGroup : ICollectionFixture<VisitorFixture>
{
    public const string Name = "E2E generátor";
}

/// <summary>Kolekce testů klávesnice s vlastní aplikací, ze stejného důvodu jako <see cref="GeneratorTestGroup"/>.</summary>
[CollectionDefinition(Name)]
public sealed class KeyboardTestGroup : ICollectionFixture<VisitorFixture>
{
    public const string Name = "E2E klávesnice";
}
