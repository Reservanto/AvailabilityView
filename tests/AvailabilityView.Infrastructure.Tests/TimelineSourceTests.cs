using System.Net;
using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Connection;
using AvailabilityView.Infrastructure.Tests.Support;
using Reservanto.ApiClient.Core;
using Reservanto.ApiClient.Tests.Support;

namespace AvailabilityView.Infrastructure.Tests;

/// <summary>Pracovní doba a obsazenost pro hodinovou osu individuálních typů.</summary>
public sealed class TimelineSourceTests
{
    private static readonly DateTimeOffset _monday = new(2026, 11, 9, 0, 0, 0, TimeSpan.FromHours(1));
    private static readonly AvailabilityInterval _week = new(_monday, _monday.AddDays(7));
    private static readonly AvailabilitySelection _selection = new(new PublicSegment(6, "Masáže", "OneToOne"), new PublicLocation(10, "Praha"), null, null);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WorkingHours_AreReadPerPublicResource_ForTheWeek()
    {
        using var harness = new Harness();
        RespondWorkingHours(harness);

        SourceTimeline timeline = (await ReadAsync(harness, [Resource(100), Resource(101)]))!;

        Assert.Equal(
            [new ResourceInterval(100, At(9), At(17)), new ResourceInterval(101, At(9), At(17))],
            timeline.Working.OrderBy(item => item.ResourceId));
        ReceivedRequest[] requests = [.. harness.Api.Received.Where(request => request.Path == ApiEndpoints.WorkingHoursGetForPeriod.Path)];
        Assert.Equal([100, 101], requests.Select(request => (int)request.Json["BookingResourceId"]!).Order());
        Assert.All(requests, request =>
        {
            Assert.Equal(_week.Start.ToUnixTimeSeconds(), (long)request.Json["IntervalStart"]!);
            Assert.Equal(_week.End.ToUnixTimeSeconds(), (long)request.Json["IntervalEnd"]!);
        });
    }

    [Fact]
    public async Task WorkingHours_AreCachedForAnHour()
    {
        using var harness = new Harness();
        RespondWorkingHours(harness);

        await ReadAsync(harness, [Resource(100)]);
        harness.Time.Advance(TimeSpan.FromMinutes(59));
        await ReadAsync(harness, [Resource(100)]);
        Assert.Equal(1, harness.Api.CountFor(ApiEndpoints.WorkingHoursGetForPeriod.Path));

        harness.Time.Advance(TimeSpan.FromMinutes(2));
        await ReadAsync(harness, [Resource(100)]);
        Assert.Equal(2, harness.Api.CountFor(ApiEndpoints.WorkingHoursGetForPeriod.Path));
    }

    [Fact]
    public async Task Bookings_AreReadForTheWeek()
    {
        using var harness = new Harness();
        RespondWorkingHours(harness);
        harness.Api.Respond(ApiEndpoints.EventGetList.Path, HttpStatusCode.OK, $$"""
            {"IsError":false,"Events":[{"AppointmentId":1,"SourceId":100,"StartDate":{{At(14).ToUnixTimeSeconds()}},"EndDate":{{At(15).ToUnixTimeSeconds()}},"Status":"Confirmed"}]}
            """);

        SourceTimeline timeline = (await ReadAsync(harness, [Resource(100)]))!;

        Assert.Equal([new ResourceInterval(100, At(14), At(15))], timeline.Busy);
        ReceivedRequest request = Assert.Single(harness.Api.Received, received => received.Path == ApiEndpoints.EventGetList.Path);
        Assert.Equal(_week.Start.ToUnixTimeSeconds(), (long)request.Json["StartDate"]!);
        Assert.Equal(_week.End.ToUnixTimeSeconds(), (long)request.Json["EndDate"]!);
    }

    [Fact]
    public async Task TokenWithoutRightToReadBookings_GivesWorkingHoursWithoutBookings()
    {
        using var harness = new Harness();
        RespondWorkingHours(harness);
        harness.Api.Respond(ApiEndpoints.EventGetList.Path, HttpStatusCode.Forbidden, "Forbidden: missing right");

        SourceTimeline timeline = (await ReadAsync(harness, [Resource(100)]))!;

        Assert.NotEmpty(timeline.Working);
        Assert.Empty(timeline.Busy);
    }

    [Theory]
    [InlineData(true)] // pracovní doba
    [InlineData(false)] // rezervace
    public async Task Outage_IsFailure(bool workingHours)
    {
        using var harness = new Harness();
        RespondWorkingHours(harness);
        harness.Api.Respond(
            workingHours ? ApiEndpoints.WorkingHoursGetForPeriod.Path : ApiEndpoints.EventGetList.Path,
            HttpStatusCode.ServiceUnavailable,
            "{}");

        ReservationSystemException exception = await Assert.ThrowsAsync<ReservationSystemException>(() => ReadAsync(harness, [Resource(100)]));

        Assert.Equal(ConnectionState.Unavailable, exception.Reason);
    }

    [Fact]
    public async Task Lessons_HaveNoTimeline_AndReadNothing()
    {
        using var harness = new Harness();

        SourceTimeline? timeline = await harness.Sources.Single(source => source.SegmentTypes.Contains("Classes"))
            .ReadTimelineAsync(new SourceRequest(_selection, [], [Resource(100)], _week, _monday), _week, Ct);

        Assert.Null(timeline);
        Assert.DoesNotContain(harness.Api.Received, request => request.Path != ApiEndpoints.AuthorizeGetShortTimeToken.Path);
    }

    private static Task<SourceTimeline?> ReadAsync(Harness harness, IReadOnlyList<PublicResource> resources) =>
        harness.Sources.Single(source => source.SegmentTypes.Contains("OneToOne"))
            .ReadTimelineAsync(new SourceRequest(_selection, [], resources, _week, _monday), _week, Ct);

    private static void RespondWorkingHours(Harness harness) =>
        harness.Api.Respond(ApiEndpoints.WorkingHoursGetForPeriod.Path, _ => Task.FromResult(FakeApi.Json(HttpStatusCode.OK, $$"""
            {"IsError":false,"BookingResourceId":null,"LocationId":null,"Items":[{"DayOfWeek":1,"Start":{{At(9).ToUnixTimeSeconds()}},"End":{{At(17).ToUnixTimeSeconds()}}}]}
            """)));

    private static PublicResource Resource(int id) => new(id, $"Zdroj {id}", 10, [6]);

    private static DateTimeOffset At(int hour) => _monday.AddHours(hour);
}
