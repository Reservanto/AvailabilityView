using System.Globalization;
using AvailabilityView.Web.E2E.Tests.Support;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Session = AvailabilityView.Web.E2E.Tests.VisitorBrowserTests.Session;

namespace AvailabilityView.Web.E2E.Tests;

/// <summary>
/// Stránka vložená do webu merchanta na jiném originu. Web merchanta je skutečný server na
/// jiném portu (<see cref="MerchantSite"/>), prohlížeč u něj hlídá <c>frame-ancestors</c>.
/// </summary>
[Trait("Category", "E2E")]
[Collection(GeneratorTestGroup.Name)]
public sealed class EmbedBrowserTests(VisitorFixture app)
{
    private const string Lessons = "/?segment=2&view=calendar,agenda&pickers=service";

    // Nová zobrazení a osa ve vložené stránce.
    public static TheoryData<string, int> EmbeddedPages => new()
    {
        { "/?segment=2&view=month,calendar&days=60&pickers=service", 320 },
        { "/?segment=2&view=month,calendar&days=60&pickers=service&lang=en", 1024 },
        { "/?pickers=service,resource&view=timegrid,agenda", 320 },
        { "/?segment=2&view=next,agenda", 320 },
    };

    [Fact]
    public async Task AllowedOrigin_EmbedsPage_AndHeightFollowsContent()
    {
        await using Session session = await Session.StartAsync(app, 1024);
        await session.Page.GotoAsync(app.Allowed.Serve("vlozeni", Embed(Lessons, script: true)));
        IFrameLocator frame = session.Page.FrameLocator("#av");
        await Assertions.Expect(frame.Locator(".av-nav__title")).ToBeVisibleAsync();
        string firstWeek = await frame.Locator(".av-nav__title").InnerTextAsync();
        await AssertHeightFollowsContentAsync(session);

        // Přepnutí týdne a zobrazení načte stránku v rámu znovu, výška se musí změnit s ní.
        await frame.Locator(".av-nav > :last-child").ClickAsync();
        await Assertions.Expect(frame.Locator(".av-nav__title")).Not.ToHaveTextAsync(firstWeek);
        await AssertHeightFollowsContentAsync(session);
        double week = await FrameHeightAsync(session);

        await frame.Locator(".av-views__link:not([aria-current])").ClickAsync();
        await Assertions.Expect(frame.Locator(".av-nav__title")).ToHaveCountAsync(0);
        await AssertHeightFollowsContentAsync(session);
        Assert.NotEqual(week, await FrameHeightAsync(session));

        // Filtr odešle formulář skriptem (requestSubmit), ne odkazem. Výška musí sledovat i tuto stránku.
        IFrame content = (await (await session.Page.QuerySelectorAsync("#av"))!.ContentFrameAsync())!;
        await frame.Locator("#av-filter-service").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        for (int attempt = 0; attempt < 50 && !content.Url.Contains("service=", StringComparison.Ordinal); attempt++)
        {
            await session.Page.WaitForTimeoutAsync(100);
        }

        Assert.Contains("service=", content.Url, StringComparison.Ordinal);
        await AssertHeightFollowsContentAsync(session);
        session.AssertNoConsoleErrors();
    }

    [Fact]
    public async Task ScriptLoadedAfterFrame_StillSetsHeight()
    {
        // Odložené skripty webu (async, nástroje na zrychlení, souhlas s cookies) spustí embed.js až po tom, co stránka
        // v rámu poslala výšku. Skript o ni požádá znovu.
        await using Session session = await Session.StartAsync(app, 1024);
        await session.Page.GotoAsync(app.Allowed.Serve("pozdni-skript", Embed(Lessons, script: false)));
        await Assertions.Expect(session.Page.FrameLocator("#av").Locator(".av-nav__title")).ToBeVisibleAsync();
        await session.Page.WaitForTimeoutAsync(500);
        Assert.Equal(600, await FrameHeightAsync(session));

        await session.Page.EvaluateAsync("src => { const script = document.createElement('script'); script.src = src; document.body.append(script); }", app.Url("/embed.js"));

        await AssertHeightFollowsContentAsync(session);
        Assert.NotEqual(600, await FrameHeightAsync(session));
        session.AssertNoConsoleErrors();
    }

    [Fact]
    public async Task ShortPage_ShrinksFrame()
    {
        await using Session session = await Session.StartAsync(app, 1024);
        await session.Page.GotoAsync(app.Allowed.Serve("kratka", Embed("/?location=999", script: true)));

        await AssertHeightFollowsContentAsync(session);
        Assert.True(await FrameHeightAsync(session) < 300);
    }

