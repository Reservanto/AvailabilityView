using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Tests.Support;

namespace AvailabilityView.Application.Tests;

/// <summary>Časové pásmo a období po místních dnech (<c>docs/SPEC.md</c> sekce 9).</summary>
public sealed class AvailabilityTimeTests
{
    [Theory]
    [InlineData(7, 2, 14)] // červenec, letní čas
    [InlineData(1, 1, 13)] // leden, zimní čas
    public async Task Slot_IsInMerchantTimeZone_WhateverTheServerZone(int month, int offsetHours, int localHour)
    {
        var harness = new ReaderHarness();
        harness.Time.SetUtcNow(new DateTimeOffset(2027, month, 10, 6, 0, 0, TimeSpan.Zero));
        var start = new DateTimeOffset(2027, month, 10, 12, 0, 0, TimeSpan.Zero);
        harness.Individual.Slots.Add(Slot(start, start.AddHours(1)));

        AvailabilitySlot slot = Assert.Single((await harness.ReadAsync()).Slots);

        Assert.Equal(TimeSpan.FromHours(offsetHours), slot.Start.Offset);
        Assert.Equal(localHour, slot.Start.Hour);
        Assert.Equal(start, slot.Start);
    }

    [Fact]
    public async Task Today_IsTakenInMerchantTimeZone()
    {
        // V UTC je ještě 31. 10., v Praze už 1. 11.
        var harness = new ReaderHarness();
        harness.Time.SetUtcNow(new DateTimeOffset(2027, 10, 31, 23, 30, 0, TimeSpan.Zero));

        await harness.ReadAsync();

        Assert.Equal(new DateTimeOffset(2027, 11, 1, 0, 0, 0, TimeSpan.FromHours(1)), harness.Individual.LastInterval!.Start);
    }

