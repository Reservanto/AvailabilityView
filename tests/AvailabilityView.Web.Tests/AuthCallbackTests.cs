using System.Net;
using AvailabilityView.Web.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AvailabilityView.Web.Tests;

/// <summary>Návrat ze souhlasu (<c>docs/SPEC.md</c> sekce 6 krok 3 a sekce 10).</summary>
public sealed class AuthCallbackTests
{
    private const string Token = "ltt-testu-0123456789";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Accepted_OnServer_ShowsSettingsWithToken()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();

        string page = await GetAsync(app, $"/Auth/Callback?accepted=1&token={Token}");

        Assert.Contains("appsettings.Production.json", page, StringComparison.Ordinal);
        Assert.Contains($"&quot;{Token}&quot;", page, StringComparison.Ordinal);
        Assert.Contains("&quot;Reservanto&quot;", page, StringComparison.Ordinal);
        Assert.DoesNotContain("user-secrets", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Accepted_InDevelopment_ShowsUserSecretsCommand()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create(environment: "Development");

        string page = await GetAsync(app, $"/Auth/Callback?accepted=1&token={Token}");

        Assert.Contains($"dotnet user-secrets set &quot;Reservanto:LongTimeToken&quot; &quot;{Token}&quot;", page, StringComparison.Ordinal);
        // Složka user-secrets, která při startu neexistovala, se za běhu nesleduje.
        Assert.Contains("restartujte", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TokenPage_MarksAddressToForget_AndHasCopyButton()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();

        string page = await GetAsync(app, $"/Auth/Callback?accepted=1&token={Token}");

        Assert.Contains("data-av-forget-address", page, StringComparison.Ordinal);
        Assert.Contains("data-av-copy=\"token-instructions\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TokenPage_IsNotStored_AndSendsNoReferrer()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();
        using HttpClient client = TestApp.Client(app);

        using HttpResponseMessage response = await client.GetAsync(new Uri($"/Auth/Callback?accepted=1&token={Token}", UriKind.Relative), Ct);

        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    [Theory]
    [InlineData("/Auth/Callback?accepted=0")]
    [InlineData("/Auth/Callback")]
    [InlineData($"/Auth/Callback?accepted=0&token={Token}")]
    [InlineData($"/Auth/Callback?accepted=true&token={Token}")]
    public async Task NotAccepted_SaysSo_AndDoesNotShowToken(string path)
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();

        string page = await GetAsync(app, path);

        Assert.Contains("Přístup nebyl povolen", page, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/Auth/Callback?accepted=1")]
    [InlineData("/Auth/Callback?accepted=1&token=")]
    [InlineData("/Auth/Callback?accepted=1&token=kratky")]
    [InlineData("/Auth/Callback?accepted=1&token=abcdef0123456789%22%3B%20del%20x%3B%20%22")]
    [InlineData("/Auth/Callback?accepted=1&token=abcdef0123456789%24(whoami)")]
    [InlineData("/Auth/Callback?accepted=1&token=%3Cscript%3Ealert(1)%3C%2Fscript%3Eabcdef")]
    [InlineData($"/Auth/Callback?accepted=1&token={Token}&token=dalsi-token-0123456789")]
    public async Task InvalidToken_SaysSo_AndDoesNotEchoAnything(string path)
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();

        string page = await GetAsync(app, path);

        Assert.Contains("Token se nepodařilo přečíst", page, StringComparison.Ordinal);
        Assert.DoesNotContain("whoami", page, StringComparison.Ordinal);
        Assert.DoesNotContain("alert", page, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdef0123456789", page, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, page, StringComparison.Ordinal);
        Assert.DoesNotContain("user-secrets", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectedInstance_NeverShowsTokenFromAddress()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create("ltt-ulozeny-0123456789");
        using HttpClient client = TestApp.Client(app);

        using HttpResponseMessage response = await client.GetAsync(new Uri($"/Auth/Callback?accepted=1&token={Token}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Setup", response.Headers.Location!.OriginalString);
        Assert.DoesNotContain(Token, await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/auth/callback")]
    [InlineData("/AUTH/CALLBACK/")]
    public async Task OtherSpellingOfAddress_StillWorks(string path)
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();

        Assert.Contains(Token, await GetAsync(app, $"{path}?accepted=1&token={Token}"), StringComparison.Ordinal);
    }

    private static async Task<string> GetAsync(WebApplicationFactory<Program> app, string path)
    {
        using HttpClient client = TestApp.Client(app);
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }
}
