using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Connection;
using AvailabilityView.Application.Tests.Support;

namespace AvailabilityView.Application.Tests;

/// <summary>Doplnění voleb a neplatné hodnoty (<c>docs/SPEC.md</c> sekce 4 a 5).</summary>
public sealed class AvailabilitySelectionTests
{
    [Theory]
    [InlineData(1, SlotKind.FreeTime)]
    [InlineData(2, SlotKind.Lesson)]
    public async Task Result_SaysKindOfSlots_EvenWithoutSlots(int segment, SlotKind kind)
    {
        // Zobrazení se řídí druhem, ne tvarem dat.
        AvailabilityResult result = await new ReaderHarness().ReadAsync(segment);

        Assert.Equal((AvailabilityOutcome.NothingFree, kind), (result.Outcome, result.Kind));
    }

    [Fact]
    public async Task Result_WithoutSelection_HasNoKind()
    {
        Assert.Null((await new ReaderHarness().ReadAsync(segment: 999)).Kind);
    }

    [Fact]
    public async Task NoValues_PickFirstSupportedSegmentWithResources_AndItsFirstOptions()
    {
        var harness = new ReaderHarness();

        AvailabilityResult result = await harness.ReadAsync();

        AvailabilitySelection selection = result.Selection!;
        Assert.Equal(1, selection.Segment.Id);
        Assert.Equal(10, selection.Location.Id);
        Assert.Null(selection.Service);
        Assert.Null(selection.Resource);
    }

    [Theory]
    [InlineData(10, null, null, new[] { 1001 })] // v Praze dělá Jana jen sportovní
    [InlineData(20, null, null, new[] { 1000, 1001 })]
    [InlineData(20, null, 1000, new[] { 1000 })] // zvolená služba
    [InlineData(null, 100, null, new[] { 1001 })] // jen služby zvoleného zdroje
    public async Task Individual_ReadsEveryPublicServiceOfView(int? location, int? resource, int? service, int[] expected)
    {
        // Volno zdroje je sjednocení přes veřejné služby pohledu. Lekce 1002 k zaměření nepatří.
        var harness = new ReaderHarness();

        await harness.ReadAsync(segment: 1, location: location, service: service, resource: resource);

        Assert.Equal(expected, harness.Individual.LastServices!.Select(item => item.Id));
    }

    [Fact]
    public async Task LocationWithoutPublicServiceOfSegment_IsSkipped()
    {
        // Provozovna by nikdy nic neukázala, vybere se další.
        var harness = new ReaderHarness();
        harness.Catalog.Value = ReaderHarness.DefaultCatalog with
        {
            Services = [.. ReaderHarness.DefaultCatalog.Services.Where(item => item.Id != 1001)],
        };

        AvailabilityResult result = await harness.ReadAsync(segment: 1);

        Assert.Equal(20, result.Selection!.Location.Id);
    }

    [Fact]
    public async Task FirstLocationWithoutMatchingResource_IsSkipped()
    {
        var harness = new ReaderHarness();

        AvailabilityResult result = await harness.ReadAsync(service: 1000);

        Assert.Equal(20, result.Selection!.Location.Id);
    }

    [Fact]
    public async Task LessonsWithoutService_ShowAllServices()
    {
        var harness = new ReaderHarness();

        AvailabilityResult result = await harness.ReadAsync(segment: 2);

        Assert.Equal(20, result.Selection!.Location.Id);
        Assert.Null(result.Selection.Service);
        Assert.Equal(1, harness.Lessons.Calls);
    }

    [Fact]
    public async Task ServiceWithoutSegment_FindsSegmentOfService()
    {
        var harness = new ReaderHarness();

        AvailabilityResult result = await harness.ReadAsync(service: 1002);

        Assert.Equal(2, result.Selection!.Segment.Id);
    }

    [Fact]
    public async Task ResourceWithoutLocation_UsesLocationOfResource()
    {
        var harness = new ReaderHarness();

        AvailabilityResult result = await harness.ReadAsync(resource: 101);

        Assert.Equal(20, result.Selection!.Location.Id);
        Assert.Equal(101, result.Selection.Resource!.Id);
    }

