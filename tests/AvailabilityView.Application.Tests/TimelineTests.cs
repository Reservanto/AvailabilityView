using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Connection;
using AvailabilityView.Application.Tests.Support;

namespace AvailabilityView.Application.Tests;

/// <summary>Úseky hodinové osy z volna, pracovní doby a obsazenosti.</summary>
public sealed class TimelineTests
{
    // Pondělí 9. 11. 2026, zimní čas. V harness je pátek 6. 11. 9:00 v Praze.
    private static readonly DateOnly _monday = new(2026, 11, 9);

    [Fact]
    public async Task FreeTime_Booking_AndRestOfWorkingHours_MakeBands()
    {
        var harness = new ReaderHarness();
        harness.Individual.Slots.Add(Free(100, At(9), At(12)));
        harness.Individual.Timeline = new SourceTimeline([Work(100, At(9), At(17))], [Work(100, At(14), At(15))]);

        AvailabilityResult result = await ReadWeekAsync(harness);

        Assert.Equal(
            [
                new TimeBand(At(9), At(12), TimeBandState.Free),
                new TimeBand(At(12), At(14), TimeBandState.Unavailable),
                new TimeBand(At(14), At(15), TimeBandState.Busy),
                new TimeBand(At(15), At(17), TimeBandState.Unavailable),
            ],
            result.Bands);
    }

    [Fact]
    public async Task FreeTime_WinsOverBooking()
    {
        // Zdroj s kapacitou pro víc lidí může mít volno i během rezervace (SPEC sekce 3).
        var harness = new ReaderHarness();
        harness.Individual.Slots.Add(Free(100, At(14, 30), At(15, 30)));
        harness.Individual.Timeline = new SourceTimeline([Work(100, At(14), At(16))], [Work(100, At(14), At(15))]);

        AvailabilityResult result = await ReadWeekAsync(harness);

        Assert.Equal(
            [
                new TimeBand(At(14), At(14, 30), TimeBandState.Busy),
                new TimeBand(At(14, 30), At(15, 30), TimeBandState.Free),
                new TimeBand(At(15, 30), At(16), TimeBandState.Unavailable),
            ],
            result.Bands);
    }

    [Fact]
    public async Task OutsideWorkingHours_NothingIsDrawn_EvenBooking()
    {
        var harness = new ReaderHarness();
        harness.Individual.Timeline = new SourceTimeline([Work(100, At(9), At(10))], [Work(100, At(18), At(19))]);

        AvailabilityResult result = await ReadWeekAsync(harness);

        Assert.Equal([new TimeBand(At(9), At(10), TimeBandState.Unavailable)], result.Bands);
    }

    [Fact]
    public async Task OtherResources_DoNotCount()
    {
        // Zdroj 101 je v Brně, pohled je v Praze. Jeho pracovní doba ani rezervace se nesmí ukázat.
        var harness = new ReaderHarness();
        harness.Individual.Timeline = new SourceTimeline(
            [Work(100, At(9), At(10)), Work(101, At(7), At(9)), Work(999, At(11), At(12))],
            [Work(101, At(9), At(10))]);

        AvailabilityResult result = await ReadWeekAsync(harness);

        Assert.Equal([new TimeBand(At(9), At(10), TimeBandState.Unavailable)], result.Bands);
    }

    [Fact]
    public async Task BookingOfOneResource_WhileOtherWorks_IsBusy()
    {
        // Bez určeného zdroje: Jana pracuje bez rezervace, Eva má rezervaci.
        var harness = new ReaderHarness();
        harness.Catalog.Value = ReaderHarness.DefaultCatalog with
        {
            Resources = [.. ReaderHarness.DefaultCatalog.Resources, new PublicResource(103, "Eva", 10, [1])],
        };
        harness.Individual.Timeline = new SourceTimeline(
            [Work(100, At(9), At(10)), Work(103, At(9), At(10))],
            [Work(103, At(9), At(10))]);

        AvailabilityResult result = await ReadWeekAsync(harness);

        Assert.Equal([new TimeBand(At(9), At(10), TimeBandState.Busy)], result.Bands);
    }

    [Fact]
    public async Task ChosenService_LeavesOutResourcesThatDoNotProvideIt()
    {
        // Eva službu 1001 neposkytuje, její rezervace pod touto službou obsazenost není.
        var harness = new ReaderHarness();
        harness.Catalog.Value = ReaderHarness.DefaultCatalog with
        {
            Resources = [.. ReaderHarness.DefaultCatalog.Resources, new PublicResource(103, "Eva", 10, [1])],
        };
        harness.Individual.Timeline = new SourceTimeline(
            [Work(100, At(9), At(10)), Work(103, At(9), At(11))],
            [Work(103, At(9), At(10))]);

        AvailabilityResult result = await harness.ReadAsync(Query() with { ServiceId = 1001 });

        Assert.Equal([new TimeBand(At(9), At(10), TimeBandState.Unavailable)], result.Bands);
    }

