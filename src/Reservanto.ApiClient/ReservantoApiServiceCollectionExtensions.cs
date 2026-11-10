using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Reservanto.ApiClient.Authorization;
using Reservanto.ApiClient.Core;

namespace Reservanto.ApiClient;

/// <summary>Registrace klienta do DI.</summary>
public static class ReservantoApiServiceCollectionExtensions
{
    /// <summary>
    /// Zaregistruje <see cref="ReservantoApiClient"/> (typed client přes <see cref="IHttpClientFactory"/>) a správu
    /// krátkodobého tokenu. Konfigurace ze sekce <see cref="ReservantoApiOptions.SectionName"/>.
    /// </summary>
    /// <param name="services">Kolekce služeb.</param>
    /// <param name="configuration">Konfigurace aplikace.</param>
    public static IServiceCollection AddReservantoApiClient(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ReservantoApiOptions>()
            .Bind(configuration.GetSection(ReservantoApiOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ReservantoApiOptions>, ReservantoApiOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<ShortTimeTokenProvider>();
        services.TryAddSingleton<ApiDegradationMonitor>();
        services.TryAddTransient<ShortTimeTokenHandler>();

        services.AddHttpClient(ShortTimeTokenProvider.HttpClientName, ConfigureHttpClient);
        services.AddHttpClient<ApiTransport>(ConfigureHttpClient)
            .AddHttpMessageHandler<ShortTimeTokenHandler>();
        services.TryAddTransient(provider => new ReservantoApiClient(provider.GetRequiredService<ApiTransport>()));

        return services;
    }

    private static void ConfigureHttpClient(IServiceProvider provider, HttpClient http)
    {
        ReservantoApiOptions options = provider.GetRequiredService<IOptions<ReservantoApiOptions>>().Value;
        // Kontrola i tady: adresa se mohla změnit po startu, ještě než klient poprvé sestavil spojení.
        http.BaseAddress = EnsureTrailingSlash(options.ApiAddress());
        http.Timeout = options.Timeout;
    }

    internal static Uri EnsureTrailingSlash(Uri url) =>
        url.AbsoluteUri.EndsWith('/') ? url : new Uri(url.AbsoluteUri + "/");
}
