using System.Collections.Specialized;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using AvailabilityView.Application.Connection;
using AvailabilityView.Web.Resources;
using AvailabilityView.Web.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace AvailabilityView.Web.Tests;

/// <summary>Průvodce připojením na nepřipojené a připojené instanci (<c>docs/SPEC.md</c> sekce 6).</summary>
public sealed class SetupPageTests
{
    private const string Token = "ltt-testu-0123456789";
    private const string ConfiguredClient = "klient-z-konfigurace";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HomePage_OnNotConnectedInstance_LinksToSetup()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();

        string page = await GetPageAsync(app, "/");

        Assert.Contains("href=\"/Setup\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HomePage_OnConnectedInstance_DoesNotLinkToSetup()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create(Token);

        Assert.DoesNotContain("/Setup", await GetPageAsync(app, "/"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Setup_ExplainsClientId_ShowsRedirectUrl_AndRightsInWords()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();

        string page = await GetPageAsync(app, "/Setup");

        Assert.Contains("obchod@reservanto.cz", page, StringComparison.Ordinal);
        Assert.Contains("https://localhost/Auth/Callback", page, StringComparison.Ordinal);
        Assert.Contains("volné termíny", page, StringComparison.Ordinal);
        Assert.DoesNotContain("FreeSpace_r", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryRequestedRight_HasTextInWords()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();
        IConnectionSetup setup = app.Services.GetRequiredService<IConnectionSetup>();
        IStringLocalizer<SharedResource> text = app.Services.GetRequiredService<IStringLocalizer<SharedResource>>();

        Assert.NotEmpty(setup.BaseRights);
        Assert.NotEmpty(setup.OccupancyRights);
        Assert.All(setup.BaseRights.Concat(setup.OccupancyRights), right => Assert.False(text[$"Setup.Right.{right}"].ResourceNotFound, right));
    }

    [Theory]
    [InlineData(ConnectionState.NotConnected)]
    [InlineData(ConnectionState.Connected)]
    [InlineData(ConnectionState.TokenRejected)]
    [InlineData(ConnectionState.MissingRights)]
    [InlineData(ConnectionState.Unavailable)]
    public async Task EveryConnectionState_HasText(ConnectionState state)
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();
        IStringLocalizer<SharedResource> text = app.Services.GetRequiredService<IStringLocalizer<SharedResource>>();

        Assert.False(text[$"Setup.Status.{state}.Heading"].ResourceNotFound);
        Assert.False(text[state == ConnectionState.Connected ? "Setup.Status.Connected.Counts" : $"Setup.Status.{state}.Explain"].ResourceNotFound);
    }

    [Fact]
    public async Task ClientIdFromConfiguration_PrefillsField()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create(clientId: ConfiguredClient);

        Assert.Contains($"value=\"{ConfiguredClient}\"", await GetPageAsync(app, "/Setup"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, new[] { "BookingResource_r", "BookingService_r", "FreeSpace_r", "Location_r", "Merchant_r", "Segment_r" })]
    [InlineData(true, new[] { "BookingResource_r", "BookingService_r", "Event_r", "FreeSpace_r", "Location_r", "Merchant_r", "Segment_r" })]
    public async Task Connect_LinksToConsent_WithRightsAndCallback(bool occupancy, string[] rights)
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();
        using HttpClient client = TestApp.Client(app);

        using HttpResponseMessage response = await PostClientIdAsync(client, " klient-testu ", occupancy);

        Uri target = await ConsentLinkAsync(response);
        NameValueCollection query = HttpUtility.ParseQueryString(target.Query);
        Assert.StartsWith(TestApp.ApiAddress + "v1/Authorize/GetLongTimeToken?", target.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("klient-testu", query["clientId"]);
        Assert.Equal("https://localhost/Auth/Callback", query["redirectUrl"]);
        Assert.Equal(rights, query.GetValues("rights")!.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Setup_OffersOccupancy_UncheckedByDefault_AndExplainsIt()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();

        string page = await GetPageAsync(app, "/Setup");

        Assert.Matches("<input type=\"checkbox\" id=\"occupancy\" name=\"occupancy\" value=\"1\"\\s+aria-describedby", page);
        Assert.Contains("Ukazovat obsazenost na hodinové ose", page, StringComparison.Ordinal);
        Assert.Contains("rezervace, jen kvůli obsazeným časům", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connect_WithoutClientId_KeepsOccupancyChoice()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();
        using HttpClient client = TestApp.Client(app);

        using HttpResponseMessage response = await PostClientIdAsync(client, "", occupancy: true);

        Assert.Contains("value=\"1\" checked=\"checked\"", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "Obsazenost na hodinové ose je zapnutá.")]
    [InlineData(false, "Obsazenost na hodinové ose je vypnutá")]
    public async Task ConnectedInstance_SaysWhetherOccupancyIsShown(bool shows, string message)
    {
        var result = new ConnectionCheckResult(ConnectionState.Connected, PublicSegments: 4, PublicLocations: 2) { ShowsOccupancy = shows };
        await using WebApplicationFactory<Program> app = TestApp.Create(Token, check: result);

        Assert.Contains(message, await GetPageAsync(app, "/Setup"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    public async Task Connect_WithoutClientId_ShowsErrorAndNoConsentLink(string clientId)
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();
        using HttpClient client = TestApp.Client(app);

        using HttpResponseMessage response = await PostClientIdAsync(client, clientId);

        string page = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("Vyplňte ClientId", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-av-follow", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connect_WithoutForm_ShowsErrorInsteadOfFailing()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();
        using HttpClient client = TestApp.Client(app);
        using var json = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await client.PostAsync(new Uri("/Setup/Connect", UriKind.Relative), json, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Vyplňte ClientId", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnderPathBase_RedirectUrlKeepsIt()
    {
        // Pod IIS může aplikace běžet v podadresáři, registrovaná návratová adresa ho musí obsahovat.
        await using WebApplicationFactory<Program> app = TestApp.Create(services: TestApp.UnderPathBase("/app"));

        Assert.Contains("https://localhost/app/Auth/Callback", await GetPageAsync(app, "/app/Setup"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidTimeZone_StopsStart()
    {
        var settings = new Dictionary<string, string?> { ["AvailabilityView:TimeZone"] = "Mars/Olympus" };
        await using WebApplicationFactory<Program> app = TestApp.Create(settings: settings);

        Assert.ThrowsAny<Exception>(() => TestApp.Client(app));
    }

    [Fact]
    public async Task Connect_WithOverlongClientId_ShowsNoConsentLink()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();
        using HttpClient client = TestApp.Client(app);

        using HttpResponseMessage response = await PostClientIdAsync(client, new string('a', 201));

        Assert.DoesNotContain("data-av-follow", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectedInstance_DoesNotOfferConnection()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create(Token, clientId: ConfiguredClient);
        using HttpClient client = TestApp.Client(app);

        string page = await GetPageAsync(app, "/Setup");
        using HttpResponseMessage connect = await PostClientIdAsync(client, "klient-testu");

        Assert.DoesNotContain("<form", page, StringComparison.Ordinal);
        Assert.DoesNotContain(ConfiguredClient, page, StringComparison.Ordinal);
        Assert.Equal("/Setup", connect.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ConnectedInstance_ShowsAccountAndCounts()
    {
        var result = new ConnectionCheckResult(ConnectionState.Connected, PublicSegments: 4, PublicLocations: 2, MerchantName: "Studio <Testovací>");
        await using WebApplicationFactory<Program> app = TestApp.Create(Token, check: result);

        string page = await GetPageAsync(app, "/Setup");

        Assert.Contains("Připojený účet: <strong>Studio &lt;Testovací&gt;</strong>", page, StringComparison.Ordinal);
        Assert.Contains("vidí 4 veřejných zaměření a 2 veřejných provozoven", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectedInstance_WithoutAccountName_LeavesLineOut()
    {
        var result = new ConnectionCheckResult(ConnectionState.Connected, PublicSegments: 4, PublicLocations: 2);
        await using WebApplicationFactory<Program> app = TestApp.Create(Token, check: result);

        Assert.DoesNotContain("Připojený účet", await GetPageAsync(app, "/Setup"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectedInstance_ShowsAllowedDomains_AndLinksToGenerator()
    {
        // Krok 5 průvodce (SPEC sekce 6). Ukázka je platný JSON s klíčem konfigurace.
        var result = new ConnectionCheckResult(ConnectionState.Connected, PublicSegments: 4, PublicLocations: 2);
        await using WebApplicationFactory<Program> app = TestApp.Create(Token, check: result, services: TestApp.UnderPathBase("/volno"));

        string page = await GetPageAsync(app, "/volno/Setup");

        string sample = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(page, "id=\"allowed-origins\">(?<json>[^<]+)</pre>").Groups["json"].Value);
        using var json = System.Text.Json.JsonDocument.Parse(sample);
        Assert.Equal("https://www.vas-web.cz", json.RootElement.GetProperty("AvailabilityView").GetProperty("AllowedFrameOrigins")[0].GetString());
        Assert.Contains("data-av-copy=\"allowed-origins\"", page, StringComparison.Ordinal);
        Assert.Contains("href=\"/volno/Generator\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectedInstanceWithProblem_DoesNotOfferNextStep()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create(Token, check: new ConnectionCheckResult(ConnectionState.TokenRejected));

        string page = await GetPageAsync(app, "/Setup");

        Assert.DoesNotContain("AllowedFrameOrigins", page, StringComparison.Ordinal);
        Assert.DoesNotContain("/Generator", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ConnectionState.TokenRejected, "Reservanto token odmítlo")]
    [InlineData(ConnectionState.MissingRights, "Token nemá potřebná práva")]
    [InlineData(ConnectionState.Unavailable, "Reservanto teď neodpovídá")]
    public async Task ConnectedInstance_ExplainsProblem(ConnectionState state, string message)
    {
        await using WebApplicationFactory<Program> app = TestApp.Create(Token, check: new ConnectionCheckResult(state));

        string page = await GetPageAsync(app, "/Setup");

        Assert.Contains(message, page, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, page, StringComparison.Ordinal);
    }

    private static async Task<string> GetPageAsync(WebApplicationFactory<Program> app, string path)
    {
        using HttpClient client = TestApp.Client(app);
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private static async Task<Uri> ConsentLinkAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string page = await response.Content.ReadAsStringAsync(Ct);
        Match link = Regex.Match(page, "href=\"([^\"]+)\" data-av-follow");
        Assert.True(link.Success);
        return new Uri(HttpUtility.HtmlDecode(link.Groups[1].Value));
    }

    private static async Task<HttpResponseMessage> PostClientIdAsync(HttpClient client, string clientId, bool occupancy = false)
    {
        var fields = new Dictionary<string, string> { ["clientId"] = clientId };
        if (occupancy)
        {
            fields["occupancy"] = "1";
        }

        using var form = new FormUrlEncodedContent(fields);
        return await client.PostAsync(new Uri("/Setup/Connect", UriKind.Relative), form, Ct);
    }
}
