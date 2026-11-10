using System.Text.Json;
using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Connection;
using AvailabilityView.Infrastructure.Tests.Support;
using AvailabilityView.Tests.Shared;
using Reservanto.ApiClient.Core;

namespace AvailabilityView.Infrastructure.Tests;

/// <summary>Čtení každého podporovaného typu nad nahrávkami z produkčního API.</summary>
public sealed class RecordedAvailabilityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("OneToOne", "one-to-one")]
    [InlineData("AutoClasses", "auto-classes")]
    [InlineData("EmsLike", "ems-like")]
    public async Task IndividualType_ReturnsFreeTimeOfResources_WithoutServiceName(string type, string scenario)
    {
        using var harness = new Harness();
        harness.UseRecordings();
        (int serviceId, int locationId) = RecordedChoice(scenario);
        PublicCatalog catalog = await harness.Catalog.GetAsync(Ct);
        PublicService service = catalog.Services.Single(item => item.Id == serviceId);
        Assert.Equal(type, catalog.Segments.Single(item => item.Id == service.SegmentId).Type);

        AvailabilityResult result = await harness.Reader.ReadAsync(new AvailabilityQuery(service.SegmentId, locationId, service.Id, null, 60), Ct);

        // Volné úseky jsou sjednocené začátky služby: každý je aspoň tak dlouhý jako služba a úseky jednoho zdroje
        // se nepřekrývají ani nenavazují.
        Assert.Equal(AvailabilityOutcome.Available, result.Outcome);
        Assert.All(result.Slots, slot =>
        {
            Assert.Null(slot.ServiceName);
            Assert.True(slot.End - slot.Start >= service.Duration);
            Assert.False(slot.IsFull);
            Assert.Null(slot.OccupancyText);
        });
        Assert.All(result.Slots.GroupBy(slot => slot.ResourceName), resource =>
            Assert.All(resource.Zip(resource.Skip(1)), pair => Assert.True(pair.First.End < pair.Second.Start)));
    }

    [Theory]
    [InlineData("one-to-one")]
    [InlineData("auto-classes")]
    [InlineData("ems-like")]
    public async Task IndividualType_WithoutService_UnitesEveryRecordedService(string scenario)
    {
        // Nahrávky mají volné začátky každé veřejné služby pohledu.
        using var harness = new Harness();
        harness.UseRecordings();
        (int serviceId, int locationId) = RecordedChoice(scenario);
        PublicCatalog catalog = await harness.Catalog.GetAsync(Ct);
        int segmentId = catalog.Services.Single(item => item.Id == serviceId).SegmentId;

        AvailabilityResult all = await harness.Reader.ReadAsync(new AvailabilityQuery(segmentId, locationId, null, null, 60), Ct);
        AvailabilityResult one = await harness.Reader.ReadAsync(new AvailabilityQuery(segmentId, locationId, serviceId, null, 60), Ct);

        Assert.Equal(AvailabilityOutcome.Available, all.Outcome);
        Assert.Null(all.Selection!.Service);
        Assert.All(all.Slots, slot => Assert.Null(slot.ServiceName));

        // Každý volný úsek jedné služby leží uvnitř úseku stejného zdroje ze všech služeb.
        Assert.All(one.Slots, slot => Assert.Contains(all.Slots, union =>
            union.ResourceName == slot.ResourceName && union.Start <= slot.Start && slot.End <= union.End));
        Assert.True(harness.Api.CountFor(ApiEndpoints.OneToOneGetAvailableStartsForLocation.Path) > 1);
    }

    [Fact]
    public async Task Timegrid_FromRecordedWorkingHoursAndBookings_HasAllThreeStates()
    {
        // Testovací rezervace u zdroje 10173 ve středu 11. 11. 14:00–15:00 (2026-11-09).
        using var harness = new Harness();
        harness.UseRecordings();
        (int serviceId, int locationId) = RecordedChoice("one-to-one");
        PublicCatalog catalog = await harness.Catalog.GetAsync(Ct);
        int segmentId = catalog.Services.Single(item => item.Id == serviceId).SegmentId;

        AvailabilityResult result = await harness.Reader.ReadAsync(
            new AvailabilityQuery(segmentId, locationId, null, BookedResource, 14) { Range = new RangeQuery(ShownRange.Week, new DateOnly(2026, 11, 11)), Timeline = true },
            Ct);

        Assert.Equal(AvailabilityOutcome.Available, result.Outcome);
        Assert.Contains(result.Bands, band => band.State == TimeBandState.Free);
        Assert.Contains(result.Bands, band => band.State == TimeBandState.Unavailable);
        TimeBand busy = Assert.Single(result.Bands, band => band.State == TimeBandState.Busy);
        Assert.Equal((14, 15), (busy.Start.Hour, busy.End.Hour));
    }

    [Fact]
    public async Task CustomerData_FromBookingsOrTheirErrors_ReachesNeitherResultNorLog()
    {
        // Rezervace se všemi poli z OpenAPI a chybová odpověď s údaji zákazníka projdou celým
        // čtením. Do výsledku pro stránku ani do logu při úrovni Trace z nich nesmí nic projít.
        string[] markers = ["Zakaznik Testovaci-Jmeno", "poznamka-zakaznika-123", "vlastni-pole-zakaznika", "platba-kartou-zakaznika", "9876543"];
        long start = new DateTimeOffset(2026, 11, 11, 14, 0, 0, TimeSpan.FromHours(1)).ToUnixTimeSeconds();
        using var harness = new Harness();
        harness.UseRecordings();
        harness.Api.Respond(ApiEndpoints.EventGetList.Path, System.Net.HttpStatusCode.OK, $$"""
            {"IsError":false,"Events":[{
              "AppointmentId": 11, "BookingServiceId": 12, "CalendarId": 13, "CourseId": null, "CustomerId": {{markers[4]}},
              "InternalSourceId": null, "LocationId": 14, "SourceId": {{BookedResource}}, "CalendarName": "Kalendar",
              "Count": 1, "CustomerFullName": "{{markers[0]}}", "CustomerNote": "{{markers[1]}}",
              "CustomValues": [{ "Id": 1, "BooleanValue": null, "DateValue": null, "Definition": { "Id": 2 }, "StringValue": "{{markers[2]}}" }],
              "EndDate": {{start + 3600}}, "InternalSourceName": null, "IsPaid": true, "LocationName": "Provozovna",
              "MerchantNote": "{{markers[1]}}", "NoShowStatus": 0, "PaidPartOfPrice": 500.0,
              "PaymentMethodFormatted": "{{markers[3]}}", "Price": 500.0, "SegmentName": "Zamereni",
              "ServiceName": "Sluzba", "SourceName": "Zdroj", "StartDate": {{start}}, "Status": "Confirmed"
            }]}
            """);
        (int serviceId, int locationId) = RecordedChoice("one-to-one");
        int segmentId = (await harness.Catalog.GetAsync(Ct)).Services.Single(item => item.Id == serviceId).SegmentId;
        var query = new AvailabilityQuery(segmentId, locationId, null, BookedResource, 14) { Range = new RangeQuery(ShownRange.Week, new DateOnly(2026, 11, 11)), Timeline = true };

        AvailabilityResult read = await harness.Reader.ReadAsync(query, Ct);

        Assert.Single(read.Bands, band => band.State == TimeBandState.Busy);
        harness.Api.Respond(ApiEndpoints.EventGetList.Path, System.Net.HttpStatusCode.OK, $$"""
            {"IsError":true,"ErrorMessage":"{{markers[0]}}","ErrorMessages":["{{markers[1]}}"],"ErrorParameters":["StartDate"],"Events":null}
            """);
        harness.Time.Advance(TimeSpan.FromMinutes(2));

        AvailabilityResult failed = await harness.Reader.ReadAsync(query, Ct);

        Assert.Equal(AvailabilityOutcome.SourceUnavailable, failed.Outcome);
        string output = string.Join('\n', [
            JsonSerializer.Serialize(read),
            JsonSerializer.Serialize(failed),
            .. harness.Logs.GetSnapshot().Select(record => $"{record.Level} {record.Message} {record.Exception}")]);
        Assert.Contains("StartDate", output, StringComparison.Ordinal); // log chyby opravdu vznikl
        Assert.All(markers, marker => Assert.DoesNotContain(marker, output, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Classes_ReturnAppointments_WithMerchantTextAndFullOnes()
    {
        using var harness = new Harness();
        harness.UseRecordings();

        AvailabilityResult result = await harness.Reader.ReadAsync(new AvailabilityQuery(ClassesSegment, ClassesLocation, null, null, 60), Ct);

        Assert.Equal(AvailabilityOutcome.Available, result.Outcome);
        Assert.All(result.Slots, slot => Assert.False(string.IsNullOrEmpty(slot.OccupancyText)));
        Assert.Contains(result.Slots, slot => slot.IsFull);
        Assert.Contains(result.Slots, slot => !slot.IsFull);
    }

    [Fact]
    public async Task DefaultView_PicksSupportedSegment_WithoutFillingService_FromRealCatalog()
    {
        using var harness = new Harness();
        harness.UseRecordings();
        harness.Api.Respond(ApiEndpoints.OneToOneGetAvailableStartsForLocation.Path, System.Net.HttpStatusCode.OK, """{"IsError":false,"Starts":{}}""");

        AvailabilityResult result = await harness.Reader.ReadAsync(new AvailabilityQuery(null, null, null, null, 14), Ct);

        Assert.Equal(AvailabilityOutcome.NothingFree, result.Outcome);
        Assert.Null(result.Selection!.Service);
    }

    [Fact]
    public async Task UnsupportedTypes_FromAccount_AreNotRead()
    {
        using var harness = new Harness();
        harness.UseRecordings();
        PublicCatalog catalog = await harness.Catalog.GetAsync(Ct);
        string[] supported = [.. harness.Sources.SelectMany(source => source.SegmentTypes)];
        PublicSegment[] unsupported = [.. catalog.Segments.Where(item => !supported.Contains(item.Type))];

        Assert.NotEmpty(unsupported);
        foreach (PublicSegment segment in unsupported)
        {
            AvailabilityResult result = await harness.Reader.ReadAsync(new AvailabilityQuery(segment.Id, null, null, null, 14), Ct);
            Assert.Equal(AvailabilityOutcome.UnsupportedSegment, result.Outcome);
        }
    }

    [Fact]
    public async Task ConnectionCheck_CountsPublicObjectsFromRecordings()
    {
        using var harness = new Harness();
        harness.UseRecordings();
        // Výchozí pohled nahraný není, ověření ho čte jen kvůli právům.
        harness.Api.Respond(ApiEndpoints.OneToOneGetAvailableStartsForLocation.Path, System.Net.HttpStatusCode.OK, """{"IsError":false,"Starts":{}}""");
        // Údaje merchanta se nenahrávají (docs/decisions.md).
        harness.Api.Respond(ApiEndpoints.MerchantGetInfo.Path, System.Net.HttpStatusCode.OK, """{"IsError":false,"Result":{"Id":1,"Name":"Merchant","IsVatPayer":false}}""");

        ConnectionCheckResult result = await harness.Check.CheckAsync(Ct);

        Assert.Equal(ConnectionState.Connected, result.State);
        Assert.Equal("Merchant", result.MerchantName);
        Assert.True(result.PublicSegments > 0);
        Assert.True(result.PublicLocations > 0);
    }

    [Fact]
    public async Task RecordedRejection_IsTokenRejected()
    {
        using var harness = new Harness();
        using var recording = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot.Path, "tests", "api-recordings", "Authorize", "GetShortTimeToken", "invalid.json")));
        string body = recording.RootElement.GetProperty("response").GetProperty("body").GetRawText();
        harness.Api.Respond(ApiEndpoints.AuthorizeGetShortTimeToken.Path, System.Net.HttpStatusCode.OK, body);

        Assert.Equal(ConnectionState.TokenRejected, (await harness.Check.CheckAsync(Ct)).State);
    }

    private const int BookedResource = 10173;
    private const int ClassesSegment = 6;
    private const int ClassesLocation = 2023;

    private static (int ServiceId, int LocationId) RecordedChoice(string scenario)
    {
        using var recording = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot.Path, "tests", "api-recordings", "OneToOne", "GetAvailableStartsForLocation", $"{scenario}.json")));
        JsonElement request = recording.RootElement.GetProperty("request");
        return (request.GetProperty("BookingServiceId").GetInt32(), request.GetProperty("LocationId").GetInt32());
    }
}
