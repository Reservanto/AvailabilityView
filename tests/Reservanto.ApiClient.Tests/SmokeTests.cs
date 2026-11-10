using Reservanto.ApiClient.Core;
using Reservanto.ApiClient.Endpoints;
using Reservanto.ApiClient.Models;
using Reservanto.ApiClient.Tests.Support;

namespace Reservanto.ApiClient.Tests;

/// <summary>
/// Klient proti produkčnímu API s přísným čtením: neznámé pole shodí test. Testy jen čtou. S proměnnou
/// <c>RESERVANTO_RECORD</c> zároveň obnoví nahrávky v <c>tests/api-recordings</c>.
/// </summary>
[Trait("Category", "Smoke")]
public sealed class SmokeTests
{
    private readonly ApiRecorder _recorder = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset Today => new(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);

    [Fact]
    public async Task Catalog_ReadsStrictly_AndKeepsOnlyPublicObjects()
    {
        using LiveApi api = Live();

        Catalog catalog = await ReadCatalogAsync(api);

        Assert.NotEmpty(catalog.Segments);
        Assert.All(catalog.Segments, item => Assert.Equal(ObjectState.Public, item.State));
        Assert.All(catalog.Locations, item => Assert.Equal(ObjectState.Public, item.State));
        Assert.All(catalog.Services, item => Assert.Equal(ObjectState.Public, item.State));
        Assert.All(catalog.Resources, item => Assert.Equal(ObjectState.Public, item.State));
    }

    [Fact]
    public async Task Paging_ReturnsEveryItemOnce()
    {
        using var api = new LiveApi();
        var request = new ObjectStateListRequest { OnlyPublic = true, PageSize = 5 };

        IReadOnlyList<SegmentApiModel> paged = await api.Client.Segment.GetListAsync(request, Ct);
        IReadOnlyList<SegmentApiModel> whole = await api.Client.Segment.GetListAsync(new ObjectStateListRequest { OnlyPublic = true }, Ct);

        Assert.True(whole.Count > 5, "Účet musí mít víc položek než velikost stránky, jinak test nic neověří.");
        Assert.Equal(whole.Select(item => item.Id).Order(), paged.Select(item => item.Id).Order());
    }

    [Theory]
    [InlineData("OneToOne", "one-to-one")]
    [InlineData("AutoClasses", "auto-classes")]
    [InlineData("EmsLike", "ems-like")]
    public async Task IndividualType_ReturnsStarts(string type, string scenario)
    {
        // Volno zdroje je sjednocení přes všechny veřejné služby pohledu, proto se čte každá.
        // První služba má jméno scénáře bez přípony, testy nad nahrávkami ji tak najdou.
        using LiveApi api = Live();
        Catalog catalog = await ReadCatalogAsync(api);
        (BookingServiceApiModel first, BookingResourceApiModel resource) = catalog.FirstBookable(type);
        List<DateTimeOffset> all = [];

        foreach (BookingServiceApiModel service in catalog.ServicesIn(first.SegmentId, resource.LocationId))
        {
            _recorder.Scenario = service.Id == first.Id ? scenario : $"{scenario}-service-{service.Id}";
            IReadOnlyDictionary<int, IReadOnlyList<DateTimeOffset>> starts = await api.Client.OneToOne.GetAvailableStartsForLocationAsync(
                new AvailableStartsForLocationRequest
                {
                    BookingServiceId = service.Id,
                    LocationId = resource.LocationId,
                    IntervalStart = Today,
                    IntervalEnd = Today.AddDays(60),
                },
                Ct);
            all.AddRange(starts.Values.SelectMany(values => values));
        }

        Assert.NotEmpty(all);
    }

    [Theory]
    [InlineData("OneToOne")]
    [InlineData("AutoClasses")]
    [InlineData("EmsLike")]
    public async Task IndividualType_WorkingHoursOfResources_ReadStrictly(string type)
    {
        using LiveApi api = Live();
        Catalog catalog = await ReadCatalogAsync(api);
        (BookingServiceApiModel first, BookingResourceApiModel resource) = catalog.FirstBookable(type);
        List<WorkingHourApiModel> all = [];

        foreach (BookingResourceApiModel item in catalog.ResourcesIn(first.SegmentId, resource.LocationId))
        {
            _recorder.Scenario = $"resource-{item.Id}";
            all.AddRange(await api.Client.WorkingHours.GetForPeriodAsync(
                new WorkingHoursForPeriodRequest { BookingResourceId = item.Id, IntervalStart = Today, IntervalEnd = Today.AddDays(14) },
                Ct));
        }

        Assert.NotEmpty(all);
        Assert.All(all, hour => Assert.True(hour.End > hour.Start));
    }

