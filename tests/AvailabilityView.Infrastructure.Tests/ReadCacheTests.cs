using System.Net;
using AvailabilityView.Application.Availability;
using AvailabilityView.Infrastructure.Availability;
using AvailabilityView.Infrastructure.Tests.Support;
using Reservanto.ApiClient.Core;

namespace AvailabilityView.Infrastructure.Tests;

/// <summary>Cache podle <c>docs/SPEC.md</c> sekce 9.</summary>
public sealed class ReadCacheTests
{
    private static readonly string _startsPath = ApiEndpoints.OneToOneGetAvailableStartsForLocation.Path;
    private static readonly string _segmentsPath = ApiEndpoints.SegmentGetList.Path;
    private static readonly AvailabilityQuery _query = new(1, null, null, null, 14);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FreeSlots_AreKeptSixtySeconds()
    {
        using Harness harness = WithCatalog();

        await harness.Reader.ReadAsync(_query, Ct);
        harness.Time.Advance(TimeSpan.FromSeconds(59));
        await harness.Reader.ReadAsync(_query, Ct);
        Assert.Equal(1, harness.Api.CountFor(_startsPath));

        harness.Time.Advance(TimeSpan.FromSeconds(1));
        await harness.Reader.ReadAsync(_query, Ct);
        Assert.Equal(2, harness.Api.CountFor(_startsPath));
    }

    [Fact]
    public async Task Catalog_IsKeptOneHour()
    {
        using Harness harness = WithCatalog();

        await harness.Catalog.GetAsync(Ct);
        harness.Time.Advance(TimeSpan.FromMinutes(59));
        await harness.Catalog.GetAsync(Ct);
        Assert.Equal(1, harness.Api.CountFor(_segmentsPath));

        harness.Time.Advance(TimeSpan.FromMinutes(1));
        await harness.Catalog.GetAsync(Ct);
        Assert.Equal(2, harness.Api.CountFor(_segmentsPath));
    }

    [Fact]
    public async Task DifferentDays_ShareOneEntry()
    {
        using Harness harness = WithCatalog();

        await harness.Reader.ReadAsync(_query with { Days = 7 }, Ct);
        await harness.Reader.ReadAsync(_query with { Days = 30 }, Ct);

        Assert.Equal(1, harness.Api.CountFor(_startsPath));
    }

    [Fact]
    public async Task DifferentService_OrLocation_HasOwnEntry()
    {
        using Harness harness = WithCatalog();
        harness.Api.Respond(ApiEndpoints.LocationGetList.Path, HttpStatusCode.OK,
            """{"IsError":false,"Items":[{"Id":10,"Name":"Praha","State":"Public","CreatedAt":1,"ModifiedAt":1},{"Id":20,"Name":"Brno","State":"Public","CreatedAt":1,"ModifiedAt":1}]}""");
        harness.Api.Respond(ApiEndpoints.BookingServiceGetList.Path, HttpStatusCode.OK,
            """{"IsError":false,"BookingServices":[{"Id":1000,"Name":"Klasická","SegmentId":1,"Duration":60,"BookingResourceIds":[100,200],"State":"Public"},{"Id":1001,"Name":"Sportovní","SegmentId":1,"Duration":30,"BookingResourceIds":[100,200],"State":"Public"}]}""");
        harness.Api.Respond(ApiEndpoints.BookingResourceGetList.Path, HttpStatusCode.OK,
            """{"IsError":false,"Items":[{"Id":100,"Name":"Jana","LocationId":10,"SegmentIds":[1],"State":"Public"},{"Id":200,"Name":"Eva","LocationId":20,"SegmentIds":[1],"State":"Public"}]}""");

        await harness.Reader.ReadAsync(new AvailabilityQuery(1, 10, 1000, null, 14), Ct);
        await harness.Reader.ReadAsync(new AvailabilityQuery(1, 10, 1001, null, 14), Ct);
        await harness.Reader.ReadAsync(new AvailabilityQuery(1, 20, 1000, null, 14), Ct);

        Assert.Equal(3, harness.Api.CountFor(_startsPath));
    }

    [Fact]
    public async Task SameQueries_InParallel_CallApiOnce()
    {
        using Harness harness = WithCatalog();
        await harness.Catalog.GetAsync(Ct);
        var release = new TaskCompletionSource();
        harness.Api.Respond(_startsPath, async _ =>
        {
            await release.Task;
            return FakeApi();
        });

        Task<AvailabilityResult>[] reads = [.. Enumerable.Range(0, 20).Select(_ => harness.Reader.ReadAsync(_query, Ct))];
        await WaitUntilAsync(() => harness.Api.CountFor(_startsPath) > 0);
        await Task.Delay(50, Ct);
        release.SetResult();
        await Task.WhenAll(reads);

        Assert.Equal(1, harness.Api.CountFor(_startsPath));
    }

    [Fact]
    public async Task CancelledCaller_DoesNotBreakOthersWaiting()
    {
        using Harness harness = WithCatalog();
        await harness.Catalog.GetAsync(Ct);
        var release = new TaskCompletionSource();
        harness.Api.Respond(_startsPath, async _ =>
        {
            await release.Task;
            return FakeApi();
        });
        using var cancelled = new CancellationTokenSource();

        Task<AvailabilityResult> first = harness.Reader.ReadAsync(_query, cancelled.Token);
        await WaitUntilAsync(() => harness.Api.CountFor(_startsPath) > 0);
        Task<AvailabilityResult> second = harness.Reader.ReadAsync(_query, Ct);
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        release.SetResult();

        Assert.Equal(AvailabilityOutcome.Available, (await second).Outcome);
    }