    [Theory]
    [InlineData(999, null, null, null)]
    [InlineData(null, 999, null, null)]
    [InlineData(null, null, 999, null)]
    [InlineData(null, null, null, 999)]
    public async Task UnknownOrPrivateValue_IsNotAvailable_AndApiIsNotCalled(int? segment, int? location, int? service, int? resource)
    {
        // Neveřejný objekt v katalogu není, pro stránku je tedy stejný jako neexistující.
        var harness = new ReaderHarness();

        AvailabilityResult result = await harness.ReadAsync(segment, location, service, resource);

        AssertNotAvailable(harness, result);
    }

    [Theory]
    [InlineData(1, null, 1002, null)] // služba z jiného zaměření
    [InlineData(2, 10, null, null)] // zaměření v provozovně nemá zdroj
    [InlineData(null, 10, null, 101)] // zdroj z jiné provozovny
    [InlineData(null, null, 1000, 100)] // zdroj službu neposkytuje
    [InlineData(4, null, null, null)] // zaměření bez zdrojů
    public async Task ValuesThatDoNotBelongTogether_AreNotAvailable(int? segment, int? location, int? service, int? resource)
    {
        var harness = new ReaderHarness();

        AvailabilityResult result = await harness.ReadAsync(segment, location, service, resource);

        AssertNotAvailable(harness, result);
    }

    [Fact]
    public async Task ServiceOfOtherSegment_IsNotAvailable_EvenWhenResourceServesBoth()
    {
        // Zdroj je v obou zaměřeních, spojení přes zdroj tedy nestačí.
        var harness = new ReaderHarness();
        PublicCatalog catalog = ReaderHarness.DefaultCatalog;
        harness.Catalog.Value = catalog with
        {
            Resources = [.. catalog.Resources.Select(item => item.Id == 101 ? item with { SegmentIds = [1, 2] } : item)],
            Services = [.. catalog.Services.Select(item => item.Id == 1002 ? item with { ResourceIds = [101, 102] } : item)],
        };

        AssertNotAvailable(harness, await harness.ReadAsync(segment: 1, service: 1002));
    }

    [Fact]
    public async Task UnsupportedSegment_HasOwnOutcome()
    {
        var harness = new ReaderHarness();

        AvailabilityResult result = await harness.ReadAsync(segment: 3);

        Assert.Equal(AvailabilityOutcome.UnsupportedSegment, result.Outcome);
    }

    [Fact]
    public async Task OnlyUnsupportedSegments_HaveOwnOutcome()
    {
        var harness = new ReaderHarness();
        harness.Catalog.Value = ReaderHarness.DefaultCatalog with { Segments = [new PublicSegment(3, "Půjčovna", "RentalLike")] };

        Assert.Equal(AvailabilityOutcome.UnsupportedSegment, (await harness.ReadAsync()).Outcome);
    }

    [Fact]
    public async Task EmptyPeriod_HasOwnOutcome()
    {
        var harness = new ReaderHarness();

        AvailabilityResult result = await harness.ReadAsync();

        Assert.Equal(AvailabilityOutcome.NothingFree, result.Outcome);
        Assert.NotNull(result.Selection);
    }

    [Fact]
    public async Task NotConnected_DoesNotCallApi()
    {
        var harness = new ReaderHarness();
        harness.Connection.IsConnected = false;

        AvailabilityResult result = await harness.ReadAsync();

        Assert.Equal(AvailabilityOutcome.NotConnected, result.Outcome);
        Assert.Equal(0, harness.Catalog.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidDays_AreNotAvailable(int days)
    {
        var harness = new ReaderHarness();

        AssertNotAvailable(harness, await harness.ReadAsync(days: days));
    }

    [Fact]
    public async Task CatalogFailure_IsReportedAsOutcome()
    {
        var harness = new ReaderHarness();
        harness.Catalog.Failure = ConnectionState.Unavailable;

        Assert.Equal(AvailabilityOutcome.SourceUnavailable, (await harness.ReadAsync()).Outcome);
    }

    [Fact]
    public async Task SourceFailure_IsReportedAsOutcome()
    {
        var harness = new ReaderHarness();
        harness.Individual.Failure = ConnectionState.Unavailable;

        Assert.Equal(AvailabilityOutcome.SourceUnavailable, (await harness.ReadAsync()).Outcome);
    }

    private static void AssertNotAvailable(ReaderHarness harness, AvailabilityResult result)
    {
        Assert.Equal(AvailabilityOutcome.ViewNotAvailable, result.Outcome);
        Assert.Null(result.Selection);
        Assert.Equal(0, harness.Individual.Calls + harness.Lessons.Calls);
    }
}