    [Fact]
    public async Task ReadsWholeShownWeek_FromMondayInMerchantZone()
    {
        var harness = new ReaderHarness();
        harness.Individual.Timeline = new SourceTimeline([], []);

        await ReadWeekAsync(harness);

        Assert.Equal(new AvailabilityInterval(At(0), At(0).AddDays(7)), harness.Individual.LastWeek);
    }

    [Theory]
    [InlineData(false, true)] // kalendář se sloupci dnů
    [InlineData(true, false)] // agenda
    public async Task WithoutTimegrid_NoWorkingHoursOrBookingsAreRead(bool timeline, bool week)
    {
        var harness = new ReaderHarness();
        harness.Individual.Timeline = new SourceTimeline([Work(100, At(9), At(10))], []);
        AvailabilityQuery query = Query() with { Timeline = timeline, Range = week ? new RangeQuery(ShownRange.Week, _monday) : null };

        AvailabilityResult result = await harness.ReadAsync(query);

        Assert.Equal(0, harness.Individual.TimelineCalls);
        Assert.Empty(result.Bands);
    }

    [Fact]
    public async Task Past_IsNotDrawn()
    {
        // V harness je pátek 6. 11. 9:00 v Praze.
        var harness = new ReaderHarness();
        DateTimeOffset friday = new(2026, 11, 6, 0, 0, 0, TimeSpan.FromHours(1));
        harness.Individual.Timeline = new SourceTimeline([Work(100, friday.AddHours(7), friday.AddHours(17))], []);

        AvailabilityResult result = await harness.ReadAsync(Query() with { Range = new RangeQuery(ShownRange.Week, new DateOnly(2026, 11, 6)) });

        Assert.Equal([new TimeBand(friday.AddHours(9), friday.AddHours(17), TimeBandState.Unavailable)], result.Bands);
    }

    [Fact]
    public async Task DaysOutsideOffer_HaveNoBands()
    {
        var harness = new ReaderHarness();
        DateTimeOffset saturday = new(2026, 11, 7, 0, 0, 0, TimeSpan.FromHours(1));
        harness.Individual.Timeline = new SourceTimeline([Work(100, saturday.AddHours(9), saturday.AddHours(17))], []);

        AvailabilityResult result = await harness.ReadAsync(Query(days: 1) with { Range = new RangeQuery(ShownRange.Week, new DateOnly(2026, 11, 6)) });

        Assert.Empty(result.Bands);
    }

    [Fact]
    public async Task TimelineFailure_IsOutage_NotPartialGrid()
    {
        var harness = new ReaderHarness();
        harness.Individual.Timeline = new SourceTimeline([], []);
        harness.Individual.Failure = ConnectionState.Unavailable;

        AvailabilityResult result = await ReadWeekAsync(harness);

        Assert.Equal(AvailabilityOutcome.SourceUnavailable, result.Outcome);
        Assert.Empty(result.Bands);
    }

    [Fact]
    public async Task Lessons_HaveNoBands()
    {
        var harness = new ReaderHarness();
        harness.Lessons.Timeline = null;

        AvailabilityResult result = await harness.ReadAsync(Query(segment: 2, location: null));

        Assert.Empty(result.Bands);
        Assert.Equal(1, harness.Lessons.TimelineCalls);
    }

    [Fact]
    public async Task WeekWithSummerTimeChange_KeepsLocalTimes()
    {
        // Neděle 28. 3. 2027: ve 2:00 se posouvá na 3:00. Pracovní doba 1:00–5:00 místního času trvá tři hodiny.
        var harness = new ReaderHarness();
        harness.Time.SetUtcNow(new DateTimeOffset(2027, 3, 26, 8, 0, 0, TimeSpan.Zero));
        DateTimeOffset start = new(2027, 3, 28, 1, 0, 0, TimeSpan.FromHours(1));
        DateTimeOffset end = new(2027, 3, 28, 5, 0, 0, TimeSpan.FromHours(2));
        harness.Individual.Timeline = new SourceTimeline([Work(100, start, end)], []);

        AvailabilityResult result = await harness.ReadAsync(Query() with { Range = new RangeQuery(ShownRange.Week, new DateOnly(2027, 3, 28)) });

        TimeBand band = Assert.Single(result.Bands);
        Assert.Equal((1, TimeSpan.FromHours(1)), (band.Start.Hour, band.Start.Offset));
        Assert.Equal((5, TimeSpan.FromHours(2)), (band.End.Hour, band.End.Offset));
        Assert.Equal(TimeSpan.FromHours(3), band.End - band.Start);
    }