    [Fact]
    public async Task Failure_IsNotStored()
    {
        using Harness harness = WithCatalog();
        harness.Api.Respond(_startsPath, HttpStatusCode.InternalServerError, string.Empty);
        Assert.Equal(AvailabilityOutcome.SourceUnavailable, (await harness.Reader.ReadAsync(_query, Ct)).Outcome);

        RespondWithStarts(harness);

        Assert.Equal(AvailabilityOutcome.Available, (await harness.Reader.ReadAsync(_query, Ct)).Outcome);
    }

    [Fact]
    public async Task Outage_AfterExpiry_DoesNotReturnOldData()
    {
        using Harness harness = WithCatalog();
        await harness.Reader.ReadAsync(_query, Ct);

        harness.Api.Respond(_startsPath, HttpStatusCode.InternalServerError, string.Empty);
        harness.Time.Advance(TimeSpan.FromSeconds(60));
        AvailabilityResult result = await harness.Reader.ReadAsync(_query, Ct);

        Assert.Equal(AvailabilityOutcome.SourceUnavailable, result.Outcome);
        Assert.Empty(result.Slots);
    }

    [Fact]
    public async Task ChangedToken_DropsEverything()
    {
        using Harness harness = WithCatalog();
        await harness.Reader.ReadAsync(_query, Ct);

        harness.ChangeLongTimeToken("ltt-druheho-uctu");
        await harness.Reader.ReadAsync(_query, Ct);

        Assert.Equal(2, harness.Api.CountFor(_segmentsPath));
        Assert.Equal(2, harness.Api.CountFor(_startsPath));
    }

    [Fact]
    public async Task ResultLoadedWithOldToken_IsNotKeptForNewToken()
    {
        using Harness harness = WithCatalog();
        var release = new TaskCompletionSource();
        harness.Api.Respond(_segmentsPath, async _ =>
        {
            await release.Task;
            return Reservanto.ApiClient.Tests.Support.FakeApi.Json(HttpStatusCode.OK, SegmentsBody);
        });

        Task<PublicCatalog> old = harness.Catalog.GetAsync(Ct);
        await WaitUntilAsync(() => harness.Api.CountFor(_segmentsPath) > 0);
        harness.ChangeLongTimeToken("ltt-druheho-uctu");
        release.SetResult();
        await old;
        await harness.Catalog.GetAsync(Ct);

        Assert.Equal(2, harness.Api.CountFor(_segmentsPath));
    }

    [Fact]
    public async Task Size_IsLimited_AndOldEntriesMakeRoom()
    {
        using var harness = new Harness();
        ReadCache cache = harness.Get<ReadCache>();
        for (int key = 0; key < ReadCache.MaxEntries; key++)
        {
            await cache.GetAsync($"plna-{key}", TimeSpan.FromMinutes(1), () => Task.FromResult("hodnota"), Ct);
        }

        int loads = 0;
        Task<string> Load()
        {
            loads++;
            return Task.FromResult("hodnota");
        }

        await cache.GetAsync("navic", TimeSpan.FromMinutes(1), Load, Ct);
        await cache.GetAsync("navic", TimeSpan.FromMinutes(1), Load, Ct);
        Assert.Equal(2, loads);

        harness.Time.Advance(TimeSpan.FromMinutes(1));
        await cache.GetAsync("po-vyprseni", TimeSpan.FromMinutes(1), Load, Ct);
        await cache.GetAsync("po-vyprseni", TimeSpan.FromMinutes(1), Load, Ct);
        Assert.Equal(3, loads);
    }

    private const string SegmentsBody = """{"IsError":false,"Items":[{"Id":1,"LocalizedName":"Masáže","SegmentType":"OneToOne","State":"Public"}]}""";

    private static Harness WithCatalog()
    {
        var harness = new Harness();
        harness.Api.Respond(_segmentsPath, HttpStatusCode.OK, SegmentsBody);
        harness.Api.Respond(ApiEndpoints.LocationGetList.Path, HttpStatusCode.OK,
            """{"IsError":false,"Items":[{"Id":10,"Name":"Praha","State":"Public","CreatedAt":1,"ModifiedAt":1}]}""");
        harness.Api.Respond(ApiEndpoints.BookingServiceGetList.Path, HttpStatusCode.OK,
            """{"IsError":false,"BookingServices":[{"Id":1000,"Name":"Klasická","SegmentId":1,"Duration":60,"BookingResourceIds":[100],"State":"Public"}]}""");
        harness.Api.Respond(ApiEndpoints.BookingResourceGetList.Path, HttpStatusCode.OK,
            """{"IsError":false,"Items":[{"Id":100,"Name":"Jana","LocationId":10,"SegmentIds":[1],"State":"Public"}]}""");
        RespondWithStarts(harness);
        return harness;
    }

    private static void RespondWithStarts(Harness harness)
    {
        long start = harness.Time.GetUtcNow().AddDays(1).ToUnixTimeSeconds();
        harness.Api.Respond(_startsPath, HttpStatusCode.OK, StartsBody(start));
    }

    private static HttpResponseMessage FakeApi() =>
        Reservanto.ApiClient.Tests.Support.FakeApi.Json(
            HttpStatusCode.OK,
            StartsBody(new DateTimeOffset(2026, 11, 7, 8, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds()));

    private static string StartsBody(long start) => """{"IsError":false,"Starts":{"100":[""" + start + "]}}";

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 500 && !condition(); attempt++)
        {
            await Task.Delay(10, Ct);
        }

        Assert.True(condition());
    }
}
