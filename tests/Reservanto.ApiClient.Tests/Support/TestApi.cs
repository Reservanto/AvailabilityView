using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Reservanto.ApiClient.Authorization;
using Reservanto.ApiClient.Core;

namespace Reservanto.ApiClient.Tests.Support;

/// <summary>
/// Klient sestavený stejnou registrací jako v aplikaci, jen s falešným API, simulovaným časem a zachytáváním logu.
/// Token a prázdný seznam zaměření jsou nastavené předem, test je může přepsat.
/// </summary>
internal sealed class TestApi : IDisposable
{
    /// <summary>Testovací hodnota, hlídá se, že se nikde neobjeví v logu.</summary>
    public const string LongTimeToken = "ltt-testu";

    private readonly ServiceProvider _provider;
    private readonly IConfigurationRoot _configuration;

    public TestApi(Action<Dictionary<string, string?>>? configure = null, bool strict = true, TimeSpan? timeout = null)
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
        // Všechny úrovně včetně Trace: kontrola, že se tokeny nelogují, musí vidět i ladicí výpisy.
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace));
        services.AddFakeLogging();
        services.AddReservantoApiClient(_configuration);
        services.PostConfigure<ReservantoApiOptions>(options =>
        {
            options.StrictDeserialization = strict;
            options.Timeout = timeout ?? options.Timeout;
        });
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = Api));
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        Api.Respond(ApiEndpoints.AuthorizeGetShortTimeToken.Path, HttpStatusCode.OK, """{"IsError":false,"ShortTimeToken":"stt-vychozi"}""");
        Api.Respond(ApiEndpoints.SegmentGetList.Path, HttpStatusCode.OK, """{"IsError":false,"Items":[]}""");
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero));

    public FakeApi Api { get; } = new();

    public ReservantoApiClient Client => _provider.GetRequiredService<ReservantoApiClient>();

    public ShortTimeTokenProvider Tokens => _provider.GetRequiredService<ShortTimeTokenProvider>();

    public ApiDegradationMonitor Degradation => _provider.GetRequiredService<ApiDegradationMonitor>();

    public ReservantoApiOptions Options => _provider.GetRequiredService<IOptionsMonitor<ReservantoApiOptions>>().CurrentValue;

    public FakeLogCollector Logs => _provider.GetFakeLogCollector();

    /// <summary>Výměna tokenu v konfiguraci za běhu, jako když správce upraví soubor.</summary>
    public void ChangeLongTimeToken(string? token) => ChangeSetting("Reservanto:LongTimeToken", token);

    /// <summary>Změna konfigurace za běhu.</summary>
    public void ChangeSetting(string key, string? value)
    {
        _configuration[key] = value;
        _configuration.Reload();
    }

    public T Get<T>()
        where T : notnull => _provider.GetRequiredService<T>();

    /// <summary>Počet požadavků na krátkodobý token.</summary>
    public int TokenRequests => Api.CountFor(ApiEndpoints.AuthorizeGetShortTimeToken.Path);

    public void Dispose() => _provider.Dispose();
}
