using AvailabilityView.Web.Security;
using AvailabilityView.Web.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AvailabilityView.Web.Tests;

/// <summary>Vlastní CSS merchanta z <c>AvailabilityView:CustomCssUrl</c>.</summary>
public sealed class CustomStyleTests
{
    private const string AppStyle = "<link rel=\"stylesheet\" href=\"/css/availability.css\">";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Průvodce, generátor a návrat ze souhlasu vlastní styl nikdy nenačtou.
    public static TheoryData<string> OtherPages => ["/Setup", "/Generator", "/Auth/Callback?accepted=0"];

    private static Dictionary<string, string?> Style(string url) => new() { ["AvailabilityView:CustomCssUrl"] = url };

    [Fact]
    public async Task VisitorPage_LoadsStyleAfterAppStyles_AndPolicyAllowsOnlyItsOrigin()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create(settings: Style("https://www.Kávárna.cz:8443/styly/web.css?v=2"));

        (string page, string policy) = await GetAsync(app, "/");

        int own = page.IndexOf(AppStyle, StringComparison.Ordinal);
        int custom = page.IndexOf("<link rel=\"stylesheet\" href=\"https://www.xn--kvrna-xqab.cz:8443/styly/web.css?v=2\">", StringComparison.Ordinal);
        Assert.True(own >= 0 && custom > own, "Vlastní styl má být za styly aplikace.");
        // Písma ze stejných míst jako styly (rozhodnutí 2026-11-09).
        Assert.Contains("; style-src 'self' https://www.xn--kvrna-xqab.cz:8443; font-src 'self' https://www.xn--kvrna-xqab.cz:8443; ", policy, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(OtherPages))]
    public async Task OtherPages_DoNotLoadStyle_NorAllowItsOrigin(string path)
    {
        await using WebApplicationFactory<Program> app = TestApp.Create(settings: Style("https://www.vas-web.cz/web.css"));

        (string page, string policy) = await GetAsync(app, path);

        Assert.DoesNotContain("vas-web.cz", page, StringComparison.Ordinal);
        Assert.Contains("; style-src 'self'; ", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("font-src", policy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PathOfInstance_IsBelowApplicationPath_AndNeedsNoOtherOrigin()
    {
        await using WebApplicationFactory<Program> app = TestApp.Create(settings: Style("/custom/web.css?v=2"), services: TestApp.UnderPathBase("/volno"));

        (string page, string policy) = await GetAsync(app, "/volno/");

        Assert.Contains("<link rel=\"stylesheet\" href=\"/volno/custom/web.css?v=2\">", page, StringComparison.Ordinal);
        Assert.Contains("; style-src 'self'; font-src 'self'; ", policy, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task WithoutStyle_PageHasOnlyAppStyles(string? url)
    {
        await using WebApplicationFactory<Program> app = TestApp.Create(settings: new Dictionary<string, string?> { ["AvailabilityView:CustomCssUrl"] = url });

        (string page, string policy) = await GetAsync(app, "/");

        Assert.Single(page.Split("rel=\"stylesheet\"")[1..]);
        Assert.Contains("; style-src 'self'; font-src 'self'; ", policy, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://www.vas-web.cz/web.css")]
    [InlineData("//www.vas-web.cz/web.css")]
    [InlineData("/\\www.vas-web.cz/web.css")]
    [InlineData("\\\\www.vas-web.cz\\web.css")]
    [InlineData("custom/web.css")]
    [InlineData("web.css")]
    [InlineData("https://jmeno:heslo@www.vas-web.cz/web.css")]
    [InlineData("https://[::1]/web.css")] // CSP adresy IPv6 nezná
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/css,body{}")]
    [InlineData("file:///etc/web.css")]
    [InlineData("https://www.vas-web.cz/web.css; script-src *")]
    [InlineData(" /custom/web.css")]
    [InlineData("/custom/web.css ")]
    [InlineData("/custom/my web.css")]
    [InlineData("/custom/web.css\n")]
    public async Task InvalidUrl_StopsStart(string url)
    {
        await using WebApplicationFactory<Program> app = TestApp.Create(settings: Style(url));

        Assert.ThrowsAny<Exception>(() => TestApp.Client(app));
    }

    [Theory]
    [InlineData("https://www.vas-web.cz/web.css", "https://www.vas-web.cz/web.css", "https://www.vas-web.cz")]
    [InlineData("https://WWW.vas-web.cz:443/styly/web.css", "https://www.vas-web.cz/styly/web.css", "https://www.vas-web.cz")]
    [InlineData("https://127.0.0.1:8443/web.css", "https://127.0.0.1:8443/web.css", "https://127.0.0.1:8443")]
    [InlineData("/custom/web.css", "/custom/web.css", null)]
    public void Url_IsParsed(string value, string url, string? origin)
    {
        Assert.Equal(new CustomStyle(url, origin), CustomStyle.Parse(value));
    }

    private static async Task<(string Page, string Policy)> GetAsync(WebApplicationFactory<Program> app, string path)
    {
        using HttpClient client = TestApp.Client(app);
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        return (await response.Content.ReadAsStringAsync(Ct), Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }
}
