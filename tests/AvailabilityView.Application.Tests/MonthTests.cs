using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Connection;
using AvailabilityView.Application.Tests.Support;

namespace AvailabilityView.Application.Tests;

/// <summary>
/// Měsíční přehled (rozhodnutí uživatele 2026-11-09). Výchozí čas harnessu je pátek 6. 11. 2026
/// v 9:00 v Praze, se 60 dny se nabízí 6. 11. 2026 až 4. 1. 2027.
/// </summary>
public sealed class MonthTests
{
    [Fact]
    public async Task CurrentMonth_IsShownWhole_WithLinkToNextMonth()
    {
        AvailabilityPeriod period = (await ReadMonthAsync(new ReaderHarness(), null)).Period!;

        Assert.Equal(
            new AvailabilityPeriod(new(2026, 11, 6), new(2027, 1, 5), new(2026, 11, 1), new(2026, 12, 1), null, new(2026, 12, 1)),
            period);
    }

    [Theory]
    [InlineData(2026, 12, 15, 2026, 12, 2026, 11, 2027, 1)]
    [InlineData(2027, 1, 4, 2027, 1, 2026, 12, null, null)]
    [InlineData(2027, 3, 1, 2027, 1, 2026, 12, null, null)] // za obdobím: nejbližší nabízený měsíc
    [InlineData(2026, 10, 1, 2026, 11, null, null, 2026, 12)] // před dneškem: aktuální měsíc
    public async Task Month_GoesByMonths_OnlyWithinOffer(
        int year, int month, int day, int shownYear, int shownMonth, int? previousYear, int? previousMonth, int? nextYear, int? nextMonth)
    {
        AvailabilityPeriod period = (await ReadMonthAsync(new ReaderHarness(), new DateOnly(year, month, day))).Period!;

        Assert.Equal(new DateOnly(shownYear, shownMonth, 1), period.ShownStart);
        Assert.Equal(new DateOnly(shownYear, shownMonth, 1).AddMonths(1), period.ShownEnd);
        Assert.Equal(previousYear is null ? null : new DateOnly(previousYear.Value, previousMonth!.Value, 1), period.Previous);
        Assert.Equal(nextYear is null ? null : new DateOnly(nextYear.Value, nextMonth!.Value, 1), period.Next);
    }

    [Fact]
    public async Task OfferEndingMidMonth_HasNoNextMonth_AndStatesOnlyForOfferedDays()
    {
        AvailabilityResult result = await ReadMonthAsync(new ReaderHarness(), null, days: 14);

        Assert.Null(result.Period!.Next);
        Assert.Equal(Enumerable.Range(6, 14).Select(day => new DateOnly(2026, 11, day)), result.Days.Select(item => item.Day));
    }

    [Fact]
    public async Task Individual_FreeFullAndClosed_FromFreeTimeAndWorkingHours()
    {
        var harness = new ReaderHarness();
        harness.Individual.Slots.Add(new SourceSlot(Local(10, 9), Local(10, 10), null, 100, null, false));
        harness.Individual.Timeline = new SourceTimeline(
            [
                new ResourceInterval(100, Local(10, 8), Local(10, 16)),
                new ResourceInterval(100, Local(11, 8), Local(11, 16)), // pracuje, volno nemá
                new ResourceInterval(101, Local(13, 8), Local(13, 16)), // cizí zdroj: jiná provozovna
            ],
            []);

        AvailabilityResult result = await ReadMonthAsync(harness, null, segment: 1, location: 10);

        Assert.Equal(DayState.Free, StateOf(result, 10));
        Assert.Equal(DayState.Full, StateOf(result, 11));
        Assert.Equal(DayState.Closed, StateOf(result, 12));
        Assert.Equal(DayState.Closed, StateOf(result, 13));
        Assert.Equal(AvailabilityOutcome.Available, result.Outcome);
    }

    [Fact]
    public async Task Lessons_FreeWhenAnyIsNotFull_FullWhenAllAre_ClosedWithout()
    {
        var harness = new ReaderHarness();
        harness.Lessons.Slots.AddRange([
            new SourceSlot(Local(10, 9), Local(10, 10), 1002, 102, "10/10", true),
            new SourceSlot(Local(10, 18), Local(10, 19), 1002, 102, "3/10", false),
            new SourceSlot(Local(11, 18), Local(11, 19), 1002, 102, "10/10", true),
        ]);

        AvailabilityResult result = await ReadMonthAsync(harness, null, segment: 2, location: 20);

        Assert.Equal((DayState.Free, DayState.Full, DayState.Closed), (StateOf(result, 10), StateOf(result, 11), StateOf(result, 12)));
        Assert.Equal(0, harness.Lessons.TimelineCalls);
    }

