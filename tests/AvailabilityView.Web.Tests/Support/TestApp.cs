using AvailabilityView.Application.Connection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AvailabilityView.Web.Tests.Support;

/// <summary>
/// Aplikace pro testy, bez sítě. Prostředí Testing: user-secrets se načítají jen v Development, vývojářův token tak
/// test neovlivní.
/// </summary>
internal static class TestApp
{
    public const string ApiAddress = "https://api.test.invalid/";

    public static WebApplicationFactory<Program> Create(
        string? token = null,
        string? clientId = null,
        ConnectionCheckResult? check = null,
        string environment = "Testing",
        Action<IServiceCollection>? services = null,
        IReadOnlyDictionary<string, string?>? settings = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Reservanto:BaseUrl"] = ApiAddress,
                    ["Reservanto:LongTimeToken"] = token,
                    ["Reservanto:ClientId"] = clientId,
                }).AddInMemoryCollection(settings ?? new Dictionary<string, string?>()));
            builder.ConfigureTestServices(collection =>
            {
                collection.AddSingleton<IConnectionCheck>(new FixedConnectionCheck(check ?? new ConnectionCheckResult(ConnectionState.Connected)));
                services?.Invoke(collection);
            });
        });

    /// <summary>Klient, který nesleduje přesměrování, aby test viděl jejich cíl.</summary>
    public static HttpClient Client(WebApplicationFactory<Program> app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    /// <summary>Aplikace pod podadresářem, jako pod IIS aplikací.</summary>
    public static Action<IServiceCollection> UnderPathBase(string pathBase) =>
        collection => collection.AddSingleton<IStartupFilter>(new PathBaseFilter(pathBase));

    private sealed class PathBaseFilter(string pathBase) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UsePathBase(pathBase);
            next(app);
        };
    }

    private sealed class FixedConnectionCheck(ConnectionCheckResult result) : IConnectionCheck
    {
        public Task<ConnectionCheckResult> CheckAsync(CancellationToken ct) => Task.FromResult(result);
    }
}
