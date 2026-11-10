using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Connection;
using AvailabilityView.Application.Tests.Support;

namespace AvailabilityView.Application.Tests;

/// <summary>
/// Období ve výsledku a týdny kalendáře. Výchozí čas harnessu je pátek
/// 6. 11. 2026 v 9:00 v Praze, se 14 dny se nabízí 6. až 19. 11., tedy týdny od 2., 9. a 16. 11.
/// </summary>
public sealed class AvailabilityPeriodTests
{
    private static readonly DateOnly _today = new(2026, 11, 6);

    [Fact]
    public async Task Agenda_ShowsWholeOfferedPeriod_WithoutWeeks()
    {
        AvailabilityResult result = await new ReaderHarness().ReadAsync();

        Assert.Equal(new AvailabilityPeriod(_today, new DateOnly(2026, 11, 20), _today, new DateOnly(2026, 11, 20), null, null), result.Period);
    }

    [Fact]
    public async Task OfferedPeriod_IsCutToMaxDays()
    {
        AvailabilityResult result = await new ReaderHarness { MaxDays = 3 }.ReadAsync(days: 500);

        Assert.Equal(new DateOnly(2026, 11, 9), result.Period!.OfferedEnd);
    }

    [Fact]
    public async Task WeekWithoutDate_IsCurrentWeekFromMonday()
    {
        AvailabilityPeriod period = (await ReadWeekAsync(null)).Period!;

        Assert.Equal(new DateOnly(2026, 11, 2), period.ShownStart);
        Assert.Equal(new DateOnly(2026, 11, 9), period.ShownEnd);
        Assert.Null(period.Previous);
        Assert.Equal(new DateOnly(2026, 11, 9), period.Next);
    }

    [Fact]
    public async Task WeekInMiddle_HasBothNeighbours()
    {
        AvailabilityPeriod period = (await ReadWeekAsync(new DateOnly(2026, 11, 15))).Period!;

        Assert.Equal(new DateOnly(2026, 11, 9), period.ShownStart);
        Assert.Equal(new DateOnly(2026, 11, 2), period.Previous);
        Assert.Equal(new DateOnly(2026, 11, 16), period.Next);
    }

    [Fact]
    public async Task LastWeek_IsTheOneWithLastOfferedDay()
    {
        AvailabilityPeriod period = (await ReadWeekAsync(new DateOnly(2026, 11, 19))).Period!;

        Assert.Equal(new DateOnly(2026, 11, 16), period.ShownStart);
        Assert.Equal(new DateOnly(2026, 11, 9), period.Previous);
        Assert.Null(period.Next);
    }

    [Theory]
    [InlineData(2026, 11, 20, 16)] // první den za obdobím
    [InlineData(9999, 12, 31, 16)]
    [InlineData(2026, 11, 5, 2)] // včerejšek
    [InlineData(1, 1, 1, 2)]
    public async Task WeekOutsideOfferedPeriod_MovesToNearestOfferedWeek(int year, int month, int day, int expectedMonday)
    {
        AvailabilityPeriod period = (await ReadWeekAsync(new DateOnly(year, month, day))).Period!;

        Assert.Equal(new DateOnly(2026, 11, expectedMonday), period.ShownStart);
    }

    [Fact]
    public async Task OneOfferedDay_HasSingleWeek()
    {
        AvailabilityPeriod period = (await ReadWeekAsync(new DateOnly(2026, 11, 30), days: 1)).Period!;

        Assert.Equal(new DateOnly(2026, 11, 2), period.ShownStart);
        Assert.Null(period.Previous);
        Assert.Null(period.Next);
    }

    [Fact]
    public async Task TodayIsSunday_WeekStartsSixDaysBefore()
    {
        var harness = new ReaderHarness();
        harness.Time.SetUtcNow(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));

        AvailabilityPeriod period = (await harness.ReadAsync(WeekQuery(null))).Period!;

