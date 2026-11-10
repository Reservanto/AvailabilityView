using System.Net;
using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Connection;
using AvailabilityView.Infrastructure.Tests.Support;
using Reservanto.ApiClient.Core;

namespace AvailabilityView.Infrastructure.Tests;

/// <summary>Neveřejná data se do katalogu nedostanou, ani když je API vrátí.</summary>
public sealed class PublicCatalogTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PrivateObjects_AreLeftOut_EvenWhenApiReturnsThem()
    {
        using var harness = new Harness();
        RespondWithMixedCatalog(harness);

        PublicCatalog catalog = await harness.Catalog.GetAsync(Ct);

        Assert.Equal([1], catalog.Segments.Select(item => item.Id));
        Assert.Equal([10], catalog.Locations.Select(item => item.Id));
        Assert.Equal([100], catalog.Resources.Select(item => item.Id));
        Assert.Equal([1000], catalog.Services.Select(item => item.Id));
    }

    [Fact]
    public async Task Links_ToPrivateObjects_AreRemoved()
    {
        using var harness = new Harness();
        RespondWithMixedCatalog(harness);

        PublicCatalog catalog = await harness.Catalog.GetAsync(Ct);

        Assert.Equal([100], Assert.Single(catalog.Services).ResourceIds);
        Assert.Equal([1], Assert.Single(catalog.Resources).SegmentIds);
    }

    [Fact]
    public async Task EveryList_AsksForPublicObjectsOnly()
    {
        using var harness = new Harness();
        RespondWithMixedCatalog(harness);

        await harness.Catalog.GetAsync(Ct);

        string[] lists = [ApiEndpoints.SegmentGetList.Path, ApiEndpoints.LocationGetList.Path, ApiEndpoints.BookingServiceGetList.Path, ApiEndpoints.BookingResourceGetList.Path];
        Assert.All(lists, path => Assert.True((bool)harness.Api.Received.Single(request => request.Path == path).Json["OnlyPublic"]!));
    }

    [Fact]
    public async Task SegmentName_PrefersMerchantsName_AndNeverUsesInternalName()
    {
        using var harness = new Harness();
        RespondWithMixedCatalog(harness);

        Assert.Equal("Masáže", Assert.Single((await harness.Catalog.GetAsync(Ct)).Segments).Name);
    }

    [Fact]
    public async Task SegmentWithoutMerchantsName_UsesLocalizedName()
    {
        using var harness = new Harness();
        RespondWithMixedCatalog(harness);
        harness.Api.Respond(ApiEndpoints.SegmentGetList.Path, HttpStatusCode.OK,
            """{"IsError":false,"Items":[{"Id":1,"CustomizedName":"","LocalizedName":"Terapie","InternalName":"interni","SegmentType":"OneToOne","State":"Public"}]}""");

        Assert.Equal("Terapie", Assert.Single((await harness.Catalog.GetAsync(Ct)).Segments).Name);
    }

    [Fact]
    public async Task DuplicateItems_FromApi_DoNotBreakReading()
    {
        // Mezi stránkami se může položka posunout a přijít dvakrát.
        using var harness = new Harness();
        RespondWithMixedCatalog(harness);
        harness.Api.Respond(ApiEndpoints.BookingResourceGetList.Path, HttpStatusCode.OK, """
            {"IsError":false,"Items":[
              {"Id":100,"Name":"Jana","LocationId":10,"SegmentIds":[1],"State":"Public"},
              {"Id":100,"Name":"Jana","LocationId":10,"SegmentIds":[1],"State":"Public"}
            ]}
            """);
        long start = harness.Time.GetUtcNow().AddDays(1).ToUnixTimeSeconds();
        harness.Api.Respond(ApiEndpoints.OneToOneGetAvailableStartsForLocation.Path, HttpStatusCode.OK, """{"IsError":false,"Starts":{"100":[""" + start + "]}}");

        AvailabilityResult result = await harness.Reader.ReadAsync(new AvailabilityQuery(1, null, null, null, 14), Ct);

        Assert.Single(result.Slots);
        Assert.Single((await harness.Catalog.GetAsync(Ct)).Resources);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, ConnectionState.MissingRights)]
    [InlineData(HttpStatusCode.Unauthorized, ConnectionState.TokenRejected)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ConnectionState.Unavailable)]
    public async Task ApiFailure_BecomesReason(HttpStatusCode status, ConnectionState reason)
    {
        using var harness = new Harness();
        harness.Api.Respond(ApiEndpoints.SegmentGetList.Path, status, string.Empty);

        Assert.Equal(reason, (await harness.Check.CheckAsync(Ct)).State);
    }

    [Fact]
    public async Task PrivateResourceInClasses_LeavesOutWholeAppointment()
    {
        using var harness = new Harness();
        RespondWithMixedCatalog(harness);
        harness.Api.Respond(ApiEndpoints.SegmentGetList.Path, HttpStatusCode.OK, """{"IsError":false,"Items":[{"Id":1,"LocalizedName":"Lekce","SegmentType":"Classes","State":"Public"}]}""");
        long start = harness.Time.GetUtcNow().AddDays(1).ToUnixTimeSeconds();
        harness.Api.Respond(ApiEndpoints.ClassesGetAvailableAppointments.Path, HttpStatusCode.OK, $$"""
            {"IsError":false,"Items":[
              {"Id":1,"BookingResourceId":101,"BookingServiceId":1000,"StartDate":{{start}},"EndDate":{{start + 3600}},"IsAvailable":true,"BookingResourceName":"Neverejny"},
              {"Id":2,"BookingResourceId":100,"BookingServiceId":1001,"StartDate":{{start}},"EndDate":{{start + 3600}},"IsAvailable":true},
              {"Id":3,"BookingResourceId":100,"BookingServiceId":1000,"StartDate":{{start}},"EndDate":{{start + 3600}},"IsAvailable":true,"FormattedAvailability":"volno"}
            ]}
            """);

        AvailabilityResult result = await harness.Reader.ReadAsync(new AvailabilityQuery(1, null, null, null, 14), Ct);

        AvailabilitySlot slot = Assert.Single(result.Slots);
        Assert.Equal("volno", slot.OccupancyText);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ClassesAppointment_KeepsEndAndFullness(bool isAvailable, bool isFull)
    {
        using var harness = new Harness();
        RespondWithMixedCatalog(harness);
        harness.Api.Respond(ApiEndpoints.SegmentGetList.Path, HttpStatusCode.OK, """{"IsError":false,"Items":[{"Id":1,"LocalizedName":"Lekce","SegmentType":"Classes","State":"Public"}]}""");
        DateTimeOffset start = harness.Time.GetUtcNow().AddDays(1);
        string available = isAvailable ? "true" : "false";
        harness.Api.Respond(ApiEndpoints.ClassesGetAvailableAppointments.Path, HttpStatusCode.OK,
            """{"IsError":false,"Items":[{"Id":3,"BookingResourceId":100,"BookingServiceId":1000,"StartDate":"""
            + start.ToUnixTimeSeconds() + ""","EndDate":""" + start.AddMinutes(45).ToUnixTimeSeconds() + ""","IsAvailable":""" + available + "}]}");

        AvailabilitySlot slot = Assert.Single((await harness.Reader.ReadAsync(new AvailabilityQuery(1, null, null, null, 14), Ct)).Slots);

        Assert.Equal(isFull, slot.IsFull);
        Assert.Equal(TimeSpan.FromMinutes(45), slot.End - slot.Start);
    }

    private static void RespondWithMixedCatalog(Harness harness)
    {
        harness.Api.Respond(ApiEndpoints.SegmentGetList.Path, HttpStatusCode.OK, """
            {"IsError":false,"Items":[
              {"Id":1,"CustomizedName":"Masáže","LocalizedName":"Terapie","InternalName":"interni","SegmentType":"OneToOne","State":"Public"},
              {"Id":2,"CustomizedName":"Tajné","SegmentType":"OneToOne","State":"Private"}
            ]}
            """);
        harness.Api.Respond(ApiEndpoints.LocationGetList.Path, HttpStatusCode.OK, """
            {"IsError":false,"Items":[
              {"Id":10,"Name":"Praha","State":"Public","CreatedAt":1,"ModifiedAt":1},
              {"Id":20,"Name":"Tajná","State":"Private","CreatedAt":1,"ModifiedAt":1},
              {"Id":30,"Name":"Smazaná","State":"Deleted","CreatedAt":1,"ModifiedAt":1}
            ]}
            """);
        harness.Api.Respond(ApiEndpoints.BookingServiceGetList.Path, HttpStatusCode.OK, """
            {"IsError":false,"BookingServices":[
              {"Id":1000,"Name":"Klasická","SegmentId":1,"Duration":60,"BookingResourceIds":[100,101],"State":"Public"},
              {"Id":1001,"Name":"Tajná","SegmentId":1,"Duration":60,"BookingResourceIds":[100],"State":"Private"},
              {"Id":1002,"Name":"Z tajného zaměření","SegmentId":2,"Duration":60,"BookingResourceIds":[100],"State":"Public"}
            ]}
            """);
        harness.Api.Respond(ApiEndpoints.BookingResourceGetList.Path, HttpStatusCode.OK, """
            {"IsError":false,"Items":[
              {"Id":100,"Name":"Jana","LocationId":10,"SegmentIds":[1,2],"State":"Public"},
              {"Id":101,"Name":"Tajný","LocationId":10,"SegmentIds":[1],"State":"Private"},
              {"Id":102,"Name":"Z tajné provozovny","LocationId":20,"SegmentIds":[1],"State":"Public"}
            ]}
            """);
    }
}
