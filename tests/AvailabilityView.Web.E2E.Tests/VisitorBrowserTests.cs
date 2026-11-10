using System.Globalization;
using System.Text.RegularExpressions;
using AvailabilityView.Web.E2E.Tests.Support;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;

namespace AvailabilityView.Web.E2E.Tests;

/// <summary>
/// Stránka návštěvníka v prohlížeči s vynucenou CSP: přístupnost, úzká šířka, filtry a odkazy.
/// </summary>
[Trait("Category", "E2E")]
[Collection(VisitorTestGroup.Name)]
public sealed class VisitorBrowserTests(VisitorFixture app)
{
    private const int Wide = 1280;
    private const int Narrow = 320;

    private const string Lessons = "/?segment=2&pickers=segment,service";
    private const string Individual = "/?pickers=service,resource";

    public static TheoryData<string, int> Pages
    {
        get
        {
            string[] paths =
            [
                $"{Lessons}&view=agenda,calendar,timegrid",
                $"{Lessons}&view=calendar,agenda,timegrid",
                $"{Lessons}&view=timegrid,agenda,calendar&week=2026-11-09",
                $"{Individual}&view=agenda,calendar,timegrid",
                $"{Individual}&view=calendar,agenda,timegrid",
                $"{Individual}&view=timegrid,agenda,calendar",
                $"{Lessons}&view=month,calendar",
                $"{Individual}&view=month,timegrid",
                $"{Lessons}&view=next,agenda",
                $"{Individual}&view=next",
                "/?location=999",
            ];
            var data = new TheoryData<string, int>();
            foreach (string path in paths)
            {
                foreach (string lang in (string[])["cs", "en"])
                {
                    foreach (int width in (int[])[Wide, Narrow])
                    {
                        data.Add($"{path}&lang={lang}", width);
                    }
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Page_HasNoAccessibilityViolations_NorConsoleErrors(string path, int width)
    {
        await using Session session = await Session.StartAsync(app, width);
        await session.Page.GotoAsync(app.Url(path));

        AxeResult result = await session.Page.RunAxe();

        // Zpráva místo Assert.Empty: xunit dlouhé položky zkrátí a nebylo by vidět, který prvek selhal.
        string[] violations = [.. result.Violations.SelectMany(violation => violation.Nodes.Select(node => string.Create(
            CultureInfo.InvariantCulture,
            $"{violation.Id} {node.Target}: {string.Join("; ", node.Any.Concat(node.All).Concat(node.None).Select(check => check.Message))}")))];
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
        session.AssertNoConsoleErrors();
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Page_HasNoHorizontalScroll(string path, int width)
    {
        await using Session session = await Session.StartAsync(app, width);
        await session.Page.GotoAsync(app.Url(path));

        int overflow = await session.Page.EvaluateAsync<int>("document.documentElement.scrollWidth - document.documentElement.clientWidth");

        Assert.Equal(0, overflow);
    }

    [Theory]
    [InlineData($"{Lessons}&view=calendar", ".av-calendar__day")]
    [InlineData($"{Lessons}&view=timegrid&week=2026-11-09", ".av-timegrid__day")]
    [InlineData($"{Individual}&view=timegrid", ".av-timegrid__day")]
    public async Task NarrowCalendars_ShowDaysOneUnderAnother_AtFullWidth(string path, string day)
    {
        await using Session session = await Session.StartAsync(app, Narrow);
        await session.Page.GotoAsync(app.Url(path));

        // Sedm dní vedle sebe by na 320 px mělo po 40 px a nešly by číst.
        double[] widths = await session.Page.EvalOnSelectorAllAsync<double[]>(day, "days => days.map(day => day.getBoundingClientRect().width)");
        double[] tops = await session.Page.EvalOnSelectorAllAsync<double[]>(day, "days => days.map(day => day.getBoundingClientRect().top)");

        Assert.Equal(7, widths.Length);
        Assert.All(widths, value => Assert.True(value > 250, $"Den je široký jen {value} px."));
        Assert.Equal(tops.Order(), tops);
        Assert.Equal(7, tops.Distinct().Count());
    }

    [Fact]
    public async Task Pointer_ChangeSubmitsFilterAtOnce()
    {
        await using Session session = await Session.StartAsync(app, Wide);
        await session.Page.GotoAsync(app.Url("/?pickers=segment"));

        await session.Page.Locator("#av-filter-segment").SelectOptionAsync("2");

        await Assertions.Expect(session.Page).ToHaveURLAsync(new Regex("segment=2"));
        session.AssertNoConsoleErrors();
    }

    [Fact]
    public async Task Calendar_SwitchesToNextWeekAndBack()
    {
        await using Session session = await Session.StartAsync(app, Wide);
        await session.Page.GotoAsync(app.Url($"{Lessons}&view=calendar"));
        ILocator title = session.Page.Locator(".av-nav__title");
        string current = await title.InnerTextAsync();
        ILocator previous = session.Page.Locator(".av-nav > :first-child");
        ILocator next = session.Page.Locator(".av-nav > :last-child");
        await Assertions.Expect(previous).ToHaveClassAsync(new Regex("av-nav__link--disabled"));

        await next.ClickAsync();

        await Assertions.Expect(session.Page).ToHaveURLAsync(new Regex("week=2026-11-09"));
        await Assertions.Expect(title).Not.ToHaveTextAsync(current);
        await Assertions.Expect(session.Page.Locator(".av-calendar__slot-time")).ToHaveCountAsync(2); // úterý 10. 11.

        await previous.ClickAsync();

        await Assertions.Expect(title).ToHaveTextAsync(current);
        await Assertions.Expect(previous).ToHaveClassAsync(new Regex("av-nav__link--disabled"));
        await Assertions.Expect(session.Page.Locator(".av-calendar__slot-time")).ToHaveCountAsync(2); // dnes a sobota
        session.AssertNoConsoleErrors();
    }

    [Fact]
    public async Task WithoutJavaScript_FilterSubmitsByButton()
    {
        await using Session session = await Session.StartAsync(app, Wide, javaScript: false);
        string start = app.Url("/?pickers=segment,service&view=agenda,calendar&week=2026-11-09");
        await session.Page.GotoAsync(start);

        await session.Page.Locator("#av-filter-segment").SelectOptionAsync("2");
        await session.Page.WaitForTimeoutAsync(500);
        Assert.Equal(start, session.Page.Url);

        await session.Page.Locator("button.av-filters__submit").ClickAsync();

        await Assertions.Expect(session.Page).ToHaveURLAsync(new Regex("segment=2"));
        await Assertions.Expect(session.Page.Locator("#av-filter-segment option:checked")).ToHaveTextAsync("Lekce");
        await Assertions.Expect(session.Page.Locator(".av-slot__service").First).ToContainTextAsync("Jóga");
        Assert.Contains("week=2026-11-09", session.Page.Url, StringComparison.Ordinal); // ostatní parametry zůstanou
    }

    [Fact]
    public async Task WithoutJavaScript_ViewAndWeekLinksWork()
    {
        await using Session session = await Session.StartAsync(app, Wide, javaScript: false);
        await session.Page.GotoAsync(app.Url($"{Lessons}&view=agenda,calendar"));

        await session.Page.Locator("a.av-views__link:not([aria-current])").ClickAsync();
        await Assertions.Expect(session.Page.Locator(".av-calendar")).ToBeVisibleAsync();

        await session.Page.Locator(".av-nav > a.av-nav__link").ClickAsync();

        await Assertions.Expect(session.Page).ToHaveURLAsync(new Regex("week=2026-11-09"));
        await Assertions.Expect(session.Page.Locator(".av-calendar__slot-time")).ToHaveCountAsync(2);
        Assert.Contains("pickers=segment", session.Page.Url, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<string> TimePages => new(
        $"{Lessons}&view=agenda",
        $"{Lessons}&view=calendar",
        $"{Individual}&view=timegrid",
        $"{Individual}&view=agenda&lang=en");

    [Theory]
    [MemberData(nameof(TimePages))]
    public async Task BrowserTimeZone_DoesNotChangeTimes(string path)
    {
        string prague = await MainTextAsync(path, "Europe/Prague");

        // Západně i východně od Prahy, aby se posun neschoval do stejného dne.
        Assert.Equal(prague, await MainTextAsync(path, "America/Los_Angeles"));
        Assert.Equal(prague, await MainTextAsync(path, "Pacific/Kiritimati"));
        Assert.Contains(path.StartsWith(Individual, StringComparison.Ordinal) ? "10:00" : "17:00", prague, StringComparison.Ordinal);
    }

    private async Task<string> MainTextAsync(string path, string timeZone)
    {
        await using Session session = await Session.StartAsync(app, Wide, timeZone: timeZone);
        await session.Page.GotoAsync(app.Url(path));
        Assert.Equal(timeZone, await session.Page.EvaluateAsync<string>("Intl.DateTimeFormat().resolvedOptions().timeZone"));
        return await session.Page.Locator("main").InnerTextAsync();
    }

    /// <summary>Izolovaný kontext a stránka se sledováním chyb v konzoli, včetně porušení CSP.</summary>
    internal sealed class Session : IAsyncDisposable
    {
        private readonly List<string> _errors = [];

        // axe-core si styly z cizího originu stahuje přes fetch kvůli pravidlům nad CSSOM. Stránka sama nic nestahuje
        // a CSP to správně blokuje (connect-src). Ignoruje se jen tahle zpráva: zablokovaný styl hlásí Chromium jinak
        // (Loading the stylesheet …), ten by test dál shodil.
        private readonly string _axeFetchOfMerchantStyle;

        private Session(IBrowserContext context, IPage page, string merchantStyle)
        {
            Context = context;
            Page = page;
            _axeFetchOfMerchantStyle = $"Connecting to '{merchantStyle}' violates";
            Page.Console += (_, message) =>
            {
                if (message.Type == "error" && !message.Text.StartsWith(_axeFetchOfMerchantStyle, StringComparison.Ordinal))
                {
                    _errors.Add(message.Text);
                }
            };
        }

        public IBrowserContext Context { get; }

        public IPage Page { get; }

        public static async Task<Session> StartAsync(VisitorFixture app, int width, bool javaScript = true, string timeZone = "Europe/Prague")
        {
            IBrowserContext context = await app.NewContextAsync(width, javaScript, timeZone);
            return new Session(context, await context.NewPageAsync(), app.MerchantStyle);
        }

        public void AssertNoConsoleErrors() => Assert.True(_errors.Count == 0, string.Join(Environment.NewLine, _errors));


        /// <summary>Tabulátorem na první prvek, který odpovídá selektoru. Ověřuje, že na něj jde dojít klávesnicí.</summary>
        public async Task TabToAsync(string selector)
        {
            for (int press = 0; press < 40; press++)
            {
                await Page.Keyboard.PressAsync("Tab");
                if (await Page.EvaluateAsync<bool>("selector => document.activeElement?.matches(selector) ?? false", selector))
                {
                    return;
                }
            }

            Assert.Fail($"Na {selector} nejde dojít tabulátorem.");
        }

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
