using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace AvailabilityView.Web.Tests;

/// <summary>
/// Token z adresy se nesmí dostat do logu (<c>docs/SPEC.md</c> sekce 10). Běží nad skutečným Kestrelem s logováním
/// na úrovni Trace a se zapnutým logováním požadavků, tedy v nejhorším nastavení, které si merchant může zvolit.
/// </summary>
public sealed class CallbackLoggingTests
{
    private const string Token = "ltt-testu-0123456789";
    private static readonly string _address = $"/Auth/Callback?accepted=1&token={Token}";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Token_DoesNotReachLog_OnRealServer()
    {
        await using LoggedApp app = new(protectAddress: true);

        string page = await app.GetAsync(_address);

        Assert.Contains(Token, page, StringComparison.Ordinal);
        string log = app.Log();
        Assert.Contains("Request starting", log, StringComparison.Ordinal);
        Assert.Contains("/Auth/Callback", log, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_CanFail_WithoutProtection_TokenIsInLog()
    {
        // Negativní test: bez ochrany by token v logu byl, kontrola výše tedy opravdu něco hlídá.
        await using LoggedApp app = new(protectAddress: false);

        await app.GetAsync(_address);

        Assert.Contains(Token, app.Log(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NextRequestOnSameConnection_DoesNotSeePreviousToken()
    {
        // Kestrel objekty požadavku mezi požadavky na jednom spojení znovu používá.
        await using LoggedApp app = new(protectAddress: true);

        await app.GetAsync(_address);
        string next = await app.GetAsync("/Auth/Callback");

        Assert.DoesNotContain(Token, next, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnderPathBase_TokenDoesNotReachLog()
    {
        await using LoggedApp app = new(protectAddress: true, pathBase: "/app");

        string page = await app.GetAsync($"/app{_address}");

        Assert.Contains(Token, page, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, app.Log(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OtherAddresses_KeepTheirParameters()
    {
        await using LoggedApp app = new(protectAddress: true);

        await app.GetAsync("/Setup?zustava=ano");

        Assert.Contains("zustava=ano", app.Log(), StringComparison.Ordinal);
    }

    /// <summary>Aplikace na Kestrelu se zachytáváním všech výpisů včetně stavu a scopů.</summary>
    private sealed class LoggedApp : IAsyncDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly FakeLogCollector _logs = new();
        private readonly HttpClient _client;

        public LoggedApp(bool protectAddress, string? pathBase = null)
        {
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Logging:LogLevel:Default"] = "Trace",
                        ["Logging:LogLevel:Microsoft.AspNetCore"] = "Trace",
                    }));
                builder.ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Trace);
                    logging.AddProvider(new FakeLoggerProvider(_logs));
                });
                builder.ConfigureTestServices(services =>
                {
                    // Bez těla odpovědi: to je stránka s tokenem a jeho logování jde zapnout jen úpravou kódu.
                    services.AddHttpLogging(options => options.LoggingFields = HttpLoggingFields.All & ~HttpLoggingFields.ResponseBody);
                    services.AddSingleton<IStartupFilter, HttpLoggingFirst>();
                    if (pathBase is not null)
                    {
                        Support.TestApp.UnderPathBase(pathBase)(services);
                    }

                    if (!protectAddress)
                    {
                        services.AddSingleton<IHttpContextFactory>(provider => new DefaultHttpContextFactory(provider));
                    }
                });
            });
            _factory.UseKestrel(0);
            _factory.StartServer();
            _client = _factory.CreateClient();
        }

        public async Task<string> GetAsync(string path)
        {
            using HttpResponseMessage response = await _client.GetAsync(new Uri(path, UriKind.Relative), Ct);
            return await response.Content.ReadAsStringAsync(Ct);
        }

        public string Log()
        {
            var text = new StringBuilder();
            foreach (FakeLogRecord record in _logs.GetSnapshot())
            {
                text.AppendLine(record.Message).AppendLine(record.Exception?.ToString());
                foreach (KeyValuePair<string, string?> state in record.StructuredState ?? [])
                {
                    text.AppendLine(state.Value);
                }

                foreach (object? scope in record.Scopes)
                {
                    text.AppendLine(scope?.ToString());
                }
            }

            return text.ToString();
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _factory.DisposeAsync();
        }
    }

    private sealed class HttpLoggingFirst : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseHttpLogging();
            next(app);
        };
    }
}