        Assert.Equal(new DateOnly(2026, 11, 2), period.ShownStart);
        Assert.True(period.IsOffered(new DateOnly(2026, 11, 8)));
        Assert.False(period.IsOffered(new DateOnly(2026, 11, 7)));
    }

    [Fact]
    public async Task Week_HasOnlySlotsOfItsOfferedDays()
    {
        var harness = new ReaderHarness();
        harness.Individual.Slots.AddRange([
            Slot(Local(2026, 11, 15, 23, 29)), // předchozí týden, končí před půlnocí
            Slot(Local(2026, 11, 16, 0, 0)),
            Slot(Local(2026, 11, 19, 23, 59)), // poslední nabízený den
            Slot(Local(2026, 11, 20, 0, 0)), // týden pokračuje, nabídka ne
        ]);

        AvailabilityResult result = await harness.ReadAsync(WeekQuery(new DateOnly(2026, 11, 16)));

        Assert.Equal([Local(2026, 11, 16, 0, 0), Local(2026, 11, 19, 23, 59)], result.Slots.Select(slot => slot.Start));
    }

    [Fact]
    public async Task CurrentWeek_LeavesOutWhatAlreadyStarted()
    {
        var harness = new ReaderHarness();
        DateTimeOffset now = harness.Time.GetUtcNow();
        harness.Individual.Slots.AddRange([Slot(now.AddMinutes(-1)), Slot(now)]);

        AvailabilityResult result = await harness.ReadAsync(WeekQuery(null));

        Assert.Equal(now, Assert.Single(result.Slots).Start);
    }

    [Fact]
    public async Task WeekWithSpringForward_EndsAtLocalMidnightOfSunday()
    {
        // 28. 3. 2027 se v Praze ve 2:00 posouvá čas, týden má o hodinu méně.
        var harness = new ReaderHarness();
        harness.Time.SetUtcNow(new DateTimeOffset(2027, 3, 24, 8, 0, 0, TimeSpan.Zero));
        DateTimeOffset lastMinute = new(2027, 3, 28, 23, 59, 0, TimeSpan.FromHours(2));
        DateTimeOffset nextMonday = new(2027, 3, 29, 0, 0, 0, TimeSpan.FromHours(2));
        harness.Individual.Slots.AddRange([Slot(lastMinute), Slot(nextMonday)]);

        AvailabilityResult result = await harness.ReadAsync(WeekQuery(null));

        Assert.Equal(new DateOnly(2027, 3, 22), result.Period!.ShownStart);
        Assert.Equal(lastMinute, Assert.Single(result.Slots).Start);
    }

    [Fact]
    public async Task WeekWithFallBack_KeepsRepeatedHourAndLastHourOfSunday()
    {
        // 31. 10. 2027 se v Praze ve 3:00 vrací čas, týden má o hodinu víc.
        var harness = new ReaderHarness();
        harness.Time.SetUtcNow(new DateTimeOffset(2027, 10, 27, 8, 0, 0, TimeSpan.Zero));
        harness.Individual.Slots.AddRange([
            Slot(new DateTimeOffset(2027, 10, 31, 2, 30, 0, TimeSpan.FromHours(2))),
            Slot(new DateTimeOffset(2027, 10, 31, 2, 30, 0, TimeSpan.FromHours(1))),
            Slot(new DateTimeOffset(2027, 10, 31, 23, 30, 0, TimeSpan.FromHours(1))),
            Slot(new DateTimeOffset(2027, 11, 1, 0, 0, 0, TimeSpan.FromHours(1))),
        ]);

        AvailabilityResult result = await harness.ReadAsync(WeekQuery(null));

        Assert.Equal(3, result.Slots.Count);
        Assert.Equal(new DateOnly(2027, 11, 1), result.Period!.ShownEnd);
    }

    [Fact]
    public async Task RangeQuery_DoesNotChangeWhatIsRead()
    {
        // Cache drží celé nejdelší období, týden ho jen filtruje.
        var harness = new ReaderHarness();
        await harness.ReadAsync();
        AvailabilityInterval agenda = harness.Individual.LastInterval!;

        await harness.ReadAsync(WeekQuery(new DateOnly(2026, 11, 16)));

        Assert.Equal(agenda, harness.Individual.LastInterval);
    }

    [Fact]
    public async Task NothingFree_StillHasPeriod_ForWeekNavigation()
    {
        AvailabilityResult result = await ReadWeekAsync(null);

        Assert.Equal(AvailabilityOutcome.NothingFree, result.Outcome);
        Assert.NotNull(result.Period);
    }

    [Fact]
    public async Task NotAvailable_HasNoPeriod()
    {
        AvailabilityResult result = await new ReaderHarness().ReadAsync(segment: 999);

        Assert.Null(result.Period);
    }

    [Fact]
    public async Task Malformed_IsNotAvailable_WithoutCallingApi()
    {
        var harness = new ReaderHarness();

        AvailabilityResult result = await harness.ReadAsync(AvailabilityQuery.Malformed);

        Assert.Equal(AvailabilityResult.Without(AvailabilityOutcome.ViewNotAvailable), result);
        Assert.Equal(0, harness.Catalog.Calls);
    }

    [Fact]
    public async Task Malformed_OnNotConnectedInstance_SaysNotConnected()
    {
        // Správce nepřipojené instance tak dostane odkaz na průvodce, ať je adresa jakákoli.
        var harness = new ReaderHarness();
        harness.Connection.IsConnected = false;

        Assert.Equal(AvailabilityOutcome.NotConnected, (await harness.ReadAsync(AvailabilityQuery.Malformed)).Outcome);
    }

    [Fact]
    public async Task Failure_HasNoPeriod()
    {
        var harness = new ReaderHarness();
        harness.Individual.Failure = ConnectionState.Unavailable;

        Assert.Null((await harness.ReadAsync(WeekQuery(null))).Period);
    }

    private static Task<AvailabilityResult> ReadWeekAsync(DateOnly? containing, int days = 14) =>
        new ReaderHarness().ReadAsync(WeekQuery(containing, days));

    private static AvailabilityQuery WeekQuery(DateOnly? containing, int days = 14) =>
        new(null, null, null, null, days) { Range = new RangeQuery(ShownRange.Week, containing) };

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.FromHours(1));

    private static SourceSlot Slot(DateTimeOffset start) => new(start, start.AddMinutes(30), 1001, 100, null, false);
}
