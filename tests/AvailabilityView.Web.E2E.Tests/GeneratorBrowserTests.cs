using System.Globalization;
using System.Text.RegularExpressions;
using AvailabilityView.Web.E2E.Tests.Support;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Session = AvailabilityView.Web.E2E.Tests.VisitorBrowserTests.Session;

namespace AvailabilityView.Web.E2E.Tests;

/// <summary>
/// Generátor v prohlížeči s vynucenou CSP: s JavaScriptem i bez něj, náhled v iframe,
/// zkopírování odkazu, přístupnost a úzká šířka.
/// </summary>
[Trait("Category", "E2E")]
[Collection(GeneratorTestGroup.Name)]
public sealed class GeneratorBrowserTests(VisitorFixture app)
{
    private const int Wide = 1280;
    private const int Narrow = 320;

    public static TheoryData<string, int> Pages => new()
    {
        { "/Generator", Wide },
        { "/Generator", Narrow },
        { "/Generator?view=month&also=calendar&segment=2&pickers=service&days=30&lang=en", Wide },
        { "/Generator?view=month&also=calendar&segment=2&pickers=service&days=30&lang=en", Narrow },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Generator_HasNoAccessibilityViolations_NorScroll_NorConsoleErrors(string path, int width)
    {
        await using Session session = await Session.StartAsync(app, width);
        await session.Page.GotoAsync(app.Url(path));
        await Assertions.Expect(Preview(session).Locator("main")).ToBeVisibleAsync();

        AxeResult result = await session.Page.RunAxe();
        int overflow = await session.Page.EvaluateAsync<int>("document.documentElement.scrollWidth - document.documentElement.clientWidth");

        string[] violations = [.. result.Violations.SelectMany(violation => violation.Nodes.Select(node => string.Create(
            CultureInfo.InvariantCulture,
            $"{violation.Id} {node.Target}: {string.Join("; ", node.Any.Concat(node.All).Concat(node.None).Select(check => check.Message))}")))];
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
        Assert.Equal(0, overflow);
        session.AssertNoConsoleErrors();
    }

    [Fact]
    public async Task WithJavaScript_ChoiceGoesToPreviewAndLink_AndLinkCopies()
    {
        await using Session session = await Session.StartAsync(app, Wide);
        await session.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"], new() { Origin = app.Url("/").TrimEnd('/') });
        await session.Page.GotoAsync(app.Url("/Generator"));

        await session.Page.Locator("#generator-segment").SelectOptionAsync("2");

        await Assertions.Expect(session.Page).ToHaveURLAsync(new Regex("segment=2"));
        await Assertions.Expect(session.Page.Locator("#generator-link")).ToHaveTextAsync(new Regex("/\\?segment=2&location=20$"));
        await Assertions.Expect(Preview(session).Locator(".av-slot__service").First).ToContainTextAsync("Jóga");

        await session.Page.Locator("#generator-also-calendar").CheckAsync();

        await Assertions.Expect(session.Page).ToHaveURLAsync(new Regex("also=calendar"));
        string link = await session.Page.Locator("#generator-link").InnerTextAsync();
        Assert.EndsWith("/?view=agenda,calendar&segment=2&location=20", link, StringComparison.Ordinal);
        await Assertions.Expect(Preview(session).Locator(".av-views__link")).ToHaveCountAsync(2);

        await session.Page.Locator("[data-av-copy=generator-link]").ClickAsync();

        await Assertions.Expect(session.Page.Locator("[data-av-copy=generator-link]")).ToHaveTextAsync("Zkopírováno");
        Assert.Equal(link, await session.Page.EvaluateAsync<string>("navigator.clipboard.readText()"));

        // Kód pro vložení se zkopíruje jako text, ne jako HTML stránky generátoru.
        await session.Page.Locator("[data-av-copy=generator-code]").ClickAsync();

        await Assertions.Expect(session.Page.Locator("[data-av-copy=generator-code]")).ToHaveTextAsync("Zkopírováno");
        // Schránka ve Windows má konce řádků CRLF.
        string code = (await session.Page.EvaluateAsync<string>("navigator.clipboard.readText()")).ReplaceLineEndings("\n");
        Assert.Equal(await session.Page.Locator("#generator-code").TextContentAsync(), code);
        Assert.StartsWith($"<iframe src=\"{link.Replace("&", "&amp;", StringComparison.Ordinal)}\"", code, StringComparison.Ordinal);
        Assert.Contains($"<script src=\"{app.Url("/embed.js")}\" async></script>", code, StringComparison.Ordinal);
        session.AssertNoConsoleErrors();
    }

    [Fact]
    public async Task WithJavaScript_ChangedDays_GoToPreviewAndLink()
    {
        // Počet dní je číselné pole, ne seznam. Po opuštění pole se formulář odešle.
        await using Session session = await Session.StartAsync(app, Wide);
        await session.Page.GotoAsync(app.Url("/Generator?segment=2"));

        await session.Page.Locator("#generator-days").FillAsync("30");
        await session.Page.Locator("#generator-days").PressAsync("Tab");

        await Assertions.Expect(session.Page).ToHaveURLAsync(new Regex("days=30"));
        await Assertions.Expect(session.Page.Locator("#generator-link")).ToHaveTextAsync(new Regex("days=30"));
        Assert.Contains("days=30", await session.Page.Locator(".av-generator__preview").GetAttributeAsync("src") ?? string.Empty, StringComparison.Ordinal);

        // Šipka z klávesnice stránku hned nenačte, uživatel může pokračovat. Odešle se po opuštění pole.
        string start = session.Page.Url;
        await session.Page.Locator("#generator-days").FocusAsync();
        await session.Page.Keyboard.PressAsync("ArrowUp");
        await session.Page.WaitForTimeoutAsync(500);
        Assert.Equal(start, session.Page.Url);
        await session.Page.Keyboard.PressAsync("ArrowUp");
        await session.Page.Keyboard.PressAsync("Tab");

        await Assertions.Expect(session.Page).ToHaveURLAsync(new Regex("days=32"));
        session.AssertNoConsoleErrors();
    }

    [Fact]
    public async Task NavigationThatDidNotHappen_DoesNotBlockNextChange()
    {
        // Odeslání, po kterém stránka zůstane (uživatel načítání zastaví, server vrátí 204), nesmí zablokovat další
        // odeslání. Odpověď 204 prohlížeč nechá na původní stránce stejně jako zastavení.
        await using Session session = await Session.StartAsync(app, Wide);
        await session.Page.GotoAsync(app.Url("/Generator?segment=2"));
        int stopped = 0;
        await session.Page.RouteAsync(new Regex("days=30"), async route =>
        {
            if (Interlocked.Increment(ref stopped) == 1)
            {
                await route.FulfillAsync(new() { Status = 204 });
                return;
            }

            await route.ContinueAsync();
        });

        await session.Page.Locator("#generator-days").FillAsync("30");
        await session.Page.Locator("#generator-days").PressAsync("Tab");
        await session.Page.WaitForTimeoutAsync(500);
        Assert.Equal(1, stopped);
        Assert.DoesNotContain("days=30", session.Page.Url, StringComparison.Ordinal);
        await session.Page.Locator("#generator-also-calendar").CheckAsync();

        await Assertions.Expect(session.Page).ToHaveURLAsync(new Regex("also=calendar"));
    }

    [Fact]
    public async Task Link_OpensExactlyThePreview()
    {
        await using Session session = await Session.StartAsync(app, Wide);
        await session.Page.GotoAsync(app.Url("/Generator?segment=2&view=calendar&also=agenda&pickers=service"));
        string preview = await Preview(session).Locator("main").InnerTextAsync();
        string link = await session.Page.Locator("#generator-link").InnerTextAsync();

        await session.Page.GotoAsync(link);

        Assert.Equal(preview, await session.Page.Locator("main").InnerTextAsync());
        session.AssertNoConsoleErrors();
    }

    [Fact]
    public async Task WithoutJavaScript_ButtonSubmits()
    {
        await using Session session = await Session.StartAsync(app, Wide, javaScript: false);
        string start = app.Url("/Generator");
        await session.Page.GotoAsync(start);

        await session.Page.Locator("#generator-segment").SelectOptionAsync("2");
        await session.Page.WaitForTimeoutAsync(500);
        Assert.Equal(start, session.Page.Url);

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Zobrazit náhled" }).ClickAsync();

        await Assertions.Expect(session.Page.Locator("#generator-link")).ToHaveTextAsync(new Regex("/\\?segment=2&location=20$"));
        await Assertions.Expect(Preview(session).Locator(".av-slot__service").First).ToContainTextAsync("Jóga");
    }

    [Fact]
    public async Task Keyboard_ArrowChangesChoiceWithoutSubmit()
    {
        await using Session session = await Session.StartAsync(app, Wide);
        string start = app.Url("/Generator");
        await session.Page.GotoAsync(start);

        await session.Page.Locator("#generator-segment").FocusAsync();
        await session.Page.Keyboard.PressAsync("ArrowDown");
        await session.Page.WaitForTimeoutAsync(500);

        Assert.Equal(start, session.Page.Url);
    }

    private static IFrameLocator Preview(Session session) => session.Page.FrameLocator(".av-generator__preview");
}