    [Fact]
    public async Task FreeTimeFromSundayNight_ReachesIntoMonday_LessonDoesNot()
    {
        // Čtení sjednotí volno přes celé období, úsek tak může začít v neděli předchozího týdne.
        // Lekce je ve čtení lekcí, každý zdroj vrací jen jeden druh termínů.
        var harness = new ReaderHarness();
        harness.Individual.Slots.Add(Free(100, At(-2), At(2)));
        harness.Individual.Timeline = new SourceTimeline([Work(100, At(-4), At(4))], []);
        harness.Lessons.Slots.Add(new SourceSlot(At(-1), At(1), 1002, 102, null, false));

        AvailabilityResult result = await ReadWeekAsync(harness);
        AvailabilityResult lessons = await harness.ReadAsync(Query(segment: 2, location: 20));

        AvailabilitySlot slot = Assert.Single(result.Slots);
        Assert.Equal((At(0), At(2), (string?)null), (slot.Start, slot.End, slot.ServiceName));
        Assert.Equal(
            [new TimeBand(At(0), At(2), TimeBandState.Free), new TimeBand(At(2), At(4), TimeBandState.Unavailable)],
            result.Bands);
        Assert.Empty(lessons.Slots);
    }

    [Fact]
    public async Task FreeTimeFromSundayNight_EndsAtMidnight_InItsOwnWeek()
    {
        // Volno se dělí po dnech (rozhodnutí uživatele 2026-11-09), neděli patří jen část do půlnoci.
        var harness = new ReaderHarness();
        harness.Individual.Slots.Add(Free(100, At(-2), At(2)));

        AvailabilityResult result = await harness.ReadAsync(Query() with { Range = new RangeQuery(ShownRange.Week, _monday.AddDays(-1)) });

        AvailabilitySlot slot = Assert.Single(result.Slots);
        Assert.Equal((At(-2), At(0)), (slot.Start, slot.End));
    }

    [Fact]
    public async Task FreeTimeOverSeveralDays_IsSplitAtEachMidnight()
    {
        // Úsek od neděle 22:00 do úterý 2:00 byl jen v den začátku.
        var harness = new ReaderHarness();
        harness.Individual.Slots.Add(Free(100, At(-2), At(26)));
        harness.Individual.Timeline = new SourceTimeline([Work(100, At(-4), At(30))], []);

        AvailabilityResult result = await ReadWeekAsync(harness);

        Assert.Equal([(At(0), At(24)), (At(24), At(26))], result.Slots.Select(slot => (slot.Start, slot.End)));
        Assert.Equal(
            [new TimeBand(At(0), At(26), TimeBandState.Free), new TimeBand(At(26), At(30), TimeBandState.Unavailable)],
            result.Bands);
    }

    [Fact]
    public async Task FreeTimeOverFallBackNight_IsSplitAtLocalMidnights()
    {
        // 31. 10. 2027 má 25 hodin. Půlnoci se počítají v pásmu merchanta, ne po 24 hodinách.
        var harness = new ReaderHarness();
        harness.Time.SetUtcNow(new DateTimeOffset(2027, 10, 27, 8, 0, 0, TimeSpan.Zero));
        DateTimeOffset saturday = new(2027, 10, 30, 22, 0, 0, TimeSpan.FromHours(2));
        DateTimeOffset monday = new(2027, 11, 1, 2, 0, 0, TimeSpan.FromHours(1));
        harness.Individual.Slots.Add(Free(100, saturday, monday));

        AvailabilityResult result = await harness.ReadAsync(Query() with { Range = new RangeQuery(ShownRange.Week, new DateOnly(2027, 10, 25)), Timeline = false });

        Assert.Equal(
            [
                (new DateTimeOffset(2027, 10, 30, 22, 0, 0, TimeSpan.FromHours(2)), new DateTimeOffset(2027, 10, 31, 0, 0, 0, TimeSpan.FromHours(2))),
                (new DateTimeOffset(2027, 10, 31, 0, 0, 0, TimeSpan.FromHours(2)), new DateTimeOffset(2027, 11, 1, 0, 0, 0, TimeSpan.FromHours(1))),
            ],
            result.Slots.Select(slot => (slot.Start, slot.End)));
    }

    private static Task<AvailabilityResult> ReadWeekAsync(ReaderHarness harness) => harness.ReadAsync(Query());

    private static AvailabilityQuery Query(int segment = 1, int? location = 10, int days = 14) =>
        new(segment, location, null, null, days) { Range = new RangeQuery(ShownRange.Week, _monday), Timeline = true };

    private static DateTimeOffset At(int hour, int minute = 0) =>
        new DateTimeOffset(_monday.Year, _monday.Month, _monday.Day, 0, 0, 0, TimeSpan.FromHours(1)).AddHours(hour).AddMinutes(minute);

    private static SourceSlot Free(int resource, DateTimeOffset start, DateTimeOffset end) => new(start, end, null, resource, null, false);

    private static ResourceInterval Work(int resource, DateTimeOffset start, DateTimeOffset end) => new(resource, start, end);
}