    [Fact]
    public async Task WorkingHours_AreReadOnlyForMonth_ForOfferedDays_WithoutBookings()
    {
        var harness = new ReaderHarness();

        await harness.ReadAsync(new AvailabilityQuery(1, 10, null, null, 60) { Range = new RangeQuery(ShownRange.Week, null), Timeline = false });
        Assert.Equal(0, harness.Individual.WorkingCalls);

        // Hodinová osa s měsícem neplatí: rezervace se pro měsíc nečtou.
        await harness.ReadAsync(new AvailabilityQuery(1, 10, null, null, 60) { Range = new RangeQuery(ShownRange.Month, null), Timeline = true });
        Assert.Equal((1, 0), (harness.Individual.WorkingCalls, harness.Individual.TimelineCalls));
        Assert.Equal(new AvailabilityInterval(Local(6, 0), Local(30, 0).AddDays(1)), harness.Individual.LastWorkingInterval);
    }

    [Fact]
    public async Task OtherRanges_HaveNoDayStates()
    {
        var harness = new ReaderHarness();
        harness.Individual.Slots.Add(new SourceSlot(Local(10, 9), Local(10, 10), null, 100, null, false));

        Assert.Empty((await harness.ReadAsync(new AvailabilityQuery(1, 10, null, null, 60))).Days);
        Assert.Empty((await harness.ReadAsync(new AvailabilityQuery(1, 10, null, null, 60) { Range = new RangeQuery(ShownRange.Week, new DateOnly(2026, 11, 10)) })).Days);
    }

    [Fact]
    public async Task MidnightWhileWorkingHoursLoad_GivesMonthOfTheNewDay()
    {
        // Žádost 9. 11. ve 23:59:59. Volno dorazí hned, pracovní doba až 10. 11. v 0:00:01.
        var harness = new ReaderHarness();
        harness.Time.SetUtcNow(new DateTimeOffset(2026, 11, 9, 23, 59, 59, TimeSpan.FromHours(1)));
        harness.Individual.Slots.Add(new SourceSlot(Local(10, 9), Local(10, 10), null, 100, null, false));
        harness.Individual.Timeline = new SourceTimeline([new ResourceInterval(100, Local(10, 8), Local(10, 16))], []);
        harness.Individual.WhileReadingWorkingHours = () => harness.Time.Advance(TimeSpan.FromSeconds(2));

        AvailabilityResult result = await ReadMonthAsync(harness, null, days: 1, segment: 1, location: 10);

        Assert.Equal(new DateOnly(2026, 11, 10), result.Period!.Today);
        Assert.Equal([new DayAvailability(new DateOnly(2026, 11, 10), DayState.Free)], result.Days);
    }

    [Fact]
    public async Task FailureOfWorkingHours_IsOutage()
    {
        var harness = new ReaderHarness();
        harness.Individual.Failure = ConnectionState.Unavailable;

        Assert.Equal(AvailabilityOutcome.SourceUnavailable, (await ReadMonthAsync(harness, null)).Outcome);
    }

    [Fact]
    public async Task MonthWithFallBack_HasEveryDayOnce_AndNightBelongsToItsDay()
    {
        // 31. 10. 2027 má 25 hodin. Volno 23:30–24:00 zimního času patří ještě do neděle.
        var harness = new ReaderHarness();
        harness.Time.SetUtcNow(new DateTimeOffset(2027, 10, 1, 8, 0, 0, TimeSpan.Zero));
        var lateEvening = new DateTimeOffset(2027, 10, 31, 23, 30, 0, TimeSpan.FromHours(1));
        harness.Individual.Slots.Add(new SourceSlot(lateEvening, lateEvening.AddMinutes(30), null, 100, null, false));

        AvailabilityResult result = await ReadMonthAsync(harness, new DateOnly(2027, 10, 31), segment: 1, location: 10);

        Assert.Equal(Enumerable.Range(1, 31).Select(day => new DateOnly(2027, 10, day)), result.Days.Select(item => item.Day));
        Assert.Equal(DayState.Free, Assert.Single(result.Days, item => item.Day == new DateOnly(2027, 10, 31)).State);
        Assert.Equal(DayState.Closed, Assert.Single(result.Days, item => item.Day == new DateOnly(2027, 10, 30)).State);
    }

    private static Task<AvailabilityResult> ReadMonthAsync(ReaderHarness harness, DateOnly? containing, int days = 60, int? segment = null, int? location = null) =>
        harness.ReadAsync(new AvailabilityQuery(segment, location, null, null, days) { Range = new RangeQuery(ShownRange.Month, containing) });

    private static DayState StateOf(AvailabilityResult result, int novemberDay) =>
        Assert.Single(result.Days, item => item.Day == new DateOnly(2026, 11, novemberDay)).State;

    private static DateTimeOffset Local(int novemberDay, int hour) => new(2026, 11, novemberDay, hour, 0, 0, TimeSpan.FromHours(1));
}
