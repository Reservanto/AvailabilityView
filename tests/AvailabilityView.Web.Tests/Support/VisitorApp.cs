using System.Net;
using System.Text.RegularExpressions;
using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Connection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace AvailabilityView.Web.Tests.Support;

/// <summary>
/// Aplikace se skutečným čtením nad ručním katalogem a termíny, bez sítě. Katalog: zaměření 1 masáže (individuální),
/// 2 lekce, 3 půjčovna (nepodporovaná), provozovny 10 Praha a 20 Brno, zdroje 100 Jana (Praha, masáže) a 102 Sál
/// (Brno, lekce), služby 1001 Sportovní (masáže, Jana) a 1002 Jóga (lekce, Sál). Je pátek 6. 11. 2026 v 9:00 v Praze.
/// </summary>
internal sealed partial class VisitorApp : IAsyncDisposable
{
    public const string Token = "ltt-testu";

    private readonly WebApplicationFactory<Program> _app;

    public VisitorApp(bool connected = true, IReadOnlyDictionary<string, string?>? settings = null, Action<IServiceCollection>? services = null)
    {
        _app = TestApp.Create(
            token: connected ? Token : null,
            settings: settings,
            services: collection =>
            {
                collection.RemoveAll<ISegmentAvailabilitySource>();
                collection.AddSingleton<ISegmentAvailabilitySource>(Lessons);
                collection.AddSingleton<ISegmentAvailabilitySource>(Individual);
                collection.AddSingleton<IPublicCatalog>(Catalog);
                collection.AddSingleton<TimeProvider>(Time);
                services?.Invoke(collection);
            });
    }

    public static PublicCatalog DefaultCatalog { get; } = new(
        [new PublicSegment(1, "Masáže", "OneToOne"), new PublicSegment(2, "Lekce", "Classes"), new PublicSegment(3, "Půjčovna", "RentalLike")],
        [new PublicLocation(10, "Praha"), new PublicLocation(20, "Brno")],
        [
            new PublicService(1001, "Sportovní", 1, TimeSpan.FromMinutes(30), [100]),
            new PublicService(1002, "Jóga", 2, TimeSpan.FromMinutes(60), [102]),
        ],
        [new PublicResource(100, "Jana", 10, [1]), new PublicResource(102, "Sál", 20, [2])]);

    public FakeCatalog Catalog { get; } = new();

    public FakeSource Lessons { get; } = new(["Classes"], SlotKind.Lesson);

    public FakeSource Individual { get; } = new(["OneToOne"], SlotKind.FreeTime);

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero));

    /// <summary>Místní čas v Praze v zimě.</summary>
    public static DateTimeOffset Local(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, TimeSpan.FromHours(1));

    public static SourceSlot Lesson(DateTimeOffset start, string? occupancy = "6/12", bool full = false) =>
        new(start, start.AddHours(1), 1002, 102, occupancy, full);

    public async Task<HttpResponseMessage> SendAsync(string url, string? acceptLanguage = null)
    {
        using HttpClient client = TestApp.Client(_app);
        if (acceptLanguage is not null)
        {
            client.DefaultRequestHeaders.Add("Accept-Language", acceptLanguage);
        }

        return await client.GetAsync(new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken);
    }

    public async Task<string> GetAsync(string url, string? acceptLanguage = null)
    {
        using HttpResponseMessage response = await SendAsync(url, acceptLanguage);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Stránka bez nonce, který je v každé odpovědi jiný.</summary>
    public async Task<string> GetComparableAsync(string url) => NoncePattern().Replace(await GetAsync(url), "nonce=\"\"");

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [GeneratedRegex("nonce=\"[0-9A-F]+\"")]
    private static partial Regex NoncePattern();

    internal sealed class FakeCatalog : IPublicCatalog
    {
        public PublicCatalog Value { get; set; } = DefaultCatalog;

        public ConnectionState? Failure { get; set; }

        public Task<PublicCatalog> GetAsync(CancellationToken ct) =>
            Failure is { } reason ? throw new ReservationSystemException(reason) : Task.FromResult(Value);
    }

    internal sealed class FakeSource(IReadOnlyList<string> types, SlotKind kind) : ISegmentAvailabilitySource
    {
        public IReadOnlyList<string> SegmentTypes => types;

        public SlotKind Kind => kind;

        public List<SourceSlot> Slots { get; } = [];

        public Task<IReadOnlyList<SourceSlot>> ReadAsync(SourceRequest request, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SourceSlot>>(Slots);

        public SourceTimeline? Timeline { get; set; }

        public Task<SourceTimeline?> ReadTimelineAsync(SourceRequest request, AvailabilityInterval week, CancellationToken ct) =>
            Task.FromResult(Timeline);

        public Task<IReadOnlyList<ResourceInterval>?> ReadWorkingHoursAsync(SourceRequest request, AvailabilityInterval interval, CancellationToken ct) =>
            Task.FromResult(Timeline?.Working);
    }
}
