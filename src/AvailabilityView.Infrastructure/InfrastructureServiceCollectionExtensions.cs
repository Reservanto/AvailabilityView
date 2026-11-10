using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Connection;
using AvailabilityView.Infrastructure.Availability;
using AvailabilityView.Infrastructure.Connection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Reservanto.ApiClient;

namespace AvailabilityView.Infrastructure;

/// <summary>Registrace vrstvy Infrastructure do DI.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Zaregistruje klienta Reservanto API, adaptéry rozhraní z Application a služby Application, která sama
    /// žádnou závislost na DI nemá.
    /// </summary>
    /// <param name="services">Kolekce služeb.</param>
    /// <param name="configuration">Konfigurace aplikace.</param>
    public static IServiceCollection AddAvailabilityViewInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddReservantoApiClient(configuration);
        services.AddOptions<AvailabilityViewOptions>()
            .Bind(configuration.GetSection(AvailabilityViewOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AvailabilityViewOptions>, AvailabilityViewOptionsValidator>());
        services.TryAddSingleton(provider =>
        {
            AvailabilityViewOptions options = provider.GetRequiredService<IOptions<AvailabilityViewOptions>>().Value;
            return new AvailabilitySettings(TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone), options.MaxDays, options.DefaultLanguage);
        });

        services.TryAddSingleton<IInstanceConnection, ReservantoInstanceConnection>();
        services.TryAddSingleton<IConnectionSetup, ReservantoConnectionSetup>();
        services.TryAddSingleton<ReadCache>();

        // Přechodné: klient API žije krátce kvůli obměně HttpClientu.
        services.TryAddTransient<IPublicCatalog, ReservantoPublicCatalog>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISegmentAvailabilitySource, ClassesAvailabilitySource>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISegmentAvailabilitySource, IndividualAvailabilitySource>());
        services.TryAddTransient<IMerchantInfo, ReservantoMerchantInfo>();
        services.TryAddTransient<IResourceOccupancy, ReservantoResourceOccupancy>();
        services.TryAddTransient<IWorkingHours, ReservantoWorkingHours>();
        services.TryAddTransient<IConnectionCheck, ConnectionCheck>();
        services.TryAddTransient<IAvailabilityReader, AvailabilityReader>();
        return services;
    }
}
