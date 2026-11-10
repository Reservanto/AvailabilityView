using AvailabilityView.Web.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AvailabilityView.Web.Tests;

/// <summary>Hlavičky a cookies na všech adresách (<c>docs/SPEC.md</c> sekce 1 a 10).</summary>
public sealed class SecurityHeadersTests
{
    private const string CallbackWithToken = "/Auth/Callback?accepted=1&token=ltt-testu-0123456789";
    private const string TestClient = "klient-testu";

    public static TheoryData<string> Pages => ["/", "/?segment=999", "/Setup", "/Generator", "/Auth/Callback", CallbackWithToken, "/neexistuje"];

    // Průvodce, generátor a návrat ze souhlasu nejde vložit nikdy.
    public static TheoryData<string> NeverFramed => ["/Setup", "/Generator", "/Auth/Callback", CallbackWithToken, "/neexistuje"];

    // Stránka návštěvníka i s hláškou, zatím jen z vlastní instance kvůli náhledu v generátoru.
    public static TheoryData<string> VisitorPages => ["/", "/?segment=999", "/?view=month"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task EveryResponse_HasStrictHeaders_AndNoCookie(string path)
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();
        using HttpClient client = TestApp.Client(app);

        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        string policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.DoesNotContain("unsafe-inline", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", policy, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", policy, StringComparison.Ordinal);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [MemberData(nameof(NeverFramed))]
    public async Task OtherPages_CannotBeFramed(string path)
    {
        Assert.EndsWith("frame-ancestors 'none'", await PolicyAsync(path), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(VisitorPages))]
    public async Task VisitorPage_CanBeFramedByThisInstance(string path)
    {
        Assert.EndsWith("frame-ancestors 'self'", await PolicyAsync(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pages_CanFrameOnlyThisInstance()
    {
        Assert.Contains("frame-src 'self';", await PolicyAsync("/Generator"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ErrorPage_KeepsSecurityHeaders()
    {
        // Stránka s chybou ve vývoji vymaže hlavičky nastavené před ní.
        await using WebApplicationFactory<Program> app = TestApp.Create(environment: "Development", services: collection =>
            collection.AddSingleton<IStartupFilter, ThrowingPath>());
        using HttpClient client = TestApp.Client(app);

        using HttpResponseMessage response = await client.GetAsync(new Uri("/vybuch", UriKind.Relative), Ct);

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("frame-ancestors 'none'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    [Fact]
    public async Task FormPost_SetsNoCookie()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();
        using HttpClient client = TestApp.Client(app);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["clientId"] = TestClient });

        using HttpResponseMessage response = await client.PostAsync(new Uri("/Setup/Connect", UriKind.Relative), form, Ct);

        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Forms_CanTargetOnlyThisSite()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();
        using HttpClient client = TestApp.Client(app);

        using HttpResponseMessage response = await client.GetAsync(new Uri("/Setup", UriKind.Relative), Ct);

        Assert.Contains("form-action 'self';", Assert.Single(response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScriptOnSetup_UsesNonceFromPolicy_AndNonceChanges()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();
        using HttpClient client = TestApp.Client(app);

        string first = await NonceInPolicyAsync(client, assertInPage: true);
        string second = await NonceInPolicyAsync(client, assertInPage: false);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task SetupAssets_AreServed()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();
        using HttpClient client = TestApp.Client(app);

        using HttpResponseMessage script = await client.GetAsync(new Uri("/js/setup.js", UriKind.Relative), Ct);
        using HttpResponseMessage style = await client.GetAsync(new Uri("/css/setup.css", UriKind.Relative), Ct);

        Assert.True(script.IsSuccessStatusCode);
        Assert.True(style.IsSuccessStatusCode);
    }

    private static async Task<string> PolicyAsync(string path)
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();
        using HttpClient client = TestApp.Client(app);
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        return Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
    }

    private sealed class ThrowingPath : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use((HttpContext context, RequestDelegate following) =>
                context.Request.Path == "/vybuch" ? throw new InvalidOperationException("test") : following(context));
        };
    }

    private static async Task<string> NonceInPolicyAsync(HttpClient client, bool assertInPage)
    {
        using HttpResponseMessage response = await client.GetAsync(new Uri("/Setup", UriKind.Relative), Ct);
        string policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        const string Prefix = "'nonce-";
        int start = policy.IndexOf(Prefix, StringComparison.Ordinal) + Prefix.Length;
        string nonce = policy[start..policy.IndexOf('\'', start)];
        if (assertInPage)
        {
            Assert.Contains($"nonce=\"{nonce}\"", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }

        return nonce;
    }
}
