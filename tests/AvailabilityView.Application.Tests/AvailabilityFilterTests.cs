using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Tests.Support;

namespace AvailabilityView.Application.Tests;

/// <summary>
/// Filtry, které si návštěvník přepíná sám (<c>docs/SPEC.md</c> sekce 4 a 5). Katalog popisuje
/// <see cref="ReaderHarness"/>.
/// </summary>
public sealed class AvailabilityFilterTests
{
    private static readonly AvailabilityFilter[] _all = Enum.GetValues<AvailabilityFilter>();

    [Fact]
    public async Task WithoutPickers_ThereAreNoFilters()
    {
        Assert.Empty((await new ReaderHarness().ReadAsync()).Filters);
    }

    [Fact]
    public async Task AllPickers_OfferOnlyWhatBelongsToHigherChoices()
    {
        AvailabilityResult result = await ReadAsync(Query(pickers: _all));

        Assert.Equal(_all, result.Filters.Select(filter => filter.Filter));
        AssertOptions(result, AvailabilityFilter.Segment, 1, [1, 2]); // nepodporované a bez zdrojů ne
        AssertOptions(result, AvailabilityFilter.Location, 10, [10, 20]);
        AssertOptions(result, AvailabilityFilter.Service, null, [1001]); // Klasická v Praze nikdo nedělá, služba se nedoplňuje
        AssertOptions(result, AvailabilityFilter.Resource, null, [100]);
    }

    [Fact]
    public async Task Options_CarryOnlyIdAndName()
    {
        AvailabilityResult result = await ReadAsync(Query(pickers: [AvailabilityFilter.Location]));

        Assert.Equal([new FilterOption(10, "Praha"), new FilterOption(20, "Brno")], Filter(result, AvailabilityFilter.Location).Options);
    }

    [Theory]
    [InlineData(1)] // individuální: volno ze všech služeb
    [InlineData(2)] // lekce: lekce všech služeb
    public async Task AllowsAll_ForResourceAndService(int segment)
    {
        AvailabilityResult result = await ReadAsync(Query(segment: segment, pickers: [AvailabilityFilter.Service, AvailabilityFilter.Resource]));

        Assert.All(result.Filters, filter => Assert.True(filter.AllowsAll));
        Assert.Null(Filter(result, AvailabilityFilter.Service).SelectedId);
    }

    [Fact]
    public async Task SegmentAndLocation_HaveNoAllOption()
    {
        AvailabilityResult result = await ReadAsync(Query(pickers: [AvailabilityFilter.Segment, AvailabilityFilter.Location]));

        Assert.All(result.Filters, filter => Assert.False(filter.AllowsAll));
    }

    [Fact]
    public async Task PickerValueThatDoesNotBelong_IsDroppedAndFilledAgain()
    {
        // Návštěvník přepnul na lekce, ty v Praze nejsou.
        AvailabilityResult result = await ReadAsync(Query(segment: 2, location: 10, pickers: [AvailabilityFilter.Segment, AvailabilityFilter.Location]));

        Assert.Equal(2, result.Selection!.Segment.Id);
        Assert.Equal(20, result.Selection.Location.Id);
    }

    [Fact]
    public async Task LowerValueThatStillBelongs_IsKept()
    {
        // Služba lekcí k masážím nepatří a zahodí se, Brno zůstane.
        AvailabilityResult result = await ReadAsync(Query(segment: 1, location: 20, service: 1002, pickers: _all));

        Assert.Equal(20, result.Selection!.Location.Id);
        Assert.Null(result.Selection.Service);
    }

    [Fact]
    public async Task HigherPickerWins_OverLowerPicker()
    {
        // Bez JavaScriptu přijdou obě změny najednou. Rozhoduje vyšší filtr, nižší se doplní znovu.
        AvailabilityResult result = await ReadAsync(Query(segment: 2, resource: 100, pickers: [AvailabilityFilter.Segment, AvailabilityFilter.Resource]));

        Assert.Equal(2, result.Selection!.Segment.Id);
        Assert.Null(result.Selection.Resource);
    }

    [Fact]
    public async Task FixedValueThatDoesNotBelong_IsNotAvailable()
    {
        AvailabilityResult result = await ReadAsync(Query(segment: 2, location: 10, pickers: [AvailabilityFilter.Service]));

        Assert.Equal(AvailabilityOutcome.ViewNotAvailable, result.Outcome);
    }

