using System.Globalization;
using System.Web;
using AvailabilityView.Web.E2E.Tests.Support;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;

namespace AvailabilityView.Web.E2E.Tests;

/// <summary>Průvodce v prohlížeči: souhlas je falešný, zbytek je skutečná aplikace s vynucenou CSP.</summary>
[Trait("Category", "E2E")]
[Collection(BrowserTestGroup.Name)]
public sealed class SetupBrowserTests(BrowserFixture browser)
{
    private const string Token = BrowserFixture.Token;
    private const string TestClient = "klient-testu";

    [Fact]
    public async Task Wizard_LeadsThroughConsent_AndForgetsTokenInAddress()
    {
        await using Session session = await Session.StartAsync(browser);

        await session.Page.GotoAsync(session.Url("/Setup"));
        await session.Page.GetByLabel("ClientId").FillAsync(TestClient);
        await session.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Pokračovat do Reservanta" }).ClickAsync();
        await session.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "3. Uložte token" }).WaitForAsync();

        Assert.Equal(TestClient, HttpUtility.ParseQueryString(browser.ConsentRequests.Last())["clientId"]);
        Assert.EndsWith("/Auth/Callback", session.Page.Url, StringComparison.Ordinal);
        Assert.Contains(Token, await session.Page.Locator("#token-instructions").InnerTextAsync(), StringComparison.Ordinal);
        session.AssertNoConsoleErrors();
    }

    [Fact]
    public async Task TokenPage_AfterReload_NoLongerShowsToken()
    {
        await using Session session = await Session.StartAsync(browser);
        await session.Page.GotoAsync(session.Url($"/Auth/Callback?accepted=1&token={Token}"));
        await session.Page.WaitForURLAsync(url => !url.Contains('?', StringComparison.Ordinal));

        await session.Page.ReloadAsync();

        Assert.DoesNotContain(Token, await session.Page.ContentAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TabHistory_DoesNotKeepToken()
    {
        await using Session session = await Session.StartAsync(browser);
        await session.Page.GotoAsync(session.Url("/Setup"));
        await session.Page.GotoAsync(session.Url($"/Auth/Callback?accepted=1&token={Token}"));
        await session.Page.WaitForURLAsync(url => !url.Contains('?', StringComparison.Ordinal));
        await session.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Ověřit připojení" }).ClickAsync();

        await session.Page.GoBackAsync();

        Assert.DoesNotContain(Token, session.Page.Url, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, await session.Page.ContentAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedConsent_AlsoForgetsAddress()
    {
        await using Session session = await Session.StartAsync(browser);

        await session.Page.GotoAsync(session.Url("/Auth/Callback?accepted=1&token=%3Cskript%3E"));

        await session.Page.WaitForURLAsync(url => !url.Contains('?', StringComparison.Ordinal));
    }

    [Fact]
    public async Task CopyButton_CopiesInstructionsWithToken()
    {
        await using Session session = await Session.StartAsync(browser);
        await session.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await session.Page.GotoAsync(session.Url($"/Auth/Callback?accepted=1&token={Token}"));

        await session.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Zkopírovat" }).ClickAsync();
        await session.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Zkopírováno" }).WaitForAsync();

        Assert.Contains(Token, await session.Page.EvaluateAsync<string>("navigator.clipboard.readText()"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Setup_CannotBeEmbeddedFromOtherSite()
    {
        await using Session session = await Session.StartAsync(browser);
        // Cizí web je skutečný server (MerchantSite): podvržená odpověď by rám nechala zablokovat kontrole lokální sítě
        // v Chromiu a test by prošel i bez CSP.
        await session.Page.GotoAsync(browser.Foreign.Serve("setup", $"""<!DOCTYPE html><iframe src="{session.Url("/Setup")}"></iframe>"""));
        await session.Page.WaitForTimeoutAsync(1000);

        IFrame frame = session.Page.Frames.Single(candidate => candidate != session.Page.MainFrame);
        Assert.DoesNotContain("Připojení k Reservantu", await SafeContentAsync(frame), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/Setup")]
    [InlineData($"/Auth/Callback?accepted=1&token={Token}")]
    [InlineData("/Auth/Callback?accepted=0")]
    public async Task Pages_HaveNoAccessibilityViolations(string path)
    {
        await using Session session = await Session.StartAsync(browser);
        await session.Page.GotoAsync(session.Url(path));

        AxeResult result = await session.Page.RunAxe();

        Assert.Empty(result.Violations.Select(violation => string.Create(CultureInfo.InvariantCulture, $"{violation.Id}: {violation.Help}")));
    }

    // Zablokovaný rámec je chybová stránka prohlížeče z jiného původu, její obsah nemusí jít přečíst.
    private static async Task<string> SafeContentAsync(IFrame frame)
    {
        try
        {
            return await frame.ContentAsync();
        }
        catch (PlaywrightException)
        {
            return string.Empty;
        }
    }

    /// <summary>Izolovaný kontext a stránka se sledováním chyb v konzoli, včetně porušení CSP.</summary>
    private sealed class Session : IAsyncDisposable
    {
        private readonly BrowserFixture _browser;
        private readonly List<string> _errors = [];

        private Session(BrowserFixture browser, IBrowserContext context, IPage page)
        {
            _browser = browser;
            Context = context;
            Page = page;
            Page.Console += (_, message) =>
            {
                if (message.Type == "error")
                {
                    _errors.Add(message.Text);
                }
            };
        }

        public IBrowserContext Context { get; }

        public IPage Page { get; }

        public static async Task<Session> StartAsync(BrowserFixture browser)
        {
            IBrowserContext context = await browser.NewContextAsync();
            return new Session(browser, context, await context.NewPageAsync());
        }

        public string Url(string path) => new Uri(_browser.Address, path).AbsoluteUri;

        public void AssertNoConsoleErrors() => Assert.Empty(_errors);

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
