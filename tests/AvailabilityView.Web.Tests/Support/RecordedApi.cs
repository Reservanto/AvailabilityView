using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AvailabilityView.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace AvailabilityView.Web.Tests.Support;

/// <summary>
/// Falešné API Reservanta, které přehrává katalog z nahrávek produkčního API (<c>tests/api-recordings</c>). Aplikace
/// ho čte skutečným klientem a skutečným katalogem z Infrastructure. Test může do odpovědí přidat vlastní objekty,
/// třeba neveřejné.
/// </summary>
internal sealed class RecordedApi : HttpMessageHandler
{
    /// <summary>Seznamy katalogu: koncový bod a název seznamu v odpovědi.</summary>
    public static readonly (string Endpoint, string List)[] Catalog =
    [
        ("Segment/GetList", "Items"),
        ("Location/GetList", "Items"),
        ("BookingResource/GetList", "Items"),
        ("BookingService/GetList", "BookingServices"),
    ];

    private readonly Dictionary<string, JsonObject> _bodies = new(StringComparer.Ordinal);

    public RecordedApi()
    {
        foreach ((string endpoint, _) in Catalog)
        {
            _bodies[endpoint] = Body(endpoint);
        }

        _bodies["Authorize/GetShortTimeToken"] = new JsonObject { ["IsError"] = false, ["ShortTimeToken"] = "stt-testu" };
    }

    /// <summary>Položka seznamu podle Id. Úprava se projeví v odpovědi.</summary>
    public JsonObject Item(string endpoint, string list, int id) => Assert.Single(Items(endpoint, list), item => item["Id"]?.GetValue<int>() == id);

    /// <summary>Kopie položky seznamu podle Id, základ pro přidaný objekt.</summary>
    public JsonObject CopyOf(string endpoint, string list, int id) => (JsonObject)Item(endpoint, list, id).DeepClone();

    /// <summary>Přidá položku do seznamu v odpovědi.</summary>
    public void Add(string endpoint, string list, JsonObject item) => List(endpoint, list).Add(item);

    /// <summary>Všechny položky seznamu.</summary>
    public IEnumerable<JsonObject> Items(string endpoint, string list) => List(endpoint, list).Select(item => (JsonObject)item!);

    /// <summary>Registrace, která pošle všechny požadavky klienta API sem.</summary>
    public Action<IServiceCollection> Use() => services =>
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = this));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;
        string endpoint = path[(path.IndexOf("/v1/", StringComparison.Ordinal) + 4)..];
        HttpResponseMessage response = _bodies.TryGetValue(endpoint, out JsonObject? body)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.NotFound);
        return Task.FromResult(response);
    }

    private JsonArray List(string endpoint, string list) => (JsonArray)_bodies[endpoint][list]!;

    private static JsonObject Body(string endpoint)
    {
        string file = Path.Combine([RepositoryRoot.Path, "tests", "api-recordings", .. endpoint.Split('/'), "ok.json"]);
        return (JsonObject)JsonNode.Parse(File.ReadAllText(file))!["response"]!["body"]!.DeepClone();
    }
}