    [Theory]
    [InlineData(AvailabilityFilter.Segment)]
    [InlineData(AvailabilityFilter.Location)]
    [InlineData(AvailabilityFilter.Service)]
    [InlineData(AvailabilityFilter.Resource)]
    public async Task UnknownPickerValue_IsNotAvailable_LikeFixedOne(AvailabilityFilter filter)
    {
        // Neexistující i neveřejná hodnota dá stejnou hlášku jako u pevné hodnoty, nic se tím neprozradí.
        AvailabilityQuery query = filter switch
        {
            AvailabilityFilter.Segment => Query(segment: 999, pickers: _all),
            AvailabilityFilter.Location => Query(location: 999, pickers: _all),
            AvailabilityFilter.Service => Query(service: 999, pickers: _all),
            _ => Query(resource: 999, pickers: _all),
        };

        Assert.Equal(AvailabilityResult.Without(AvailabilityOutcome.ViewNotAvailable), await ReadAsync(query));
    }

    [Fact]
    public async Task UnsupportedSegmentInPicker_HasOwnOutcome()
    {
        AvailabilityResult result = await ReadAsync(Query(segment: 3, pickers: _all));

        Assert.Equal(AvailabilityOutcome.UnsupportedSegment, result.Outcome);
    }

    [Fact]
    public async Task FixedLowerValue_LimitsHigherOptions()
    {
        // Merchant pevně určil zdroj z Brna, Praha se tedy nenabízí.
        AvailabilityResult result = await ReadAsync(Query(resource: 101, pickers: [AvailabilityFilter.Location]));

        AssertOptions(result, AvailabilityFilter.Location, 20, [20]);
    }

    [Fact]
    public async Task PickerValues_DoNotLimitHigherOptions()
    {
        // Zdroj z Prahy zvolil návštěvník, změna provozovny ho zahodí. Brno se proto nabízí.
        AvailabilityResult result = await ReadAsync(Query(resource: 100, pickers: [AvailabilityFilter.Location, AvailabilityFilter.Resource]));

        AssertOptions(result, AvailabilityFilter.Location, 10, [10, 20]);
    }

    [Fact]
    public async Task EveryOffered_Option_GivesThatChoice_AndKeepsHigherOnes()
    {
        // Nabídka a doplnění stojí na stejné logice. Volba z nabídky se nikdy nezahodí ani nezmění vyšší volby.
        AvailabilityQuery start = Query(pickers: _all);
        AvailabilityResult result = await ReadAsync(start);
        int checkedOptions = 0;

        foreach (FilterChoices filter in result.Filters)
        {
            foreach (FilterOption option in filter.Options)
            {
                AvailabilityResult picked = await ReadAsync(With(Current(result, start), filter.Filter, option.Id));

                Assert.Equal(option.Id, ValueOf(picked.Selection!, filter.Filter));
                foreach (AvailabilityFilter higher in _all.Where(item => item < filter.Filter))
                {
                    Assert.Equal(ValueOf(result.Selection!, higher), ValueOf(picked.Selection!, higher));
                }

                checkedOptions++;
            }
        }

        Assert.True(checkedOptions >= 6);
    }

    [Fact]
    public async Task EveryValidChoice_IsOffered()
    {
        // Opačný směr: co jde zvolit k vyšším volbám, to se nabízí.
        var harness = new ReaderHarness();
        AvailabilityResult result = await harness.ReadAsync(Query(segment: 1, pickers: [AvailabilityFilter.Location, AvailabilityFilter.Resource]));
        int[] offered = [.. Filter(result, AvailabilityFilter.Resource).Options.Select(option => option.Id)];

        foreach (PublicResource resource in ReaderHarness.DefaultCatalog.Resources)
        {
            AvailabilityResult picked = await harness.ReadAsync(Query(segment: 1, location: 10, resource: resource.Id));
            Assert.Equal(picked.Selection is not null, offered.Contains(resource.Id));
        }
    }

    public static TheoryData<int?, int?, int?, int?, AvailabilityFilter[]> OptionQueries => new()
    {
        { null, null, null, null, _all },
        { 2, null, null, null, _all },
        { 1, 20, 1002, null, _all }, // nižší hodnota, která nepatří, se zahodí
        { null, null, null, 101, [AvailabilityFilter.Location] }, // pevný zdroj omezí provozovny
        { 2, 10, null, null, [AvailabilityFilter.Segment, AvailabilityFilter.Location] },
        { 1, null, null, null, [] },
    };

