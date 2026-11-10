using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reservanto.ApiClient.Core;
using Reservanto.ApiClient.Endpoints;
using Reservanto.ApiClient.Models;
using Reservanto.ApiClient.Tests.Support;

namespace Reservanto.ApiClient.Tests;

/// <summary>
/// Tvar dat na drátě: <c>TimeStamp</c> doplňuje klient, časy jsou unix sekundy UTC, výčty jako text, názvy polí
/// 1:1 s OpenAPI.
/// </summary>
public sealed class WireFormatTests
{
    [Fact]
    public async Task Request_GetsTimeStampFromClock_AsUnixSeconds()
    {
        using var api = new TestApi();

        await api.Client.Segment.GetListAsync(new ObjectStateListRequest { OnlyPublic = true }, CancellationToken.None);

        JObject body = api.Api.Received[^1].Json;
        Assert.Equal(api.Time.GetUtcNow().ToUnixTimeSeconds(), (long)body["TimeStamp"]!);
        Assert.True((bool)body["OnlyPublic"]!);
    }

    [Fact]
    public async Task Request_SerializesTimesAndNames_AsInOpenApi()
    {
        using var api = new TestApi();
        api.Api.Respond("/v1/OneToOne/GetAvailableStartsForLocation", System.Net.HttpStatusCode.OK, """{"IsError":false,"Starts":{}}""");
        var start = new DateTimeOffset(2026, 10, 5, 9, 30, 0, TimeSpan.FromHours(2));

        await api.Client.OneToOne.GetAvailableStartsForLocationAsync(
            new AvailableStartsForLocationRequest { LocationId = 3, BookingServiceId = 4, IntervalStart = start, IntervalEnd = start.AddDays(1) },
            CancellationToken.None);

        JObject body = api.Api.Received[^1].Json;
        Assert.Equal(1791185400L, (long)body["IntervalStart"]!); // 2026-10-05 07:30 UTC
        Assert.Equal(
            ["BookingServiceId", "IntervalEnd", "IntervalStart", "LocationId", "TimeStamp"],
            body.Properties().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task Response_UnixTimes_AreReadAsUtc()
    {
        using var api = new TestApi();
        api.Api.Respond("/v1/OneToOne/GetAvailableStartsForLocation", System.Net.HttpStatusCode.OK, """{"IsError":false,"Starts":{"1":[1747924200]}}""");

        IReadOnlyDictionary<int, IReadOnlyList<DateTimeOffset>> starts = await api.Client.OneToOne.GetAvailableStartsForLocationAsync(
            new AvailableStartsForLocationRequest(), CancellationToken.None);

        // 1747924200 = 2025-05-22 14:30:00 UTC.
        Assert.Equal(new DateTimeOffset(2025, 5, 22, 14, 30, 0, TimeSpan.Zero), Assert.Single(starts[1]));
        Assert.Equal(TimeSpan.Zero, starts[1][0].Offset);
    }

    [Fact]
    public async Task Response_DictionaryKeyedByResourceId_IsRead()
    {
        using var api = new TestApi();
        api.Api.Respond("/v1/OneToOne/GetAvailableStartsForLocation", System.Net.HttpStatusCode.OK,
            """{"IsError":false,"Starts":{"101":[1747924200],"102":[1747924200,1747926000]}}""");

        IReadOnlyDictionary<int, IReadOnlyList<DateTimeOffset>> starts = await api.Client.OneToOne.GetAvailableStartsForLocationAsync(
            new AvailableStartsForLocationRequest(), CancellationToken.None);

        Assert.Single(starts[101]);
        Assert.Equal(2, starts[102].Count);
    }

    [Fact]
    public async Task Response_NullStartsForResource_AreEmpty()
    {
        // OpenAPI hodnotu null u zdroje dovoluje, volající má dostat prázdný seznam.
        using var api = new TestApi();
        api.Api.Respond("/v1/OneToOne/GetAvailableStartsForLocation", System.Net.HttpStatusCode.OK, """{"IsError":false,"Starts":{"101":null}}""");

        IReadOnlyDictionary<int, IReadOnlyList<DateTimeOffset>> starts = await api.Client.OneToOne.GetAvailableStartsForLocationAsync(
            new AvailableStartsForLocationRequest(), CancellationToken.None);

        Assert.Empty(starts[101]);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"2026-10-02\"")]
    [InlineData("1.5")]
    public void UnixTime_InvalidValueForRequiredTime_Throws(string value)
    {
        Assert.ThrowsAny<JsonException>(() =>
            JsonConvert.DeserializeObject<WorkingHourApiModel>($$"""{"DayOfWeek":1,"Start":{{value}},"End":1}""", ApiJson.Settings));
    }

    [Fact]
    public void UnixTime_NullForOptionalTime_IsNull()
    {
        BookingResourceApiModel resource = JsonConvert.DeserializeObject<BookingResourceApiModel>(
            """{"Id":1,"LocationId":1,"Capacity":1,"MinTimeUnit":15,"HasCustomImage":false,"State":"Public","AvailableFrom":null}""",
            ApiJson.Settings)!;

        Assert.Null(resource.AvailableFrom);
    }

    [Fact]
    public void Enum_AsNumber_IsRejected()
    {
        // API posílá výčty jako text. Číslo by znamenalo změnu kontraktu, ne hodnotu k tichému převodu.
        Assert.ThrowsAny<JsonException>(() =>
            JsonConvert.DeserializeObject<SegmentApiModel>("""{"Id":1,"State":1}""", ApiJson.Settings));
    }
}
