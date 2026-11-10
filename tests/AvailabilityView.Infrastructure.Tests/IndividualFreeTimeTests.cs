using System.Net;
using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Connection;
using AvailabilityView.Infrastructure.Availability;
using AvailabilityView.Infrastructure.Tests.Support;
using Reservanto.ApiClient.Core;
using Reservanto.ApiClient.Tests.Support;

namespace AvailabilityView.Infrastructure.Tests;

/// <summary>Volno zdrojů jako sjednocení volných začátků přes služby pohledu.</summary>
public sealed class IndividualFreeTimeTests
{
    private static readonly DateTimeOffset _day = new(2026, 11, 11, 0, 0, 0, TimeSpan.FromHours(1));
    private static readonly AvailabilityInterval _interval = new(_day, _day.AddDays(1));
    private static readonly PublicService _short = new(1, "Krátká", 6, TimeSpan.FromMinutes(30), [100, 101]);
    private static readonly PublicService _long = new(2, "Dlouhá", 6, TimeSpan.FromMinutes(120), [100]);
    private static readonly AvailabilitySelection _selection = new(new PublicSegment(6, "Masáže", "OneToOne"), new PublicLocation(10, "Praha"), null, null);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Merge_JoinsOverlappingAndTouching_PerResource()
    {
        IReadOnlyList<SourceSlot> merged = FreeTime.Merge([
            Slot(100, 9, 0, 10, 0),
            Slot(100, 9, 30, 10, 30), // překryv
            Slot(100, 10, 30, 11, 0), // navazuje
            Slot(100, 12, 0, 13, 0), // mezera
            Slot(101, 9, 0, 10, 0), // jiný zdroj
        ]);

        Assert.Equal(
            [Slot(100, 9, 0, 11, 0), Slot(101, 9, 0, 10, 0), Slot(100, 12, 0, 13, 0)],
            merged);
    }

    [Fact]
    public void Merge_KeepsLongerEnd_WhenShorterSlotIsInside()
    {
        Assert.Equal([Slot(100, 9, 0, 12, 0)], FreeTime.Merge([Slot(100, 9, 0, 12, 0), Slot(100, 10, 0, 10, 30)]));
    }

    [Fact]
    public void Merge_AcrossMidnight_IsOneSlot()
    {
        SourceSlot evening = new(_day.AddHours(22), _day.AddHours(24), null, 100, null, false);
        SourceSlot night = new(_day.AddHours(24), _day.AddHours(26), null, 100, null, false);

        Assert.Equal([new SourceSlot(_day.AddHours(22), _day.AddHours(26), null, 100, null, false)], FreeTime.Merge([night, evening]));
    }

    [Fact]
    public async Task EveryServiceIsRead_AndStartsAreUnitedPerResource()
    {
        // Příklad z produkce: služby 30 a 120 min dohromady dají volno 9–12 a 13–17, mezera 12–13 zůstane.
        using var harness = new Harness();
        RespondStarts(harness, new()
        {
            [_short.Id] = $$"""{"100":[{{At(9, 0)}},{{At(10, 0)}},{{At(11, 0)}},{{At(13, 0)}},{{At(13, 30)}}],"101":[{{At(9, 0)}}]}""",
            [_long.Id] = $$"""{"100":[{{At(9, 0)}},{{At(14, 0)}},{{At(15, 0)}}]}""",
        });

        IReadOnlyList<SourceSlot> slots = await ReadAsync(harness, [_short, _long], notBefore: _day);

        Assert.Equal([Slot(100, 9, 0, 11, 30), Slot(101, 9, 0, 9, 30), Slot(100, 13, 0, 17, 0)], slots);
        Assert.Equal(2, harness.Api.CountFor(ApiEndpoints.OneToOneGetAvailableStartsForLocation.Path));
    }