    [Theory]
    [MemberData(nameof(OptionQueries))]
    public async Task Options_AreTheSameAsFiltersOfRead_WithoutReadingAvailability(
        int? segment, int? location, int? service, int? resource, AvailabilityFilter[] pickers)
    {
        // Generátor nabídne totéž co stránka a nevolá čtení termínů.
        AvailabilityQuery query = Query(segment, location, service, resource, pickers);
        var harness = new ReaderHarness();
        AvailabilityOptions options = await harness.Reader.OptionsAsync(query, TestContext.Current.CancellationToken);

        Assert.Equal(0, harness.Individual.Calls + harness.Lessons.Calls + harness.Individual.TimelineCalls + harness.Lessons.TimelineCalls);
        AvailabilityResult read = await harness.ReadAsync(query);
        Assert.Equal(AvailabilityOutcome.Available, options.Outcome);
        Assert.Equal(read.Selection, options.Selection);
        Assert.Equal(Flatten(read.Filters), Flatten(options.Filters));
    }

    [Fact]
    public async Task Options_OfViewThatIsNotAvailable_HaveSameOutcomeAsRead()
    {
        foreach (AvailabilityQuery query in new[] { Query(segment: 999, pickers: _all), Query(segment: 3, pickers: _all), AvailabilityQuery.Malformed })
        {
            var harness = new ReaderHarness();
            AvailabilityOptions options = await harness.Reader.OptionsAsync(query, TestContext.Current.CancellationToken);

            Assert.Equal((await harness.ReadAsync(query)).Outcome, options.Outcome);
            Assert.Null(options.Selection);
            Assert.Empty(options.Filters);
        }
    }

    [Fact]
    public async Task Options_OfDisconnectedInstanceOrFailedCatalog_SaySo()
    {
        var disconnected = new ReaderHarness();
        disconnected.Connection.IsConnected = false;
        var failed = new ReaderHarness();
        failed.Catalog.Failure = Connection.ConnectionState.Unavailable;

        Assert.Equal(AvailabilityOutcome.NotConnected, (await disconnected.Reader.OptionsAsync(Query(pickers: _all), TestContext.Current.CancellationToken)).Outcome);
        AvailabilityOptions options = await failed.Reader.OptionsAsync(Query(pickers: _all), TestContext.Current.CancellationToken);
        Assert.Equal((AvailabilityOutcome.SourceUnavailable, Connection.ConnectionState.Unavailable), (options.Outcome, options.Failure));
    }

    private static string[] Flatten(IReadOnlyList<FilterChoices> filters) =>
        [.. filters.Select(filter =>
            $"{filter.Filter}:{filter.SelectedId}:{filter.AllowsAll}:{string.Join(",", filter.Options.Select(option => $"{option.Id}={option.Name}"))}")];

    private static Task<AvailabilityResult> ReadAsync(AvailabilityQuery query) => new ReaderHarness().ReadAsync(query);

    private static AvailabilityQuery Query(
        int? segment = null,
        int? location = null,
        int? service = null,
        int? resource = null,
        IEnumerable<AvailabilityFilter>? pickers = null) =>
        new(segment, location, service, resource, 14) { Pickers = (pickers ?? []).ToHashSet() };

    private static AvailabilityQuery Current(AvailabilityResult result, AvailabilityQuery query) =>
        query with
        {
            SegmentId = result.Selection!.Segment.Id,
            LocationId = result.Selection.Location.Id,
            ServiceId = result.Selection.Service?.Id,
            ResourceId = result.Selection.Resource?.Id,
        };

    private static AvailabilityQuery With(AvailabilityQuery query, AvailabilityFilter filter, int id) => filter switch
    {
        AvailabilityFilter.Segment => query with { SegmentId = id },
        AvailabilityFilter.Location => query with { LocationId = id },
        AvailabilityFilter.Service => query with { ServiceId = id },
        _ => query with { ResourceId = id },
    };

    private static int? ValueOf(AvailabilitySelection selection, AvailabilityFilter filter) => filter switch
    {
        AvailabilityFilter.Segment => selection.Segment.Id,
        AvailabilityFilter.Location => selection.Location.Id,
        AvailabilityFilter.Service => selection.Service?.Id,
        _ => selection.Resource?.Id,
    };

    private static FilterChoices Filter(AvailabilityResult result, AvailabilityFilter filter) =>
        Assert.Single(result.Filters, item => item.Filter == filter);

    private static void AssertOptions(AvailabilityResult result, AvailabilityFilter filter, int? selected, int[] ids)
    {
        FilterChoices choices = Filter(result, filter);
        Assert.Equal(selected, choices.SelectedId);
        Assert.Equal(ids, choices.Options.Select(option => option.Id));
    }
}