    [Fact]
    public async Task Interval_AlwaysCoversMaxDays_AndNeverMore()
    {
        var harness = new ReaderHarness { MaxDays = 30 };

        await harness.ReadAsync(days: 500);

        AvailabilityInterval interval = harness.Individual.LastInterval!;
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.FromHours(1)), interval.Start);
        Assert.Equal(new DateTimeOffset(2026, 12, 6, 0, 0, 0, TimeSpan.FromHours(1)), interval.End);
    }

    [Fact]
    public async Task Days_LimitsResult_ByLocalDays()
    {
        var harness = new ReaderHarness();
        harness.Individual.Slots.AddRange([
            Slot(Local(2026, 11, 6, 23, 59)),
            Slot(Local(2026, 11, 7, 0, 0)),
        ]);

        AvailabilityResult result = await harness.ReadAsync(days: 1);

        Assert.Equal(Local(2026, 11, 6, 23, 59), Assert.Single(result.Slots).Start);
    }

    [Fact]
    public async Task DaysOverMaxDays_DoNotReachBeyondMaxDays()
    {
        var harness = new ReaderHarness { MaxDays = 2 };
        harness.Individual.Slots.AddRange([Slot(Local(2026, 11, 7, 12, 0)), Slot(Local(2026, 11, 8, 12, 0))]);

        AvailabilityResult result = await harness.ReadAsync(days: 500);

        Assert.Equal(Local(2026, 11, 7, 12, 0), Assert.Single(result.Slots).Start);
    }

    [Fact]
    public async Task PastSlots_AreLeftOut_CurrentMomentStays()
    {
        var harness = new ReaderHarness();
        DateTimeOffset now = harness.Time.GetUtcNow();
        harness.Individual.Slots.AddRange([Slot(now.AddMinutes(-1)), Slot(now)]);

        AvailabilityResult result = await harness.ReadAsync();

        Assert.Equal(now, Assert.Single(result.Slots).Start);
    }

    [Fact]
    public async Task LessonThatStartsWhileLoading_IsLeftOut()
    {
        var harness = new ReaderHarness();
        DateTimeOffset now = harness.Time.GetUtcNow();
        harness.Lessons.Slots.Add(new SourceSlot(now.AddSeconds(1), now.AddHours(1), 1002, 102, "3/10", false));
        harness.Lessons.WhileReading = () => harness.Time.Advance(TimeSpan.FromSeconds(2));

        Assert.Empty((await harness.ReadAsync(segment: 2, location: 20)).Slots);
    }

    [Fact]
    public async Task MidnightWhileLoading_GivesPeriodOfTheNewDay()
    {
        // Žádost 9. 11. ve 23:59:59, čtení skončí 10. 11. v 0:00:01. Lekce 10. 11. v 9:00 je dnes, ne zítra.
        var harness = new ReaderHarness();
        harness.Time.SetUtcNow(new DateTimeOffset(2026, 11, 9, 23, 59, 59, TimeSpan.FromHours(1)));
        harness.Lessons.Slots.Add(new SourceSlot(Local(2026, 11, 10, 9, 0), Local(2026, 11, 10, 10, 0), 1002, 102, "3/10", false));
        harness.Lessons.WhileReading = () => harness.Time.Advance(TimeSpan.FromSeconds(2));

        AvailabilityResult result = await harness.ReadAsync(segment: 2, location: 20, days: 1);

        Assert.Equal(new DateOnly(2026, 11, 10), result.Period!.Today);
        Assert.Equal(new DateOnly(2026, 11, 11), result.Period.OfferedEnd);
        Assert.Equal(Local(2026, 11, 10, 9, 0), Assert.Single(result.Slots).Start);
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.FromHours(1)), harness.Lessons.LastInterval!.Start);
    }

    [Fact]
    public async Task FreeTimeThatStartsWhileLoading_StaysForItsLaterStarts()
    {
        // Úsek sestavilo čtení volna ze začátků od NotBefore. Začátky, které mezitím prošly, vyřadí čtení volna samo,
        // čtení dostupnosti úsek zahodit nesmí.
        var harness = new ReaderHarness();
        DateTimeOffset now = harness.Time.GetUtcNow();
        harness.Individual.Slots.Add(Slot(now.AddSeconds(1), now.AddMinutes(90)));
        harness.Individual.WhileReading = () => harness.Time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(now.AddSeconds(1), Assert.Single((await harness.ReadAsync()).Slots).Start);
    }

    [Fact]
    public async Task SpringForward_DayHas23Hours_AndEndsAtLocalMidnight()
    {
        // 28. 3. 2027 se v Praze ve 2:00 posouvá čas. Přičtení 24 hodin by skončilo v 1:00 dalšího dne.
        var harness = new ReaderHarness();
        harness.Time.SetUtcNow(new DateTimeOffset(2027, 3, 27, 23, 30, 0, TimeSpan.Zero));
        DateTimeOffset lastMinute = new(2027, 3, 28, 23, 59, 0, TimeSpan.FromHours(2));
        DateTimeOffset nextDay = new(2027, 3, 29, 0, 30, 0, TimeSpan.FromHours(2));
        harness.Individual.Slots.AddRange([Slot(lastMinute), Slot(nextDay)]);

        AvailabilityResult result = await harness.ReadAsync(days: 1);

        Assert.Equal(lastMinute, Assert.Single(result.Slots).Start);
        Assert.Equal(TimeSpan.FromHours(23), new DateTimeOffset(2027, 3, 29, 0, 0, 0, TimeSpan.FromHours(2)) - harness.Individual.LastInterval!.Start);
    }

    [Fact]
    public async Task FallBack_DayHas25Hours_AndKeepsItsLastHour()
    {
        // 31. 10. 2027 se v Praze ve 3:00 vrací čas. Přičtení 24 hodin by poslední hodinu dne ztratilo.
        var harness = new ReaderHarness();
        harness.Time.SetUtcNow(new DateTimeOffset(2027, 10, 30, 22, 30, 0, TimeSpan.Zero));
        DateTimeOffset lastHour = new(2027, 10, 31, 23, 30, 0, TimeSpan.FromHours(1));
        harness.Individual.Slots.Add(Slot(lastHour));

        AvailabilityResult result = await harness.ReadAsync(days: 1);

        Assert.Equal(lastHour, Assert.Single(result.Slots).Start);
    }

    [Fact]
    public async Task FallBack_RepeatedHour_KeepsBothSlotsWithTheirOffsets()
    {
        var harness = new ReaderHarness();
        harness.Time.SetUtcNow(new DateTimeOffset(2027, 10, 30, 22, 30, 0, TimeSpan.Zero));
        harness.Individual.Slots.AddRange([
            Slot(new DateTimeOffset(2027, 10, 31, 0, 30, 0, TimeSpan.Zero)),
            Slot(new DateTimeOffset(2027, 10, 31, 1, 30, 0, TimeSpan.Zero)),
        ]);

        AvailabilityResult result = await harness.ReadAsync(days: 1);

        Assert.Equal([TimeSpan.FromHours(2), TimeSpan.FromHours(1)], result.Slots.Select(slot => slot.Start.Offset));
        Assert.All(result.Slots, slot => Assert.Equal(2, slot.Start.Hour));
    }

    [Fact]
    public async Task ZoneWhereMidnightDoesNotExist_StartsDayAfterTheGap()
    {
        // Na Kubě se letní čas zapíná o půlnoci, 14. 3. 2027 v 0:00 hodiny skočí na 1:00.
        var harness = new ReaderHarness { TimeZone = "America/Havana" };
        harness.Time.SetUtcNow(new DateTimeOffset(2027, 3, 14, 12, 0, 0, TimeSpan.Zero));

        await harness.ReadAsync();

        Assert.Equal(new DateTimeOffset(2027, 3, 14, 1, 0, 0, TimeSpan.FromHours(-4)), harness.Individual.LastInterval!.Start);
    }

    [Fact]
    public async Task ZoneWhereMidnightRepeats_StartsDayAtFirstMidnight()
    {
        // Na Kubě se čas vrací z 1:00 na 0:00, 7. 11. 2027 je půlnoc dvakrát.
        var harness = new ReaderHarness { TimeZone = "America/Havana" };
        harness.Time.SetUtcNow(new DateTimeOffset(2027, 11, 7, 15, 0, 0, TimeSpan.Zero));

        await harness.ReadAsync();

        Assert.Equal(new DateTimeOffset(2027, 11, 7, 0, 0, 0, TimeSpan.FromHours(-4)), harness.Individual.LastInterval!.Start);
    }

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.FromHours(1));

    private static SourceSlot Slot(DateTimeOffset start) => Slot(start, start.AddHours(1));

    private static SourceSlot Slot(DateTimeOffset start, DateTimeOffset end) =>
        new(start, end, 1001, 100, null, false);
}