    [Fact]
    public async Task StartsBeforeNow_DoNotMakeFreeTime()
    {
        // Začátek 9:00 už prošel. Kdyby se počítal, úsek by začínal v minulosti a stránka by ho celý zahodila.
        using var harness = new Harness();
        RespondStarts(harness, new() { [_short.Id] = $$"""{"100":[{{At(9, 0)}},{{At(9, 30)}},{{At(10, 0)}}]}""" });

        IReadOnlyList<SourceSlot> slots = await ReadAsync(harness, [_short], notBefore: _day.AddHours(9).AddMinutes(5));

        Assert.Equal([Slot(100, 9, 30, 10, 30)], slots);
    }

    [Fact]
    public async Task StartThatPassesWhileLoading_DoesNotMakeFreeTime_LaterStartsDo()
    {
        // Žádost v 8:59:59, odpověď API v 9:00:01. Začátek 9:00 prošel, úsek začne 9:30.
        using var harness = new Harness();
        harness.Time.SetUtcNow(_day.AddHours(9).AddSeconds(-1));
        string starts = $$"""{"100":[{{At(9, 0)}},{{At(9, 30)}},{{At(10, 0)}}]}""";
        harness.Api.Respond(ApiEndpoints.OneToOneGetAvailableStartsForLocation.Path, _ =>
        {
            harness.Time.SetUtcNow(_day.AddHours(9).AddSeconds(1));
            return Task.FromResult(FakeApi.Json(HttpStatusCode.OK, $$"""{"IsError":false,"Starts":{{starts}}}"""));
        });

        IReadOnlyList<SourceSlot> slots = await ReadAsync(harness, [_short], notBefore: _day.AddHours(9).AddSeconds(-1));

        Assert.Equal([Slot(100, 9, 30, 10, 30)], slots);
    }

    [Fact]
    public async Task FailureOfAnyService_IsFailure_NotPartialFreeTime()
    {
        using var harness = new Harness();
        harness.Api.Respond(ApiEndpoints.OneToOneGetAvailableStartsForLocation.Path, request =>
            Task.FromResult((int)request.Json["BookingServiceId"]! == _long.Id
                ? FakeApi.Json(HttpStatusCode.ServiceUnavailable, "{}")
                : FakeApi.Json(HttpStatusCode.OK, $$$"""{"IsError":false,"Starts":{"100":[{{{At(9, 0)}}}]}}""")));

        ReservationSystemException exception = await Assert.ThrowsAsync<ReservationSystemException>(
            () => ReadAsync(harness, [_short, _long], notBefore: _day));

        Assert.Equal(ConnectionState.Unavailable, exception.Reason);
    }

    [Fact]
    public async Task NoServices_ReadsNothing()
    {
        using var harness = new Harness();

        Assert.Empty(await ReadAsync(harness, [], notBefore: _day));
        Assert.Equal(0, harness.Api.CountFor(ApiEndpoints.OneToOneGetAvailableStartsForLocation.Path));
    }

    private static Task<IReadOnlyList<SourceSlot>> ReadAsync(Harness harness, IReadOnlyList<PublicService> services, DateTimeOffset notBefore) =>
        harness.Sources.Single(source => source.SegmentTypes.Contains("OneToOne"))
            .ReadAsync(new SourceRequest(_selection, services, [], _interval, notBefore), Ct);

    private static void RespondStarts(Harness harness, Dictionary<int, string> startsByService) =>
        harness.Api.Respond(ApiEndpoints.OneToOneGetAvailableStartsForLocation.Path, request =>
            Task.FromResult(FakeApi.Json(
                HttpStatusCode.OK,
                $$"""{"IsError":false,"Starts":{{startsByService[(int)request.Json["BookingServiceId"]!]}}}""")));

    private static long At(int hour, int minute) => _day.AddHours(hour).AddMinutes(minute).ToUnixTimeSeconds();

    private static SourceSlot Slot(int resource, int fromHour, int fromMinute, int toHour, int toMinute) =>
        new(_day.AddHours(fromHour).AddMinutes(fromMinute), _day.AddHours(toHour).AddMinutes(toMinute), null, resource, null, false);
}
