using AvailabilityView.Application.Connection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace AvailabilityView.Infrastructure.Tests;

/// <summary>
/// Instance načte token z konfiguračního souboru bez restartu (<c>docs/SPEC.md</c> sekce 6 krok 4). Skutečný soubor
/// a stejné nastavení, jaké pro <c>appsettings.Production.json</c> používá ASP.NET Core.
/// </summary>
public sealed class TokenReloadTests : IDisposable
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private readonly string _directory = Directory.CreateTempSubdirectory("availabilityview-").FullName;
    private readonly string _file;
    private readonly ServiceProvider _provider;

    public TokenReloadTests()
    {
        _file = Path.Combine(_directory, "appsettings.Production.json");
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile(new PhysicalFileProvider(_directory), "appsettings.Production.json", optional: true, reloadOnChange: true)
            .Build();
        _provider = new ServiceCollection().AddLogging().AddAvailabilityViewInfrastructure(configuration).BuildServiceProvider();
    }

    private IInstanceConnection Connection => _provider.GetRequiredService<IInstanceConnection>();

    [Fact]
    public async Task File_CreatedAfterStart_ConnectsInstance_AndDeletingTokenDisconnects()
    {
        Assert.False(Connection.IsConnected);

        await File.WriteAllTextAsync(_file, """{"Reservanto":{"LongTimeToken":"<token>"}}""", TestContext.Current.CancellationToken);
        await WaitForAsync(connected: true);

        await File.WriteAllTextAsync(_file, """{"Reservanto":{}}""", TestContext.Current.CancellationToken);
        await WaitForAsync(connected: false);
    }

    [Theory]
    [InlineData("http://api.reservanto.cz/")]
    [InlineData("https://[")]
    public async Task InvalidBaseUrl_WrittenAtRuntime_DoesNotBreakPages(string baseUrl)
    {
        await File.WriteAllTextAsync(_file, """{"Reservanto":{"LongTimeToken":"<token>"}}""", TestContext.Current.CancellationToken);
        await WaitForAsync(connected: true);

        await File.WriteAllTextAsync(_file, """{"Reservanto":{"LongTimeToken":"<token>","BaseUrl":""" + "\"" + baseUrl + "\"}}", TestContext.Current.CancellationToken);
        await Task.Delay(1000, TestContext.Current.CancellationToken);

        Assert.True(Connection.IsConnected);
    }

    public void Dispose()
    {
        _provider.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private async Task WaitForAsync(bool connected)
    {
        DateTimeOffset end = TimeProvider.System.GetUtcNow() + _wait;
        while (Connection.IsConnected != connected && TimeProvider.System.GetUtcNow() < end)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Equal(connected, Connection.IsConnected);
    }
}
