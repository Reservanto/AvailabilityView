using System.Net;
using Reservanto.ApiClient.Core;
using Reservanto.ApiClient.Endpoints;
using Reservanto.ApiClient.Tests.Support;

namespace Reservanto.ApiClient.Tests;

/// <summary>Opakování, timeout a ochrana API při výpadku.</summary>
public sealed class RetryAndDegradationTests
{
    private const string Path = "/v1/Segment/GetList";

    [Fact]
    public async Task TransportErrorOnce_IsRetriedAndSucceeds()
    {
        using var api = new TestApi();
        int calls = 0;
        api.Api.Respond(Path, _ => Interlocked.Increment(ref calls) == 1
            ? throw new HttpRequestException("spojení spadlo")
            : Task.FromResult(FakeApi.Json(HttpStatusCode.OK, """{"IsError":false,"Items":[]}""")));

        await GetSegmentsAsync(api);

        Assert.Equal(2, api.Api.CountFor(Path));
    }

    [Fact]
    public async Task TransportErrorTwice_FailsAsUnavailable_AfterOneRetry()
    {
        using var api = new TestApi();
        api.Api.Respond(Path, _ => throw new HttpRequestException("spojení spadlo"));

        await Assert.ThrowsAsync<ReservantoApiUnavailableException>(() => GetSegmentsAsync(api));

        Assert.Equal(2, api.Api.CountFor(Path));
    }

    [Fact]
    public async Task Timeout_IsUnavailable_AndIsRetriedOnce()
    {
        using var api = new TestApi(timeout: TimeSpan.FromMilliseconds(200));
        api.Api.Respond(Path, async (_, requestAborted) =>
        {
            await Task.Delay(Timeout.Infinite, requestAborted);
            throw new InvalidOperationException("nedosažitelné");
        });

        ReservantoApiUnavailableException error = await Assert.ThrowsAsync<ReservantoApiUnavailableException>(() => GetSegmentsAsync(api));

        Assert.Contains("časový limit", error.Message, StringComparison.Ordinal);
        Assert.Equal(2, api.Api.CountFor(Path));
    }

    [Fact]
    public async Task CallerCancellation_PropagatesAsCancellation_NotAsUnavailable()
    {
        using var api = new TestApi();
        using var cancellation = new CancellationTokenSource();
        api.Api.Respond(Path, async _ =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(Timeout.Infinite, cancellation.Token);
            throw new InvalidOperationException("nedosažitelné");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => api.Client.Segment.GetListAsync(new ObjectStateListRequest(), cancellation.Token));

        Assert.Equal(1, api.Api.CountFor(Path));
    }

    [Fact]
    public void DefaultTimeout_IsTenSeconds()
    {
        using var api = new TestApi();

        Assert.Equal(TimeSpan.FromSeconds(10), api.Options.Timeout);
    }

    [Fact]
    public async Task Degradation_AfterFiveFailuresIn30Seconds_FailsFastWithoutCallingApi()
    {
        using var api = new TestApi();
        api.Api.Respond(Path, _ => throw new HttpRequestException("spojení spadlo"));

        // Selhání se počítá za volání, ne za pokus: páté neúspěšné volání dosáhne prahu.
        for (int call = 0; call < 5; call++)
        {
            await Assert.ThrowsAsync<ReservantoApiUnavailableException>(() => GetSegmentsAsync(api));
        }

        Assert.True(api.Degradation.IsDegraded);
        int requestsBefore = api.Api.CountFor(Path);
        ReservantoApiUnavailableException error = await Assert.ThrowsAsync<ReservantoApiUnavailableException>(() => GetSegmentsAsync(api));
        Assert.Contains("degraded", error.Message, StringComparison.Ordinal);
        Assert.Equal(requestsBefore, api.Api.CountFor(Path));
    }

    [Fact]
    public async Task Degradation_FourFailedCalls_AreNotEnough()
    {
        using var api = new TestApi();
        api.Api.Respond(Path, _ => throw new HttpRequestException("spojení spadlo"));

        for (int call = 0; call < 4; call++)
        {
            await Assert.ThrowsAsync<ReservantoApiUnavailableException>(() => GetSegmentsAsync(api));
        }

        Assert.False(api.Degradation.IsDegraded);
    }

    [Fact]
    public async Task Degradation_EndsAfter30Seconds()
    {
        using var api = new TestApi();
        DegradeNow(api);
        await Assert.ThrowsAsync<ReservantoApiUnavailableException>(() => GetSegmentsAsync(api));

        api.Time.Advance(TimeSpan.FromSeconds(30));
        await GetSegmentsAsync(api);

        Assert.Equal(1, api.Api.CountFor(Path));
    }

    [Fact]
    public void Degradation_FailuresOutsideWindow_DoNotAddUp()
    {
        using var api = new TestApi();
        for (int failure = 0; failure < 4; failure++)
        {
            api.Degradation.RecordFailure();
        }

        api.Time.Advance(TimeSpan.FromSeconds(31));
        api.Degradation.RecordFailure();

        Assert.False(api.Degradation.IsDegraded);
    }

    [Fact]
    public void Degradation_SuccessBreaksTheSeries()
    {
        using var api = new TestApi();
        for (int failure = 0; failure < 4; failure++)
        {
            api.Degradation.RecordFailure();
        }

        api.Degradation.RecordSuccess();
        api.Degradation.RecordFailure();

        Assert.False(api.Degradation.IsDegraded);
    }

    [Fact]
    public async Task ServerError_CountsTowardDegradation_ButApiErrorResponseDoesNot()
    {
        using var api = new TestApi();
        api.Api.Respond(Path, HttpStatusCode.ServiceUnavailable, string.Empty);
        for (int call = 0; call < 5; call++)
        {
            await Assert.ThrowsAsync<ReservantoApiException>(() => GetSegmentsAsync(api));
        }

        Assert.True(api.Degradation.IsDegraded);

        using var other = new TestApi();
        other.Api.Respond(Path, HttpStatusCode.OK, """{"IsError":true,"ErrorMessage":"x"}""");
        for (int call = 0; call < 10; call++)
        {
            await Assert.ThrowsAsync<ReservantoApiException>(() => GetSegmentsAsync(other));
        }

        Assert.False(other.Degradation.IsDegraded);
    }

    [Fact]
    public async Task NotRetried_WhenApiBecameDegradedDuringFirstAttempt()
    {
        // Souběžná volání mohou API přepnout do degradace během prvního pokusu, opakování už API volat nesmí.
        using var api = new TestApi();
        api.Api.Respond(Path, _ =>
        {
            DegradeNow(api);
            throw new HttpRequestException("spojení spadlo");
        });

        await Assert.ThrowsAsync<ReservantoApiUnavailableException>(() => GetSegmentsAsync(api));

        Assert.Equal(1, api.Api.CountFor(Path));
    }

    private static void DegradeNow(TestApi api)
    {
        for (int failure = 0; failure < api.Options.Degradation.FailureThreshold; failure++)
        {
            api.Degradation.RecordFailure();
        }
    }

    private static Task<IReadOnlyList<Models.SegmentApiModel>> GetSegmentsAsync(TestApi api) =>
        api.Client.Segment.GetListAsync(new ObjectStateListRequest(), CancellationToken.None);
}
