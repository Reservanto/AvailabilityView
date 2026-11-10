using System.Net;
using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Connection;
using AvailabilityView.Tests.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Newtonsoft.Json.Linq;
using Reservanto.ApiClient;
using Reservanto.ApiClient.Core;
using Reservanto.ApiClient.Tests.Support;

namespace AvailabilityView.Infrastructure.Tests.Support;

/// <summary>Infrastructure složená stejnou registrací jako v aplikaci, se skutečným klientem a falešným API.</summary>
internal sealed class Harness : IDisposable
{
    public const string LongTimeToken = "ltt-testu";

    private static readonly string[] _matchedFields = ["BookingResourceId", "BookingServiceId", "LocationId", "SegmentId"];

    private readonly ServiceProvider _provider;
    private readonly IConfigurationRoot _configuration;

    public Harness(Action<Dictionary<string, string?>>? configure = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Reservanto:BaseUrl"] = "https://api.test.invalid/",
            ["Reservanto:LongTimeToken"] = LongTimeToken,
        };
        configure?.Invoke(settings);
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Time);
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace));
        services.AddFakeLogging();
        services.AddAvailabilityViewInfrastructure(_configuration);
        // Přísné čtení: neznámé pole v nahrávce shodí test.
        services.PostConfigure<ReservantoApiOptions>(options => options.StrictDeserialization = true);
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = Api));
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        Api.Respond(ApiEndpoints.AuthorizeGetShortTimeToken.Path, HttpStatusCode.OK, """{"IsError":false,"ShortTimeToken":"stt-testu"}""");

        // Rezervace se nenahrávají celé (údaje zákazníků), výchozí je prázdný seznam. Test ho může přepsat.
        Api.Respond(ApiEndpoints.EventGetList.Path, HttpStatusCode.OK, """{"IsError":false,"Events":[]}""");
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero));

    public FakeApi Api { get; } = new();

    /// <summary>Log všech úrovní včetně Trace.</summary>
    public FakeLogCollector Logs => _provider.GetFakeLogCollector();

    public IAvailabilityReader Reader => _provider.GetRequiredService<IAvailabilityReader>();

    public IPublicCatalog Catalog => _provider.GetRequiredService<IPublicCatalog>();

    public IConnectionCheck Check => _provider.GetRequiredService<IConnectionCheck>();

    public IEnumerable<ISegmentAvailabilitySource> Sources => _provider.GetRequiredService<IEnumerable<ISegmentAvailabilitySource>>();

    public T Get<T>()
        where T : notnull => _provider.GetRequiredService<T>();

    public void ChangeLongTimeToken(string? token)
    {
        _configuration["Reservanto:LongTimeToken"] = token;
        _configuration.Reload();
    }

    /// <summary>Falešné API přehrává nahrávky a čas se nastaví na okamžik nahrání.</summary>
    public void UseRecordings()
    {
        string root = Path.Combine(RepositoryRoot.Path, "tests", "api-recordings");
        JObject[] recordings = [.. Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).Select(file => JObject.Parse(File.ReadAllText(file)))];
        Assert.NotEmpty(recordings);

        foreach (IGrouping<string, JObject> endpoint in recordings.GroupBy(recording => (string)recording["endpoint"]!))
        {
            JObject[] variants = [.. endpoint.Where(recording => (string?)recording["scenario"] != "invalid")];
            Api.Respond($"/v1/{endpoint.Key}", request =>
            {
                JObject recording = variants.Single(variant => Matches(variant, request));
                return Task.FromResult(FakeApi.Json((HttpStatusCode)(int)recording["response"]!["status"]!, recording["response"]!["body"]!.ToString()));
            });
        }

        Time.SetUtcNow(DateTimeOffset.Parse((string)recordings[0]["recordedAt"]!, System.Globalization.CultureInfo.InvariantCulture));
    }

    public void Dispose() => _provider.Dispose();

    private static bool Matches(JObject recording, ReceivedRequest request) =>
        _matchedFields.All(field => JToken.DeepEquals(recording["request"]![field], request.Json[field]));
}
