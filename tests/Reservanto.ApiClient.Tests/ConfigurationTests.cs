using Microsoft.Extensions.Options;
using Reservanto.ApiClient.Tests.Support;

namespace Reservanto.ApiClient.Tests;

/// <summary>Konfigurace klienta.</summary>
public sealed class ConfigurationTests
{
    [Fact]
    public void MissingLongTimeToken_PassesValidation()
    {
        // Nepřipojená instance musí nastartovat, aby se dala připojit.
        using var api = new TestApi(settings => settings["Reservanto:LongTimeToken"] = null);

        Assert.Null(api.Options.LongTimeToken);
    }

    [Theory]
    [InlineData("http://api.reservanto.cz/")]
    [InlineData("api.reservanto.cz")]
    [InlineData("https://[")]
    public void NonHttpsOrRelativeBaseUrl_FailsValidation(string baseUrl)
    {
        using var api = new TestApi(settings => settings["Reservanto:BaseUrl"] = baseUrl);

        OptionsValidationException error = Assert.Throws<OptionsValidationException>(() => api.Get<IStartupValidator>().Validate());

        Assert.Contains("Reservanto:BaseUrl", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://api.reservanto.cz/")]
    [InlineData("https://[")]
    public async Task InvalidBaseUrl_ChangedAfterStart_DoesNotBreakReading(string baseUrl)
    {
        // Překlep v souboru za běhu nesmí shodit každou stránku.
        using var api = new TestApi();
        await api.Client.Segment.GetListAsync(new Endpoints.ObjectStateListRequest(), TestContext.Current.CancellationToken);
        api.ChangeSetting("Reservanto:BaseUrl", baseUrl);

        await api.Client.Segment.GetListAsync(new Endpoints.ObjectStateListRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(TestApi.LongTimeToken, api.Options.LongTimeToken);
    }

    [Fact]
    public void InternalValues_CannotBeSetFromConfiguration()
    {
        // Klíče konfigurace jsou veřejné rozhraní pro merchanty a tyhle ve SPEC sekce 5 nejsou.
        using var api = new TestApi(settings =>
        {
            settings["Reservanto:Timeout"] = "00:00:01";
            settings["Reservanto:ShortTimeTokenLifetime"] = "00:02:00";
            settings["Reservanto:Degradation:FailureThreshold"] = "1";
        });

        Assert.Equal(TimeSpan.FromSeconds(10), api.Options.Timeout);
        Assert.Equal(TimeSpan.FromMinutes(30), api.Options.ShortTimeTokenLifetime);
        Assert.Equal(5, api.Options.Degradation.FailureThreshold);
    }

    [Theory]
    [InlineData("http://api.reservanto.cz/")]
    [InlineData("https://[")]
    public async Task InvalidBaseUrl_BeforeFirstRequest_SendsNothing(string baseUrl)
    {
        // Kontrola při startu prošla, adresa se změnila dřív, než klient sestavil spojení. Token nesmí odejít nešifrovaně.
        using var api = new TestApi();
        api.Get<IStartupValidator>().Validate();
        api.ChangeSetting("Reservanto:BaseUrl", baseUrl);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            api.Client.Segment.GetListAsync(new Endpoints.ObjectStateListRequest(), TestContext.Current.CancellationToken));

        Assert.Empty(api.Api.Received);
    }

    [Fact]
    public void DefaultBaseUrl_IsProductionApi()
    {
        Assert.Equal(new Uri("https://api.reservanto.cz/"), new ReservantoApiOptions().ApiAddress());
    }
}
