using System.Net;
using Reservanto.ApiClient.Core;
using Reservanto.ApiClient.Endpoints;
using Reservanto.ApiClient.Models;
using Reservanto.ApiClient.Tests.Support;

namespace Reservanto.ApiClient.Tests;

/// <summary>API vrátí nejvýš 100 položek na stránku, klient musí přečíst všechny.</summary>
public sealed class PagingTests
{
    private static readonly string _path = ApiEndpoints.SegmentGetList.Path;

    [Fact]
    public async Task Client_ReadsAllPages_InOrder()
    {
        using var api = new TestApi();
        RespondWithPages(api, _path, "Items", 100, 100, 5);

        IReadOnlyList<SegmentApiModel> segments = await GetSegmentsAsync(api);

        Assert.Equal(205, segments.Count);
        Assert.Equal(Enumerable.Range(1, 205), segments.Select(segment => segment.Id));
        Assert.Equal([1, 2, 3], api.Api.Received.Where(IsList).Select(request => (int)request.Json["Page"]!));
        Assert.All(api.Api.Received.Where(IsList), request => Assert.Equal(100, (int)request.Json["PageSize"]!));
    }

    [Fact]
    public async Task ExactlyFullLastPage_EndsWithEmptyPage()
    {
        using var api = new TestApi();
        RespondWithPages(api, _path, "Items", 100, 0);

        Assert.Equal(100, (await GetSegmentsAsync(api)).Count);
    }

    [Fact]
    public async Task ShortFirstPage_MakesOneCall()
    {
        using var api = new TestApi();
        RespondWithPages(api, _path, "Items", 3);

        await GetSegmentsAsync(api);

        Assert.Equal(1, api.Api.CountFor(_path));
    }

    [Fact]
    public async Task Services_InOwnField_AreAlsoPaged()
    {
        using var api = new TestApi();
        string path = ApiEndpoints.BookingServiceGetList.Path;
        RespondWithPages(api, path, "BookingServices", 100, 1);

        IReadOnlyList<BookingServiceApiModel> services = await api.Client.BookingService.GetListAsync(
            new ObjectStateModifiedAfterListRequest(), CancellationToken.None);

        Assert.Equal(101, services.Count);
    }

    [Fact]
    public async Task ApiIgnoringPages_FailsInsteadOfLoopingForever()
    {
        using var api = new TestApi();
        string fullPage = Page("Items", 1, 100);
        api.Api.Respond(_path, HttpStatusCode.OK, fullPage);

        await Assert.ThrowsAsync<ReservantoApiException>(() => GetSegmentsAsync(api));

        Assert.Equal(100, api.Api.CountFor(_path));
    }

    private static bool IsList(ReceivedRequest request) => request.Path == _path;

    private static Task<IReadOnlyList<SegmentApiModel>> GetSegmentsAsync(TestApi api) =>
        api.Client.Segment.GetListAsync(new ObjectStateListRequest(), CancellationToken.None);

    private static void RespondWithPages(TestApi api, string path, string field, params int[] sizes) =>
        api.Api.Respond(path, request =>
        {
            int page = (int)request.Json["Page"]!;
            int firstId = sizes.Take(page - 1).Sum() + 1;
            return Task.FromResult(FakeApi.Json(HttpStatusCode.OK, Page(field, firstId, sizes[page - 1])));
        });

    private static string Page(string field, int firstId, int size)
    {
        IEnumerable<string> items = Enumerable.Range(firstId, size).Select(id => $$"""{"Id":{{id}},"State":"Public"}""");
        return $$"""{"IsError":false,"{{field}}":[{{string.Join(',', items)}}]}""";
    }
}