    [Fact]
    public async Task WithoutScript_FrameKeepsFixedHeight_AndPageWorks()
    {
        await using Session session = await Session.StartAsync(app, 1024);
        await session.Page.GotoAsync(app.Allowed.Serve("bez-skriptu", Embed(Lessons, script: false)));
        await Assertions.Expect(session.Page.FrameLocator("#av").Locator(".av-nav__title")).ToBeVisibleAsync();
        await session.Page.WaitForTimeoutAsync(500);

        Assert.Equal(600, await FrameHeightAsync(session));
    }

    [Fact]
    public async Task OtherOrigin_CannotEmbed()
    {
        await using Session session = await Session.StartAsync(app, 1024);
        await session.Page.GotoAsync(app.Foreign.Serve("cizi", Embed(Lessons, script: true)));
        await session.Page.WaitForTimeoutAsync(1000);

        IFrame frame = (await (await session.Page.QuerySelectorAsync("#av"))!.ContentFrameAsync())!;
        Assert.DoesNotContain("av-root", await SafeContentAsync(frame), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmbedScript_IgnoresMessagesFromOtherWindows()
    {
        await using Session session = await Session.StartAsync(app, 1024);

        // Vedle stránky je rám z cizího originu, který posílá podvrženou výšku. Podvrhne ji i sama stránka merchanta.
        string evil = app.Foreign.Serve("zly", """<!DOCTYPE html><script>setInterval(() => parent.postMessage({ type: 'av:height', height: 7 }, '*'), 50);</script>""");
        string page = Embed(Lessons, script: true) + $"""<iframe id="zly" title="Cizí rám" src="{evil}"></iframe>""";

        await session.Page.GotoAsync(app.Allowed.Serve("podvrh", page));
        await AssertHeightFollowsContentAsync(session);
        await session.Page.EvaluateAsync("() => window.postMessage({ type: 'av:height', height: 9 }, '*')");
        await session.Page.WaitForTimeoutAsync(500);

        await AssertHeightFollowsContentAsync(session);
        Assert.True(await FrameHeightAsync(session) > 100);
        Assert.NotEqual(7, await session.Page.Locator("#zly").EvaluateAsync<double>("frame => frame.clientHeight"));
    }

    [Fact]
    public async Task TwoFramesOnOnePage_EachGetsItsOwnHeight()
    {
        // Merchant může na jednu stránku vložit dva pohledy. Zpráva jednoho nesmí změnit výšku druhého.
        await using Session session = await Session.StartAsync(app, 1024);
        string second = $"""<iframe id="av2" src="{app.Url("/?location=999")}" title="Druhý pohled" width="100%" height="600" style="border: 0;"></iframe>""";

        await session.Page.GotoAsync(app.Allowed.Serve("dva", Embed(Lessons, script: true) + second));

        await AssertHeightFollowsContentAsync(session, "#av");
        await AssertHeightFollowsContentAsync(session, "#av2");
        Assert.True(await FrameHeightAsync(session, "#av2") < await FrameHeightAsync(session, "#av"));
    }

    [Fact]
    public async Task CustomStyle_ChangesVariable_OnVisitorPageAndInFrames_NotInGenerator()
    {
        // Styl je na webu merchanta (jiný origin), CSP by ho bez povolení zablokovala a zapsala chybu do konzole.
        const string Accent = "element => getComputedStyle(element).backgroundColor";
        await using Session session = await Session.StartAsync(app, 1024);

        await session.Page.GotoAsync(app.Url(Lessons));
        Assert.Equal(VisitorFixture.MerchantAccent, await session.Page.Locator(".av-views__link[aria-current]").EvaluateAsync<string>(Accent));

        // Písmo z webu stylu CSP pustí (rozhodnutí 2026-11-09): požadavek dojde na server merchanta.
        int fonts = app.Allowed.RequestsOf("pismo.woff2");
        await session.Page.EvaluateAsync("() => document.fonts.load('16px AvMerchant').catch(() => null)");
        Assert.True(app.Allowed.RequestsOf("pismo.woff2") > fonts, "Písmo z webu stylu se nestáhlo.");

        await session.Page.GotoAsync(app.Allowed.Serve("se-stylem", Embed(Lessons, script: true)));
        Assert.Equal(VisitorFixture.MerchantAccent, await session.Page.FrameLocator("#av").Locator(".av-views__link[aria-current]").EvaluateAsync<string>(Accent));

        await session.Page.GotoAsync(app.Url("/Generator?view=calendar&also=agenda&segment=2"));
        IFrameLocator preview = session.Page.FrameLocator(".av-generator__preview");
        Assert.Equal(VisitorFixture.MerchantAccent, await preview.Locator(".av-views__link[aria-current]").EvaluateAsync<string>(Accent));
        Assert.False(await session.Page.EvaluateAsync<bool>("() => [...document.styleSheets].some(sheet => sheet.href?.endsWith('/vzhled.css'))"));
        session.AssertNoConsoleErrors();
    }

    [Theory]
    [MemberData(nameof(EmbeddedPages))]
    public async Task EmbeddedPage_HasNoAccessibilityViolations_NorScrollInFrame(string path, int width)
    {
        // Web merchanta sám přístupný je, aby axe hlásil jen nálezy stránky v rámu.
        await using Session session = await Session.StartAsync(app, width);
        await session.Page.GotoAsync(app.Allowed.Serve($"pristupnost-{width}-{path.GetHashCode(StringComparison.Ordinal)}", AccessibleEmbed(path)));
        await AssertHeightFollowsContentAsync(session);

        AxeResult result = await session.Page.RunAxe();
        IFrame frame = (await (await session.Page.QuerySelectorAsync("#av"))!.ContentFrameAsync())!;
        int overflow = await frame.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

        string[] violations = [.. result.Violations.SelectMany(violation => violation.Nodes.Select(node => string.Create(
            CultureInfo.InvariantCulture,
            $"{violation.Id} {node.Target}: {string.Join("; ", node.Any.Concat(node.All).Concat(node.None).Select(check => check.Message))}")))];
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
        Assert.Equal(0, overflow);
        session.AssertNoConsoleErrors();
    }

    [Fact]
    public async Task EmbeddedPage_WorksWithKeyboard()
    {
        await using Session session = await Session.StartAsync(app, 1024);
        await session.Page.GotoAsync(app.Allowed.Serve("klavesnice", AccessibleEmbed("/?segment=2&view=month,calendar&days=60")));
        IFrameLocator frame = session.Page.FrameLocator("#av");
        await Assertions.Expect(frame.Locator(".av-month")).ToBeVisibleAsync();

        // Tabulátor projde z webu merchanta do rámu.
        await TabIntoFrameAsync(session, "a.av-nav__link");
        await session.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(frame.Locator(".av-nav__title")).ToHaveTextAsync("prosinec 2026");
    }

    private static async Task TabIntoFrameAsync(Session session, string selector)
    {
        IFrame frame = (await (await session.Page.QuerySelectorAsync("#av"))!.ContentFrameAsync())!;
        for (int press = 0; press < 40; press++)
        {
            await session.Page.Keyboard.PressAsync("Tab");
            if (await frame.EvaluateAsync<bool>("selector => document.hasFocus() && (document.activeElement?.matches(selector) ?? false)", selector))
            {
                return;
            }
        }

        Assert.Fail($"Na {selector} v rámu nejde dojít klávesnicí.");
    }

    private string AccessibleEmbed(string path) =>
        $"""<!DOCTYPE html><html lang="cs"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Web merchanta</title></head><body><main><h1>Rezervace</h1><iframe id="av" src="{app.Url(path)}" title="Volné termíny" width="100%" height="600" style="border: 0;"></iframe></main><script src="{app.Url("/embed.js")}" async></script></body></html>""";

    private string Embed(string path, bool script) =>
        $"""<!DOCTYPE html><html lang="cs"><title>Web merchanta</title><iframe id="av" src="{app.Url(path)}" title="Volné termíny" width="100%" height="600" style="border: 0;"></iframe>"""
        + (script ? $"""<script src="{app.Url("/embed.js")}" async></script>""" : string.Empty);

    private static Task<double> FrameHeightAsync(Session session, string frame = "#av") =>
        session.Page.Locator(frame).EvaluateAsync<double>("frame => frame.clientHeight");

    // Výška rámu je výška obsahu stránky v něm, s tolerancí na zaokrouhlení. Rám se může právě načítat znovu.
    private static async Task AssertHeightFollowsContentAsync(Session session, string selector = "#av")
    {
        double frameHeight = 0;
        double content = 0;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            frameHeight = await FrameHeightAsync(session, selector);
            try
            {
                IFrame frame = (await (await session.Page.QuerySelectorAsync(selector))!.ContentFrameAsync())!;
                content = await frame.EvaluateAsync<double>("() => document.readyState === 'complete' ? document.documentElement.getBoundingClientRect().height : -1");
            }
            catch (PlaywrightException)
            {
                content = -1;
            }

            if (content > 0 && Math.Abs(frameHeight - Math.Ceiling(content)) <= 1)
            {
                return;
            }

            await session.Page.WaitForTimeoutAsync(100);
        }

        Assert.Fail($"Rám {selector} má výšku {frameHeight}, obsah {content}.");
    }

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
}
