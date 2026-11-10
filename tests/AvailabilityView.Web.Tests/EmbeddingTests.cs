using System.Net;
using AvailabilityView.Web.Security;
using AvailabilityView.Web.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AvailabilityView.Web.Tests;

/// <summary>Vložení přes iframe z povolených domén a skript pro web merchanta.</summary>
public sealed class EmbeddingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> Origins(params string[] origins) =>
        origins.Select((origin, index) => (origin, index)).ToDictionary(item => $"AvailabilityView:AllowedFrameOrigins:{item.index}", item => (string?)item.origin);

    [Fact]
    public async Task VisitorPage_CanBeFramedByAllowedOrigins_OtherPagesNever()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create(settings: Origins("https://www.vas-web.cz", "HTTPS://Druhy.cz:8443/", "https://www.vas-web.cz/"));

        Assert.EndsWith("frame-ancestors 'self' https://www.vas-web.cz https://druhy.cz:8443", await PolicyAsync(app, "/?segment=999"), StringComparison.Ordinal);
        Assert.EndsWith("frame-ancestors 'none'", await PolicyAsync(app, "/Setup"), StringComparison.Ordinal);
        Assert.EndsWith("frame-ancestors 'none'", await PolicyAsync(app, "/Generator"), StringComparison.Ordinal);
        Assert.EndsWith("frame-ancestors 'none'", await PolicyAsync(app, "/Auth/Callback?accepted=0"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("www.vas-web.cz")]
    [InlineData("https://*.vas-web.cz")]
    [InlineData("*")]
    [InlineData("'self'")]
    [InlineData("https://vas-web.cz/rezervace")]
    [InlineData("https://vas-web.cz?x=1")]
    [InlineData("https://vas-web.cz/?")]
    [InlineData("https://vas-web.cz#kotva")]
    [InlineData("https://vas-web.cz/#")]
    [InlineData("https://jmeno@vas-web.cz")]
    [InlineData("ftp://vas-web.cz")]
    [InlineData("https://[::1]:8443")] // CSP adresy IPv6 nezná
    [InlineData("javascript:alert(1)")]
    [InlineData("https://vas-web.cz; script-src *")]
    [InlineData("https://vas-web.cz https://jiny.cz")]
    [InlineData("https://vas-web.cz,https://jiny.cz")]
    [InlineData(" https://vas-web.cz")]
    [InlineData("")]
    public async Task InvalidOrigin_StopsStart(string origin)
    {
        await using WebApplicationFactory<Program> app = TestApp.Create(settings: Origins("https://www.vas-web.cz", origin));

        Assert.ThrowsAny<Exception>(() => TestApp.Client(app));
    }

    [Theory]
    [InlineData("https://www.vas-web.cz", "https://www.vas-web.cz")]
    [InlineData("https://www.vas-web.cz/", "https://www.vas-web.cz")]
    [InlineData("https://www.vas-web.cz:443", "https://www.vas-web.cz")]
    [InlineData("http://localhost:3000", "http://localhost:3000")]
    [InlineData("https://127.0.0.1:8443", "https://127.0.0.1:8443")]
    [InlineData("https://www.Kávárna.cz", "https://www.xn--kvrna-xqab.cz")]
    public void Origin_IsNormalized(string value, string expected)
    {
        Assert.Equal(expected, FrameOrigin.Normalize(value));
    }

    [Fact]
    public async Task EmbedScript_IsServedFromInstanceRoot_AlsoUnderIisApplication()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create();
        await using WebApplicationFactory<Program> underIis = TestApp.Create(services: TestApp.UnderPathBase("/volno"));

        foreach ((WebApplicationFactory<Program> instance, string path) in new[] { (app, "/embed.js"), (underIis, "/volno/embed.js") })
        {
            using HttpClient client = TestApp.Client(instance);
            using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("av:height", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }
    }

    private static async Task<string> PolicyAsync(WebApplicationFactory<Program> app, string path)
    {
        using HttpClient client = TestApp.Client(app);
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        return Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
    }
}
