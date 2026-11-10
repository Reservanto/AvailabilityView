using System.Text.RegularExpressions;
using AvailabilityView.Web.E2E.Tests.Support;
using Session = AvailabilityView.Web.E2E.Tests.VisitorBrowserTests.Session;

namespace AvailabilityView.Web.E2E.Tests;

/// <summary>
/// Ovládání stránky návštěvníka klávesnicí. Vlastní aplikace: se
/// sdílenou by testy stránky návštěvníka narazily na limit 120 požadavků za minutu.
/// </summary>
[Trait("Category", "E2E")]
[Collection(KeyboardTestGroup.Name)]
public sealed class KeyboardBrowserTests(VisitorFixture app)
{
    private const int Wide = 1280;

    private const string Lessons = "/?segment=2&pickers=segment,service";

    // Čekání průběžnou kontrolou adresy: WaitForURLAsync zmešká navigaci, která skončí dřív, než se na ni začne čekat.
    [Fact]
    public async Task Keyboard_SwitchesWeek()
    {
        await using Session session = await Session.StartAsync(app, Wide);
        await session.Page.GotoAsync(app.Url($"{Lessons}&view=calendar"));

        await session.TabToAsync("a.av-nav__link");
        await session.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(session.Page).ToHaveURLAsync(new Regex("week=2026-11-09"));
    }

    [Fact]
    public async Task Keyboard_SwitchesMonth_AndOpensWeekOfFreeDay()
    {
        await using Session session = await Session.StartAsync(app, Wide);
        await session.Page.GotoAsync(app.Url($"{Lessons}&view=month,calendar&days=60"));

        await session.TabToAsync("a.av-nav__link");
        await session.Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(session.Page).ToHaveURLAsync(new Regex("month=2026-12"));

        await session.TabToAsync("a.av-nav__link");
        await session.Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(session.Page).ToHaveURLAsync(new Regex("month=2026-11"));

        // Den s volnem vede na svůj týden v kalendáři.
        await session.TabToAsync("a.av-month__link");
        await session.Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(session.Page.Locator(".av-calendar")).ToBeVisibleAsync();
        await Assertions.Expect(session.Page).ToHaveURLAsync(new Regex("view=calendar"));
    }

    [Fact]
    public async Task Keyboard_SwitchesView()
    {
        await using Session session = await Session.StartAsync(app, Wide);
        await session.Page.GotoAsync(app.Url($"{Lessons}&view=agenda,timegrid"));

        await session.TabToAsync("a.av-views__link:not([aria-current])");
        await session.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(session.Page).ToHaveURLAsync(new Regex("view=timegrid"));
        await Assertions.Expect(session.Page.Locator(".av-timegrid")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Keyboard_ArrowChangesFilterWithoutSubmit_ButtonSubmits()
    {
        await using Session session = await Session.StartAsync(app, Wide);
        string start = app.Url("/?pickers=segment");
        await session.Page.GotoAsync(start);

        await session.TabToAsync("select.av-filters__select");
        await session.Page.Keyboard.PressAsync("ArrowDown");
        await session.Page.WaitForTimeoutAsync(500);

        Assert.Equal(start, session.Page.Url);
        Assert.Equal("2", await session.Page.Locator("#av-filter-segment").InputValueAsync());

        await session.TabToAsync("button.av-filters__submit");
        await session.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(session.Page).ToHaveURLAsync(new Regex("segment=2"));
        await Assertions.Expect(session.Page.Locator("#av-filter-segment option:checked")).ToHaveTextAsync("Lekce");
    }

    [Fact]
    public async Task KeyboardChangeEvent_DoesNotSubmit()
    {
        // Chromium na Windows po šipce na zavřeném seznamu pošle keydown, input a change (ověřeno v prohlížeči
        // 2026-11-09). Headless Chromium change nepošle, proto test posloupnost přehraje sám.
        await using Session session = await Session.StartAsync(app, Wide);
        string start = app.Url("/?pickers=segment");
        await session.Page.GotoAsync(start);

        await session.Page.Locator("#av-filter-segment").EvaluateAsync(
            """
            select => {
                select.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
                select.value = '2';
                select.dispatchEvent(new Event('input', { bubbles: true }));
                select.dispatchEvent(new Event('change', { bubbles: true }));
            }
            """);
        await session.Page.WaitForTimeoutAsync(500);

        Assert.Equal(start, session.Page.Url);
    }
}
