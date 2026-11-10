using System.Net;
using AvailabilityView.Application.Availability;
using AvailabilityView.Web.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AvailabilityView.Web.Tests;

/// <summary>Omezení počtu požadavků podle IP.</summary>
public sealed class RequestLimitTests
{
    private const int Limit = 120;
    private const string IpHeader = "X-Test-Client-Ip";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OverLimit_Returns429_WithMessage_AndDoesNotRead()
    {
        var reader = new CountingReader();
        await using WebApplicationFactory<Program> app = Create(reader);
        using HttpClient client = Client(app, "203.0.113.1");

        await AssertAllOkAsync(client, "/", Limit);
        using HttpResponseMessage response = await client.GetAsync(new Uri("/", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(Limit, reader.Calls);
        string page = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("<p class=\"av-message av-message--error\">Stránku teď otevíráte příliš často. Zkuste to prosím za minutu.</p>", page, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"cs\">", page, StringComparison.Ordinal);
        Assert.Contains("href=\"/css/availability.css\"", page, StringComparison.Ordinal);
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        // Okno je minuta a začalo prvním požadavkem před chvílí: zbývá skoro celé.
        Assert.True(int.Parse(Assert.Single(response.Headers.GetValues("Retry-After")), System.Globalization.CultureInfo.InvariantCulture) is >= 30 and <= 60);
        // Hláška patří ke stránce návštěvníka a jde vložit jako ona.
        Assert.EndsWith("frame-ancestors 'self'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task OtherIp_IsNotLimited()
    {
        await using WebApplicationFactory<Program> app = Create(new CountingReader());
        using HttpClient first = Client(app, "203.0.113.1");
        using HttpClient second = Client(app, "203.0.113.2");

        await AssertAllOkAsync(first, "/", Limit);
        using HttpResponseMessage limited = await first.GetAsync(new Uri("/", UriKind.Relative), Ct);
        using HttpResponseMessage other = await second.GetAsync(new Uri("/", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task Ipv4MappedToIpv6_IsSameAddress()
    {
        await using WebApplicationFactory<Program> app = Create(new CountingReader());
        using HttpClient ipv4 = Client(app, "203.0.113.1");
        using HttpClient mapped = Client(app, "::ffff:203.0.113.1");

        await AssertAllOkAsync(ipv4, "/", Limit);
        using HttpResponseMessage response = await mapped.GetAsync(new Uri("/", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task StaticFiles_AreNotCounted()
    {
        await using WebApplicationFactory<Program> app = Create(new CountingReader());
        using HttpClient client = Client(app, "203.0.113.1");

        await AssertAllOkAsync(client, "/css/availability.css", Limit * 2);
        await AssertAllOkAsync(client, "/", Limit);
    }

    [Theory]
    [InlineData("/Setup")]
    [InlineData("/neexistuje")]
    public async Task OtherPages_CountToo(string path)
    {
        await using WebApplicationFactory<Program> app = Create(new CountingReader());
        using HttpClient client = Client(app, "203.0.113.1");

        for (int i = 0; i < Limit; i++)
        {
            using HttpResponseMessage allowed = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        using HttpResponseMessage response = await client.GetAsync(new Uri("/", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Message_SpeaksLanguageOfPage()
    {
        await using WebApplicationFactory<Program> app = Create(new CountingReader());
        using HttpClient client = Client(app, "203.0.113.1");

        await AssertAllOkAsync(client, "/?lang=en", Limit);
        using HttpResponseMessage visitor = await client.GetAsync(new Uri("/?lang=en", UriKind.Relative), Ct);
        using HttpResponseMessage setup = await client.GetAsync(new Uri("/Setup?lang=en", UriKind.Relative), Ct);

        string page = await visitor.Content.ReadAsStringAsync(Ct);
        Assert.Contains("<html lang=\"en\">", page, StringComparison.Ordinal);
        Assert.Contains("You are opening this page too often. Please try again in a minute.", page, StringComparison.Ordinal);
        Assert.Contains("<title>Free slots</title>", page, StringComparison.Ordinal);
        Assert.Contains("Stránku teď otevíráte příliš často.", await setup.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnderPathBase_LinksStylesheetBelowIt()
    {
        await using WebApplicationFactory<Program> app = Create(new CountingReader(), TestApp.UnderPathBase("/volno"));
        using HttpClient client = Client(app, "203.0.113.1");

        await AssertAllOkAsync(client, "/volno/", Limit);
        using HttpResponseMessage response = await client.GetAsync(new Uri("/volno/", UriKind.Relative), Ct);

        Assert.Contains("href=\"/volno/css/availability.css\"", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LimitPage_HasLayoutOfVisitorPage_WithCustomStyle_OnlyForVisitorPage()
    {
        await using WebApplicationFactory<Program> app = Create(new CountingReader(), settings: new Dictionary<string, string?>
        {
            ["AvailabilityView:CustomCssUrl"] = "https://www.vas-web.cz/web.css",
        });
        using HttpClient client = Client(app, "203.0.113.1");

        await AssertAllOkAsync(client, "/", Limit);
        using HttpResponseMessage visitor = await client.GetAsync(new Uri("/", UriKind.Relative), Ct);
        using HttpResponseMessage setup = await client.GetAsync(new Uri("/Setup", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, visitor.StatusCode);
        string page = await visitor.Content.ReadAsStringAsync(Ct);
        Assert.Contains("<link rel=\"stylesheet\" href=\"https://www.vas-web.cz/web.css\">", page, StringComparison.Ordinal);
        Assert.Contains("style-src 'self' https://www.vas-web.cz; font-src 'self' https://www.vas-web.cz;", Assert.Single(visitor.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        // Skript stránky návštěvníka posílá výšku i z hlášky ve vloženém rámu.
        Assert.Contains("src=\"/js/availability.js\"", page, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.TooManyRequests, setup.StatusCode);
        Assert.DoesNotContain("vas-web.cz", await setup.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Contains("style-src 'self';", Assert.Single(setup.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
    }

    private static async Task AssertAllOkAsync(HttpClient client, string path, int count)
    {
        for (int i = 0; i < count; i++)
        {
            using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private static WebApplicationFactory<Program> Create(
        CountingReader reader, Action<IServiceCollection>? more = null, IReadOnlyDictionary<string, string?>? settings = null) =>
        TestApp.Create(token: "ltt-testu", settings: settings, services: services =>
        {
            services.RemoveAll<IAvailabilityReader>();
            services.AddSingleton<IAvailabilityReader>(reader);
            services.AddSingleton<IStartupFilter, ClientIpFromHeader>();
            more?.Invoke(services);
        });

    private static HttpClient Client(WebApplicationFactory<Program> app, string ip)
    {
        HttpClient client = TestApp.Client(app);
        client.DefaultRequestHeaders.Add(IpHeader, ip);
        return client;
    }

    // Testovací server adresu klienta nenastavuje. Tady ji určí hlavička, jen v testu.
    private sealed class ClientIpFromHeader : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext context, RequestDelegate following) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(context.Request.Headers[IpHeader].ToString());
                return following(context);
            });
            next(app);
        };
    }

    private sealed class CountingReader : IAvailabilityReader
    {
        private int _calls;

        public int Calls => _calls;

        public Task<AvailabilityResult> ReadAsync(AvailabilityQuery query, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(AvailabilityResult.Without(AvailabilityOutcome.NothingFree));
        }

        public Task<AvailabilityOptions> OptionsAsync(AvailabilityQuery query, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new AvailabilityOptions(AvailabilityOutcome.NotConnected, null, []));
        }
    }
}
