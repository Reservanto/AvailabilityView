using System.Net;
using Reservanto.ApiClient.Core;
using Reservanto.ApiClient.Endpoints;
using Reservanto.ApiClient.Tests.Support;

namespace Reservanto.ApiClient.Tests;

/// <summary>Odmítnutý token se liší od výpadku a výměna tokenu za běhu (<c>docs/SPEC.md</c> sekce 6 krok 4).</summary>
public sealed class LongTimeTokenTests
{
    private const string NewToken = "ltt-novy-testu";
    private static readonly string _tokenPath = ApiEndpoints.AuthorizeGetShortTimeToken.Path;
    private static readonly string _segmentPath = ApiEndpoints.SegmentGetList.Path;

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task AuthorizationStatusOnToken_IsRejection(HttpStatusCode status)
    {
        using var api = new TestApi();
        api.Api.Respond(_tokenPath, status, string.Empty);

        await Assert.ThrowsAsync<ReservantoTokenRejectedException>(() => GetSegmentsAsync(api));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task OtherStatus_IsNotRejection(HttpStatusCode status)
    {
        using var api = new TestApi();
        api.Api.Respond(_tokenPath, status, string.Empty);

        await Assert.ThrowsAsync<ReservantoApiUnavailableException>(() => GetSegmentsAsync(api));
    }

    [Fact]
    public async Task ErrorAboutOtherParameter_IsNotRejection()
    {
        // Odmítnutí tokenu nese ErrorParameter LongTimeToken (nahrávka Authorize/GetShortTimeToken/invalid).
        using var api = new TestApi();
        api.Api.Respond(_tokenPath, HttpStatusCode.OK, """{"IsError":true,"ErrorParameter":"TimeStamp","ErrorParameters":["TimeStamp"]}""");

        await Assert.ThrowsAsync<ReservantoApiUnavailableException>(() => GetSegmentsAsync(api));
    }

    [Fact]
    public async Task ConnectionFailure_IsNotRejection()
    {
        using var api = new TestApi();
        api.Api.Respond(_tokenPath, _ => throw new HttpRequestException("spojení"));

        await Assert.ThrowsAsync<ReservantoApiUnavailableException>(() => GetSegmentsAsync(api));
    }

    [Fact]
    public async Task Rejection_IsNotRetried_AndDoesNotCountAsOutage()
    {
        using var api = new TestApi();
        Reject(api);

        for (int call = 0; call < api.Options.Degradation.FailureThreshold + 1; call++)
        {
            await Assert.ThrowsAsync<ReservantoTokenRejectedException>(() => GetSegmentsAsync(api));
            api.Time.Advance(TimeSpan.FromMinutes(6));
        }

        Assert.Equal(api.Options.Degradation.FailureThreshold + 1, api.TokenRequests);
        Assert.False(api.Degradation.IsDegraded);
    }

    [Fact]
    public async Task Backoff_KeepsCauseOfUnavailability()
    {
        using var api = new TestApi();
        api.Api.Respond(_tokenPath, HttpStatusCode.ServiceUnavailable, string.Empty);

        await Assert.ThrowsAsync<ReservantoApiUnavailableException>(() => GetSegmentsAsync(api));
        int requests = api.TokenRequests;
        await Assert.ThrowsAsync<ReservantoApiUnavailableException>(() => GetSegmentsAsync(api));

        Assert.Equal(requests, api.TokenRequests);
    }

    [Fact]
    public async Task ChangedToken_DropsShortTimeToken_AndUsesNewOne()
    {
        using var api = new TestApi();
        await GetSegmentsAsync(api);

        api.ChangeLongTimeToken(NewToken);
        await GetSegmentsAsync(api);

        Assert.Equal(2, api.TokenRequests);
        Assert.Contains(NewToken, api.Api.Received.Last(request => request.Path == _tokenPath).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnchangedToken_AfterConfigurationReload_KeepsShortTimeToken()
    {
        using var api = new TestApi();
        await GetSegmentsAsync(api);

        api.ChangeLongTimeToken(TestApi.LongTimeToken);
        await GetSegmentsAsync(api);

        Assert.Equal(1, api.TokenRequests);
    }

    [Fact]
    public async Task ChangedToken_ForgetsBackoff_OfPreviousToken()
    {
        using var api = new TestApi();
        Reject(api);
        await Assert.ThrowsAsync<ReservantoTokenRejectedException>(() => GetSegmentsAsync(api));

        api.Api.Respond(_tokenPath, HttpStatusCode.OK, """{"IsError":false,"ShortTimeToken":"stt-novy"}""");
        api.ChangeLongTimeToken(NewToken);
        await GetSegmentsAsync(api);

        Assert.Equal("stt-novy", api.Api.Received.Last(request => request.Path == _segmentPath).Authorization);
        Assert.Equal(0, api.Tokens.ConsecutiveFailures);
    }

    [Fact]
    public async Task UnchangedToken_KeepsBackoff()
    {
        using var api = new TestApi();
        Reject(api);
        await Assert.ThrowsAsync<ReservantoTokenRejectedException>(() => GetSegmentsAsync(api));
        int requests = api.TokenRequests;

        api.ChangeLongTimeToken(TestApi.LongTimeToken);
        await Assert.ThrowsAsync<ReservantoTokenRejectedException>(() => GetSegmentsAsync(api));

        Assert.Equal(requests, api.TokenRequests);
    }

    [Fact]
    public async Task RemovedToken_StopsUsingShortTimeToken()
    {
        using var api = new TestApi();
        await GetSegmentsAsync(api);
        int segmentCalls = api.Api.CountFor(_segmentPath);

        api.ChangeLongTimeToken(null);

        await Assert.ThrowsAsync<ReservantoApiUnavailableException>(() => GetSegmentsAsync(api));
        Assert.Equal(segmentCalls, api.Api.CountFor(_segmentPath));
    }

    [Fact]
    public async Task TokenReplacedWhileFetching_RequestAlreadyUsesNewToken()
    {
        using var api = new TestApi();
        int calls = 0;
        api.Api.Respond(_tokenPath, _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                api.ChangeLongTimeToken(NewToken);
                return Task.FromResult(FakeApi.Json(HttpStatusCode.OK, """{"IsError":false,"ShortTimeToken":"stt-stary"}"""));
            }

            return Task.FromResult(FakeApi.Json(HttpStatusCode.OK, """{"IsError":false,"ShortTimeToken":"stt-novy"}"""));
        });

        await GetSegmentsAsync(api);

        Assert.Equal("stt-novy", api.Api.Received.Single(request => request.Path == _segmentPath).Authorization);
    }

    [Fact]
    public async Task TokenRemovedWhileFetching_RequestDoesNotUseOldToken()
    {
        using var api = new TestApi();
        api.Api.Respond(_tokenPath, _ =>
        {
            api.ChangeLongTimeToken(null);
            return Task.FromResult(FakeApi.Json(HttpStatusCode.OK, """{"IsError":false,"ShortTimeToken":"stt-stary"}"""));
        });

        await Assert.ThrowsAsync<ReservantoApiUnavailableException>(() => GetSegmentsAsync(api));

        Assert.Equal(0, api.Api.CountFor(_segmentPath));
    }

    private static void Reject(TestApi api) =>
        api.Api.Respond(_tokenPath, HttpStatusCode.OK, """{"IsError":true,"ErrorMessage":"Neplatný LongTimeToken.","ErrorParameter":"LongTimeToken","ErrorParameters":["LongTimeToken"]}""");

    private static Task<IReadOnlyList<Models.SegmentApiModel>> GetSegmentsAsync(TestApi api) =>
        api.Client.Segment.GetListAsync(new ObjectStateListRequest(), CancellationToken.None);
}
