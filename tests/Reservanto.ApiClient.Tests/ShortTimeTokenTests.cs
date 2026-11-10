using System.Net;
using Reservanto.ApiClient.Core;
using Reservanto.ApiClient.Endpoints;
using Reservanto.ApiClient.Tests.Support;

namespace Reservanto.ApiClient.Tests;

/// <summary>Krátkodobý token merchanta.</summary>
public sealed class ShortTimeTokenTests
{
    private const string TokenPath = "/v1/Authorize/GetShortTimeToken";
    private const string SegmentPath = "/v1/Segment/GetList";
    private const string EmptyList = """{"IsError":false,"Items":[]}""";

    [Fact]
    public async Task FirstCall_AcquiresTokenLazily_AndSendsItAsRawAuthorizationHeader()
    {
        using var api = new TestApi();
        IssueTokens(api, "stt-1");

        await GetSegmentsAsync(api);

        Assert.Equal(1, api.TokenRequests);
        Assert.Equal(TestApi.LongTimeToken, (string)api.Api.Received[0].Json["LongTimeToken"]!);
        Assert.Null(api.Api.Received[0].Authorization);
        Assert.Equal("stt-1", api.Api.Received[^1].Authorization);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingLongTimeToken_FailsAsUnavailable_WithoutCallingApi(string? token)
    {
        using var api = new TestApi(settings => settings["Reservanto:LongTimeToken"] = token);

        ReservantoApiUnavailableException error = await Assert.ThrowsAsync<ReservantoApiUnavailableException>(() => GetSegmentsAsync(api));

        Assert.Contains("není připojená", error.Message, StringComparison.Ordinal);
        Assert.Empty(api.Api.Received);
        Assert.Equal(0, api.Tokens.ConsecutiveFailures);
    }

    [Fact]
    public async Task ParallelCalls_ShareOneTokenRequest()
    {
        using var api = new TestApi();
        var release = new TaskCompletionSource();
        api.Api.Respond(TokenPath, async _ =>
        {
            await release.Task;
            return TokenResponse("stt-shared");
        });

        Task[] calls = [.. Enumerable.Range(0, 20).Select(_ => GetSegmentsAsync(api))];
        release.SetResult();
        await Task.WhenAll(calls);

        Assert.Equal(1, api.TokenRequests);
        Assert.All(api.Api.Received.Where(r => r.Path == SegmentPath), r => Assert.Equal("stt-shared", r.Authorization));
    }

    [Fact]
    public async Task Token_IsReusedWithinLifetime_AndRenewedNearExpiry()
    {
        using var api = new TestApi();
        IssueTokens(api, "stt-1", "stt-2");

        await GetSegmentsAsync(api);
        api.Time.Advance(TimeSpan.FromMinutes(28));
        await GetSegmentsAsync(api);
        Assert.Equal(1, api.TokenRequests);

        // Poslední minuta životnosti (30 min) se už nepoužívá, token by mohl vypršet cestou.
        api.Time.Advance(TimeSpan.FromMinutes(1));
        await GetSegmentsAsync(api);

        Assert.Equal(2, api.TokenRequests);
        Assert.Equal("stt-2", api.Api.Received[^1].Authorization);
    }

    [Fact]
    public async Task Unauthorized_RefreshesTokenOnce_AndRepeatsRequest()
    {
        using var api = new TestApi();
        IssueTokens(api, "stt-old", "stt-new");
        api.Api.Respond(SegmentPath, request => Task.FromResult(request.Authorization == "stt-old"
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : FakeApi.Json(HttpStatusCode.OK, EmptyList)));

        await GetSegmentsAsync(api);

        Assert.Equal(2, api.TokenRequests);
        Assert.Equal(["stt-old", "stt-new"], api.Api.Received.Where(r => r.Path == SegmentPath).Select(r => r.Authorization));
    }

    [Fact]
    public async Task Unauthorized_AfterRefresh_FailsWithoutFurtherAttempts()
    {
        using var api = new TestApi();
        IssueTokens(api, "stt-1", "stt-2", "stt-3");
        api.Api.Respond(SegmentPath, _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        ReservantoApiException error = await Assert.ThrowsAsync<ReservantoApiException>(() => GetSegmentsAsync(api));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Equal(2, api.TokenRequests);
        Assert.Equal(2, api.Api.CountFor(SegmentPath));
    }

    [Fact]
    public async Task ConcurrentUnauthorized_RefreshesOnlyOnce()
    {
        using var api = new TestApi();
        IssueTokens(api, "stt-old", "stt-new");
        var bothRejected = new TaskCompletionSource();
        int rejected = 0;
        api.Api.Respond(SegmentPath, async request =>
        {
            if (request.Authorization == "stt-old")
            {
                if (Interlocked.Increment(ref rejected) == 2)
                {
                    bothRejected.SetResult();
                }

                await bothRejected.Task;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            return FakeApi.Json(HttpStatusCode.OK, EmptyList);
        });

        await Task.WhenAll(GetSegmentsAsync(api), GetSegmentsAsync(api));

        // Druhé odmítnutí stejného tokenu nesmí vyvolat další požadavek, token už mezitím někdo obnovil.
        Assert.Equal(2, api.TokenRequests);
    }

    [Fact]
    public async Task InvalidCredential_FailsAsRejected_AndBacksOffWithoutCallingApi()
    {
        using var api = new TestApi();
        RejectTokens(api);

        await Assert.ThrowsAsync<ReservantoTokenRejectedException>(() => GetSegmentsAsync(api));
        int requestsAfterFirstCall = api.TokenRequests;
        await Assert.ThrowsAsync<ReservantoTokenRejectedException>(() => GetSegmentsAsync(api));

        Assert.Equal(requestsAfterFirstCall, api.TokenRequests);
        Assert.Equal(0, api.Api.CountFor(SegmentPath));

        api.Time.Advance(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ReservantoTokenRejectedException>(() => GetSegmentsAsync(api));
        Assert.Equal(requestsAfterFirstCall + 1, api.TokenRequests);
    }

    [Fact]
    public async Task Backoff_GrowsExponentially_UpToFiveMinutes()
    {
        using var api = new TestApi();
        RejectTokens(api);
        var waits = new List<TimeSpan>();
        for (int failure = 1; failure <= 9; failure++)
        {
            await Assert.ThrowsAsync<ReservantoTokenRejectedException>(() => api.Tokens.GetTokenAsync(CancellationToken.None).AsTask());
            TimeSpan wait = TimeSpan.Zero;
            while (await AttemptBlockedAsync(api))
            {
                api.Time.Advance(TimeSpan.FromSeconds(1));
                wait += TimeSpan.FromSeconds(1);
            }

            waits.Add(wait);
        }

        Assert.Equal([5, 10, 20, 40, 80, 160, 300, 300, 300], waits.Select(w => (int)w.TotalSeconds));
    }

    [Fact]
    public async Task HangingTokenEndpoint_CountsAsFailure_AndStartsBackoff()
    {
        // Token se získává s vlastním timeoutem, ne pod zrušením volajícího, jinak by se selhání nikdy nezapočítalo.
        using var api = new TestApi(timeout: TimeSpan.FromMilliseconds(200));
        api.Api.Respond(TokenPath, async (_, requestAborted) =>
        {
            await Task.Delay(Timeout.Infinite, requestAborted);
            throw new InvalidOperationException("nedosažitelné");
        });

        await Assert.ThrowsAnyAsync<Exception>(() => GetSegmentsAsync(api));

        Assert.Equal(1, api.Tokens.ConsecutiveFailures);
        int requests = api.TokenRequests;
        await Assert.ThrowsAsync<ReservantoApiUnavailableException>(() => api.Tokens.GetTokenAsync(CancellationToken.None).AsTask());
        Assert.Equal(requests, api.TokenRequests);
    }

    [Fact]
    public async Task Backoff_CountsFromFailure_NotFromStartOfRequest()
    {
        // Výchozí timeout je delší než první backoff: počítaný od začátku požadavku by byl vypršelý hned po selhání.
        using var api = new TestApi();
        api.Api.Respond(TokenPath, _ =>
        {
            api.Time.Advance(TimeSpan.FromSeconds(10));
            return Task.FromResult(FakeApi.Json(HttpStatusCode.ServiceUnavailable, string.Empty));
        });

        await Assert.ThrowsAsync<ReservantoApiUnavailableException>(() => api.Tokens.GetTokenAsync(CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<ReservantoApiUnavailableException>(() => api.Tokens.GetTokenAsync(CancellationToken.None).AsTask());

        Assert.Equal(1, api.TokenRequests);
    }

    [Fact]
    public async Task MalformedTokenResponse_DoesNotLeakItsContent()
    {
        // Chyba čtení JSON cituje hodnotu z odpovědi, a ta může být token.
        using var api = new TestApi();
        api.Api.Respond(TokenPath, HttpStatusCode.OK, "\"stt-secret-v-chybe\"");

        ReservantoApiUnavailableException error = await Assert.ThrowsAsync<ReservantoApiUnavailableException>(() => GetSegmentsAsync(api));

        string everything = string.Join("\n", api.Logs.GetSnapshot().Select(record => $"{record.Message} {record.Exception}"));
        Assert.NotEmpty(api.Logs.GetSnapshot());
        Assert.DoesNotContain("stt-secret", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("stt-secret", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyTokenInResponse_IsFailure()
    {
        using var api = new TestApi();
        api.Api.Respond(TokenPath, HttpStatusCode.OK, """{"IsError":false,"ShortTimeToken":""}""");

        await Assert.ThrowsAsync<ReservantoApiUnavailableException>(() => GetSegmentsAsync(api));
    }

    [Fact]
    public async Task Tokens_NeverAppearInLogs()
    {
        // Projde celý tok včetně selhání. Tokeny se nelogují ani zkrácené.
        using var api = new TestApi();
        IssueTokens(api, "stt-secret-1", "stt-secret-2");
        api.Api.Respond(SegmentPath, request => Task.FromResult(request.Authorization == "stt-secret-1"
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : FakeApi.Json(HttpStatusCode.OK, EmptyList)));
        await GetSegmentsAsync(api);
        api.Api.Respond(TokenPath, HttpStatusCode.OK, """{"IsError":true,"ErrorMessage":"Neplatný token","ErrorParameter":"LongTimeToken","ShortTimeToken":"stt-secret-3"}""");
        api.Time.Advance(TimeSpan.FromMinutes(30));
        await Assert.ThrowsAsync<ReservantoTokenRejectedException>(() => GetSegmentsAsync(api));

        string everything = string.Join("\n", api.Logs.GetSnapshot().Select(record => $"{record.Message} {record.Exception}"));
        Assert.NotEmpty(api.Logs.GetSnapshot());
        Assert.DoesNotContain(TestApi.LongTimeToken, everything, StringComparison.Ordinal);
        Assert.DoesNotContain("stt-secret", everything, StringComparison.Ordinal);
    }

    private static Task<IReadOnlyList<Models.SegmentApiModel>> GetSegmentsAsync(TestApi api) =>
        api.Client.Segment.GetListAsync(new ObjectStateListRequest(), CancellationToken.None);

    private static void IssueTokens(TestApi api, params string[] tokens)
    {
        int next = 0;
        api.Api.Respond(TokenPath, _ => Task.FromResult(TokenResponse(tokens[Math.Min(Interlocked.Increment(ref next) - 1, tokens.Length - 1)])));
    }

    private static void RejectTokens(TestApi api) =>
        api.Api.Respond(TokenPath, HttpStatusCode.OK, """{"IsError":true,"ErrorMessage":"Neplatný LongTimeToken.","ErrorParameter":"LongTimeToken"}""");

    private static HttpResponseMessage TokenResponse(string token) =>
        FakeApi.Json(HttpStatusCode.OK, $$"""{"IsError":false,"ShortTimeToken":"{{token}}"}""");

    // Je další pokus o token ještě blokovaný backoffem? Pokus, který projde k API, se pozná podle počtu požadavků.
    private static async Task<bool> AttemptBlockedAsync(TestApi api)
    {
        int before = api.TokenRequests;
        await Assert.ThrowsAnyAsync<ReservantoApiException>(() => api.Tokens.GetTokenAsync(CancellationToken.None).AsTask());
        return api.TokenRequests == before;
    }
}
