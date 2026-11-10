using System.Net;
using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Connection;
using AvailabilityView.Infrastructure.Tests.Support;
using Reservanto.ApiClient.Core;

namespace AvailabilityView.Infrastructure.Tests;

/// <summary>Obsazenost zdrojů z rezervací pro hodinovou osu. Odpovědi jsou ručně psané, s údaji zákazníků.</summary>
public sealed class ResourceOccupancyTests
{
    private static readonly AvailabilityInterval _week = new(
        new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.FromHours(1)),
        new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.FromHours(1)));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OnlyHeldReservations_AreBusy()
    {
        using var harness = new Harness();
        harness.Api.Respond(ApiEndpoints.EventGetList.Path, HttpStatusCode.OK, Events(
            Event(100, 1794218400, 1794222000, "Confirmed"),
            Event(101, 1794222000, 1794225600, "ConfirmationRequested"),
            Event(102, 1794225600, 1794229200, "Canceled"),
            Event(103, 1794225600, 1794229200, "Rejected"),
            Event(104, 1794225600, 1794229200, "AlternateSubscribed"),
            Event(105, 1794225600, 1794229200, "AlternateAutoPromotion")));

        OccupancyReading reading = await harness.Get<IResourceOccupancy>().ReadAsync(_week, Ct);

        Assert.Equal(
            [
                new ResourceInterval(100, DateTimeOffset.FromUnixTimeSeconds(1794218400), DateTimeOffset.FromUnixTimeSeconds(1794222000)),
                new ResourceInterval(101, DateTimeOffset.FromUnixTimeSeconds(1794222000), DateTimeOffset.FromUnixTimeSeconds(1794225600)),
            ],
            reading.Busy);
        Assert.True(reading.IsPermitted);
    }

    [Fact]
    public async Task Request_AsksForThePeriod()
    {
        using var harness = new Harness();

        await harness.Get<IResourceOccupancy>().ReadAsync(_week, Ct);

        Newtonsoft.Json.Linq.JObject request = Assert.Single(harness.Api.Received, received => received.Path == ApiEndpoints.EventGetList.Path).Json;
        Assert.Equal(_week.Start.ToUnixTimeSeconds(), (long)request["StartDate"]!);
        Assert.Equal(_week.End.ToUnixTimeSeconds(), (long)request["EndDate"]!);
    }

    [Fact]
    public async Task Occupancy_IsCachedFor60Seconds()
    {
        using var harness = new Harness();
        IResourceOccupancy occupancy = harness.Get<IResourceOccupancy>();

        await occupancy.ReadAsync(_week, Ct);
        harness.Time.Advance(TimeSpan.FromSeconds(59));
        await occupancy.ReadAsync(_week, Ct);
        Assert.Equal(1, harness.Api.CountFor(ApiEndpoints.EventGetList.Path));

        harness.Time.Advance(TimeSpan.FromSeconds(2));
        await occupancy.ReadAsync(_week, Ct);
        Assert.Equal(2, harness.Api.CountFor(ApiEndpoints.EventGetList.Path));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ConnectionState.TokenRejected)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ConnectionState.Unavailable)]
    public async Task ApiFailure_BecomesReason(HttpStatusCode status, ConnectionState reason)
    {
        using var harness = new Harness();
        harness.Api.Respond(ApiEndpoints.EventGetList.Path, status, "{}");

        ReservationSystemException exception = await Assert.ThrowsAsync<ReservationSystemException>(
            () => harness.Get<IResourceOccupancy>().ReadAsync(_week, Ct));

        Assert.Equal(reason, exception.Reason);
    }

    [Fact]
    public async Task ConnectionCheck_TokenWithoutEventRight_IsConnectedWithoutOccupancy()
    {
        using var harness = new Harness();
        harness.UseRecordings();
        harness.Api.Respond(ApiEndpoints.OneToOneGetAvailableStartsForLocation.Path, HttpStatusCode.OK, """{"IsError":false,"Starts":{}}""");
        harness.Api.Respond(ApiEndpoints.EventGetList.Path, HttpStatusCode.Forbidden, "Forbidden: missing right");
        harness.Api.Respond(ApiEndpoints.MerchantGetInfo.Path, HttpStatusCode.OK, """{"IsError":false,"Result":{"Id":7,"Name":"Studio","IsVatPayer":false}}""");

        ConnectionCheckResult result = await harness.Check.CheckAsync(Ct);

        Assert.Equal(ConnectionState.Connected, result.State);
        Assert.False(result.ShowsOccupancy);
    }

    [Fact]
    public async Task TokenWithoutEventRight_IsNotPermitted_AndAskedOncePerHour()
    {
        // Produkční API bez práva Event_r odpoví 403 s krátkým tělem, které není JSON (ověřeno 2026-11-08).
        using var harness = new Harness();
        harness.Api.Respond(ApiEndpoints.EventGetList.Path, HttpStatusCode.Forbidden, "Forbidden: missing right");
        IResourceOccupancy occupancy = harness.Get<IResourceOccupancy>();

        Assert.Same(OccupancyReading.NotPermitted, await occupancy.ReadAsync(_week, Ct));
        harness.Time.Advance(TimeSpan.FromMinutes(59));
        await occupancy.ReadAsync(_week, Ct);
        Assert.Equal(1, harness.Api.CountFor(ApiEndpoints.EventGetList.Path));

        harness.Time.Advance(TimeSpan.FromMinutes(2));
        await occupancy.ReadAsync(_week, Ct);
        Assert.Equal(2, harness.Api.CountFor(ApiEndpoints.EventGetList.Path));
    }

    [Fact]
    public async Task ConnectionCheck_TokenWithEventRight_ShowsOccupancy()
    {
        using var harness = new Harness();
        harness.UseRecordings();
        harness.Api.Respond(ApiEndpoints.OneToOneGetAvailableStartsForLocation.Path, HttpStatusCode.OK, """{"IsError":false,"Starts":{}}""");
        harness.Api.Respond(ApiEndpoints.EventGetList.Path, HttpStatusCode.OK, Events());
        harness.Api.Respond(ApiEndpoints.MerchantGetInfo.Path, HttpStatusCode.OK, """{"IsError":false,"Result":{"Id":7,"Name":"Studio","IsVatPayer":false}}""");

        ConnectionCheckResult result = await harness.Check.CheckAsync(Ct);

        Assert.Equal(ConnectionState.Connected, result.State);
        Assert.True(result.ShowsOccupancy);
    }

    private static string Events(params string[] events) => $$"""{"IsError":false,"Events":[{{string.Join(',', events)}}]}""";

    private static string Event(int resource, long start, long end, string status) => $$"""
        {"AppointmentId":1,"BookingServiceId":2,"CalendarId":3,"CourseId":null,"CustomerId":4,"InternalSourceId":null,
        "LocationId":5,"SourceId":{{resource}},"CalendarName":"K","Count":1,"CustomerFullName":"Zakaznik","CustomerNote":null,
        "CustomValues":null,"EndDate":{{end}},"InternalSourceName":null,"IsPaid":false,"LocationName":"P","MerchantNote":null,
        "NoShowStatus":0,"PaidPartOfPrice":0,"PaymentMethodFormatted":null,"Price":0,"SegmentName":"Z","ServiceName":"S",
        "SourceName":"Zdroj","StartDate":{{start}},"Status":"{{status}}"}
        """;
}
