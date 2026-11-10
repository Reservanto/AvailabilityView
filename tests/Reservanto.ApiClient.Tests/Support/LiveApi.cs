using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Reservanto.ApiClient.Tests.Support;

/// <summary>
/// Klient proti skutečnému API s tokenem z user-secrets aplikace, pro smoke testy. Účet je skutečný, projekt ale
/// jinak než čtením API volat neumí, klient zápisové endpointy nemá.
/// </summary>
internal sealed class LiveApi : IDisposable
{
    private const string UserSecretsId = "availabilityview-web";

    private readonly ServiceProvider _provider;

    public LiveApi(string? longTimeToken = null, Func<DelegatingHandler>? observer = null, bool strict = true)
    {
        IConfigurationBuilder builder = new ConfigurationBuilder().AddUserSecrets(UserSecretsId);
        if (longTimeToken is not null)
        {
            builder.AddInMemoryCollection(new Dictionary<string, string?> { ["Reservanto:LongTimeToken"] = longTimeToken });
        }

        IConfiguration configuration = builder.Build();
        if (string.IsNullOrWhiteSpace(configuration["Reservanto:LongTimeToken"]))
        {
            throw new InvalidOperationException("Smoke testy potřebují LongTimeToken v user-secrets, připojte účet v průvodci.");
        }

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddReservantoApiClient(configuration);
        services.PostConfigure<ReservantoApiOptions>(options => options.StrictDeserialization = strict);
        if (observer is not null)
        {
            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handlers => handlers.AdditionalHandlers.Add(observer())));
        }

        _provider = services.BuildServiceProvider();
    }

    public ReservantoApiClient Client => _provider.GetRequiredService<ReservantoApiClient>();

    public void Dispose() => _provider.Dispose();
}