    [Fact]
    public async Task Events_ReadStrictly_WithoutCustomerData()
    {
        // Token musí mít právo Event_r, účet testů ho má (volba obsazenosti v průvodci). Nahrávka má jen pole, která
        // aplikace čte, údaje zákazníků zahodí už klient.
        using LiveApi api = Live();

        IReadOnlyList<EventApiModel> events = await api.Client.Event.GetListAsync(
            new EventListRequest { StartDate = Today, EndDate = Today.AddDays(14) },
            Ct);

        Assert.All(events, item => Assert.True(item.EndDate > item.StartDate));
    }

    [Fact]
    public async Task Classes_ReturnAppointments_IncludingFullOnes()
    {
        using LiveApi api = Live();
        Catalog catalog = await ReadCatalogAsync(api);
        List<ExtendedAppointmentClassApiModel> all = [];

        foreach (SegmentApiModel segment in catalog.Segments.Where(item => item.SegmentType == "Classes"))
        {
            foreach (int location in catalog.LocationsOf(segment))
            {
                _recorder.Scenario = $"segment-{segment.Id}-location-{location}";
                all.AddRange(await api.Client.Classes.GetAvailableAppointmentsAsync(
                    new AvailableClassesRequest { SegmentId = segment.Id, LocationId = location, IntervalStart = Today, IntervalEnd = Today.AddDays(60) },
                    Ct));
            }
        }

        Assert.NotEmpty(all);
        Assert.Contains(all, appointment => !appointment.IsAvailable);
    }

    [Fact]
    public async Task Merchant_ReadsStrictly_AndHasName()
    {
        // Bez nahrávání: odpověď nese IČO, kontakty a adresy merchanta, viz docs/decisions.md.
        using var api = new LiveApi();

        MerchantInfoApiModel merchant = await api.Client.Merchant.GetInfoAsync(new EmptyRequest(), Ct);

        Assert.False(string.IsNullOrWhiteSpace(merchant.Name));
    }

    [Fact]
    public async Task InvalidToken_IsRejected_NotOutage()
    {
        // Jediné volání s neplatným tokenem, API na něj odpoví chybou a nic jiného se neděje.
        _recorder.Scenario = "invalid";
        using var api = new LiveApi(Guid.NewGuid().ToString(), ObserverIfRecording());

        await Assert.ThrowsAsync<ReservantoTokenRejectedException>(() => api.Client.Segment.GetListAsync(new ObjectStateListRequest(), Ct));
    }

    private static async Task<Catalog> ReadCatalogAsync(LiveApi api) =>
        new(
            await api.Client.Segment.GetListAsync(new ObjectStateListRequest { OnlyPublic = true }, Ct),
            await api.Client.Location.GetListAsync(new ObjectStateListRequest { OnlyPublic = true }, Ct),
            await api.Client.BookingService.GetListAsync(new ObjectStateModifiedAfterListRequest { OnlyPublic = true }, Ct),
            await api.Client.BookingResource.GetListAsync(new BookingResourceGetListRequest { OnlyPublic = true }, Ct));

    private LiveApi Live() => new(observer: ObserverIfRecording());

    private Func<DelegatingHandler>? ObserverIfRecording() => ApiRecorder.IsEnabled ? _recorder.CreateHandler : null;

    private sealed record Catalog(
        IReadOnlyList<SegmentApiModel> Segments,
        IReadOnlyList<LocationApiModel> Locations,
        IReadOnlyList<BookingServiceApiModel> Services,
        IReadOnlyList<BookingResourceApiModel> Resources)
    {
        public (BookingServiceApiModel Service, BookingResourceApiModel Resource) FirstBookable(string type)
        {
            foreach (SegmentApiModel segment in Segments.Where(item => item.SegmentType == type))
            {
                foreach (BookingServiceApiModel service in Services.Where(item => item.SegmentId == segment.Id))
                {
                    BookingResourceApiModel? resource = Resources.FirstOrDefault(item => service.BookingResourceIds?.Contains(item.Id) == true);
                    if (resource is not null)
                    {
                        return (service, resource);
                    }
                }
            }

            throw new InvalidOperationException($"Účet nemá veřejnou službu se zdrojem typu {type}.");
        }

        public IEnumerable<BookingResourceApiModel> ResourcesIn(int segmentId, int locationId) =>
            Resources.Where(item => item.LocationId == locationId && item.SegmentIds?.Contains(segmentId) == true);

        public IEnumerable<BookingServiceApiModel> ServicesIn(int segmentId, int locationId)
        {
            int[] resources = [.. ResourcesIn(segmentId, locationId).Select(item => item.Id)];
            return Services.Where(item => item.SegmentId == segmentId && item.BookingResourceIds?.Any(resources.Contains) == true);
        }

        public IEnumerable<int> LocationsOf(SegmentApiModel segment) =>
            Resources.Where(item => item.SegmentIds?.Contains(segment.Id) == true).Select(item => item.LocationId).Distinct();
    }
}
