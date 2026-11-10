using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Reservanto.ApiClient.Tests.Support;

/// <summary>
/// Falešné API: odpovídá tím, co test nastaví. Požadavek bez nastavené odpovědi = výjimka, test nikdy nesáhne na síť.
/// Zaznamenává přijaté požadavky pro aserce.
/// </summary>
internal sealed class FakeApi : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Func<ReceivedRequest, CancellationToken, Task<HttpResponseMessage>>> _responders = new();
    private readonly ConcurrentQueue<ReceivedRequest> _received = new();

    public IReadOnlyList<ReceivedRequest> Received => [.. _received];

    public int CountFor(string path) => _received.Count(request => request.Path == path);

    public void Respond(string path, Func<ReceivedRequest, Task<HttpResponseMessage>> responder) =>
        _responders[path] = (request, _) => responder(request);

    /// <summary>S tokenem zrušení požadavku, pro simulaci timeoutu HttpClientu.</summary>
    public void Respond(string path, Func<ReceivedRequest, CancellationToken, Task<HttpResponseMessage>> responder) =>
        _responders[path] = responder;

    public void Respond(string path, HttpStatusCode status, string body) =>
        Respond(path, _ => Task.FromResult(Json(status, body)));

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;
        string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        string? authorization = request.Headers.TryGetValues("Authorization", out IEnumerable<string>? values) ? values.Single() : null;
        var received = new ReceivedRequest(path, authorization, body);
        _received.Enqueue(received);

        return _responders.TryGetValue(path, out Func<ReceivedRequest, CancellationToken, Task<HttpResponseMessage>>? responder)
            ? await responder(received, cancellationToken)
            : throw new InvalidOperationException($"Test nenastavil odpověď pro {path}, testy nesmí volat síť.");
    }
}

/// <summary>Požadavek, který falešné API přijalo.</summary>
internal sealed record ReceivedRequest(string Path, string? Authorization, string Body)
{
    public JObject Json => JObject.Parse(Body);
}
