using System.Net;
using System.Text.RegularExpressions;
using AvailabilityView.Web.Tests.Support;
using Microsoft.AspNetCore.WebUtilities;
using static AvailabilityView.Web.Tests.Support.VisitorApp;

namespace AvailabilityView.Web.Tests;

/// <summary>
/// Kalendář se sloupci dnů a přepínač zobrazení. Je pátek 6. 11. 2026, se 14 dny se
/// nabízí 6. až 19. 11., tedy týdny od 2., 9. a 16. 11.
/// </summary>
public sealed partial class CalendarPageTests
{
    [Fact]
    public async Task Calendar_StartsWithCurrentWeekFromMonday()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/?segment=2&view=calendar");

        Assert.Contains("<span class=\"av-nav__title\">2. 11. – 8. 11. 2026</span>", page, StringComparison.Ordinal);
        Assert.Equal(
            ["po 2. 11.", "út 3. 11.", "st 4. 11.", "čt 5. 11.", "pá 6. 11.", "so 7. 11.", "ne 8. 11."],
            DayHeadings(page));
    }

    [Fact]
    public async Task DaysBeforeToday_AreOutsideOffer_TodayIsMarked()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/?segment=2&view=calendar");

        Assert.Equal(4, Regex.Count(page, "av-calendar__day--outside"));
        Assert.Equal(4, Regex.Count(page, ">mimo nabídku<"));
        Assert.Contains("<section class=\"av-calendar__day av-calendar__day--today\">", page, StringComparison.Ordinal);
        Assert.Contains("pá 6. 11.<span class=\"av-visually-hidden\">, dnes</span>", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FirstWeek_CannotGoBack_CanGoForward()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/?segment=2&view=calendar");

        Assert.Contains("<span class=\"av-nav__link av-nav__link--disabled\">‹ Předchozí týden</span>", page, StringComparison.Ordinal);
        Assert.Equal("2026-11-09", LinkQuery(page, "Další týden")["week"]);
    }

    [Fact]
    public async Task LastWeek_CanGoBack_CannotGoForward_AndEndsWithOutsideDays()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/?segment=2&view=calendar&week=2026-11-16");

        Assert.Contains("16. 11. – 22. 11. 2026", page, StringComparison.Ordinal);
        Assert.Equal("2026-11-09", LinkQuery(page, "Předchozí týden")["week"]);
        Assert.Contains("<span class=\"av-nav__link av-nav__link--disabled\">Další týden ›</span>", page, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Count(page, ">mimo nabídku<"));
    }

    [Theory]
    [InlineData("2027-03-01", "16. 11. – 22. 11. 2026")]
    [InlineData("2020-01-01", "2. 11. – 8. 11. 2026")]
    public async Task WeekOutsideOffer_MovesToNearestOfferedWeek(string week, string title)
    {
        await using var app = new VisitorApp();

        Assert.Contains(title, await app.GetAsync($"/?segment=2&view=calendar&week={week}"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Days_LimitWhereYouCanGo()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/?segment=2&view=calendar&days=2");

        Assert.Contains("<span class=\"av-nav__link av-nav__link--disabled\">Další týden ›</span>", page, StringComparison.Ordinal);
        Assert.Equal(5, Regex.Count(page, ">mimo nabídku<"));
    }

    [Fact]
    public async Task Slot_IsInColumnOfItsLocalDay_OtherWeeksAreNotShown()
    {
        await using var app = new VisitorApp();
        app.Lessons.Slots.AddRange([Lesson(Local(7, 0, 30), "1/5"), Lesson(Local(10, 9), "2/5")]);

        string page = await app.GetAsync("/?segment=2&view=calendar");

        string saturday = page[page.IndexOf("so 7. 11.", StringComparison.Ordinal)..page.IndexOf("ne 8. 11.", StringComparison.Ordinal)];
        Assert.Contains("0:30–1:30", saturday, StringComparison.Ordinal);
        Assert.Contains("Sál · 1/5", saturday, StringComparison.Ordinal);
        Assert.DoesNotContain("2/5", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FullSlot_IsMarked()
    {
        await using var app = new VisitorApp();
        app.Lessons.Slots.Add(Lesson(Local(7, 9), "obsazeno", full: true));

        string page = await app.GetAsync("/?segment=2&view=calendar");

        Assert.Contains("<li class=\"av-calendar__slot av-calendar__slot--full\">", page, StringComparison.Ordinal);
        Assert.Contains("Sál · obsazeno<span class=\"av-visually-hidden\">, plně obsazeno</span>", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyWeek_SaysSo_AndStillOffersOtherWeeks()
    {
        await using var app = new VisitorApp();
        app.Lessons.Slots.Add(Lesson(Local(10, 9)));

        string page = await app.GetAsync("/?segment=2&view=calendar");

        Assert.Contains("<p class=\"av-message\">V tomto období nejsou žádné volné termíny.</p>", page, StringComparison.Ordinal);
        Assert.Equal("2026-11-09", LinkQuery(page, "Další týden")["week"]);
    }

    [Fact]
    public async Task English_FormatsWeekAndDaysBritish()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/?segment=2&view=calendar&lang=en");

        Assert.Contains("2 Nov – 8 Nov 2026", page, StringComparison.Ordinal);
        Assert.Contains("Mon 2 Nov", page, StringComparison.Ordinal);
        Assert.Contains(">not offered<", page, StringComparison.Ordinal);
        Assert.Equal("en", LinkQuery(page, "Next week")["lang"]);
    }

    [Fact]
    public async Task WeekWithSpringForward_HasRightDaysAndTimes()
    {
        await using var app = new VisitorApp();
        app.Time.SetUtcNow(new DateTimeOffset(2027, 3, 24, 8, 0, 0, TimeSpan.Zero));
        app.Lessons.Slots.AddRange([
            Lesson(new DateTimeOffset(2027, 3, 28, 1, 30, 0, TimeSpan.FromHours(1))),
            Lesson(new DateTimeOffset(2027, 3, 28, 23, 30, 0, TimeSpan.FromHours(2))),
        ]);

        string page = await app.GetAsync("/?segment=2&view=calendar");

        Assert.Equal("ne 28. 3.", DayHeadings(page)[6]);
        string sunday = page[page.IndexOf("ne 28. 3.", StringComparison.Ordinal)..];
        Assert.Contains("1:30–3:30", sunday, StringComparison.Ordinal); // hodina 2:00 až 3:00 neexistuje
        Assert.Contains("23:30–0:30", sunday, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Timegrid_GoesByWeeks_LikeCalendar()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/?segment=2&view=timegrid&week=2026-11-16");

        Assert.Contains("16. 11. – 22. 11. 2026", page, StringComparison.Ordinal);
        Assert.DoesNotContain("av-agenda", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SingleView_HasNoSwitcher()
    {
        await using var app = new VisitorApp();

        Assert.DoesNotContain("av-views", await app.GetAsync("/?segment=2&view=calendar"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Switcher_StartsWithFirstView_KeepsOrderFixed_AndKeepsParameters()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/?view=calendar,agenda&segment=2&utm_source=news&week=2026-11-09");

        Assert.Contains("av-calendar__grid", page, StringComparison.Ordinal);
        Assert.Equal(["Seznam", "Kalendář"], Regex.Matches(page, "class=\"av-views__link\"[^>]*>([^<]+)<").Select(match => match.Groups[1].Value));
        Assert.Contains("aria-current=\"page\">Kalendář<", page, StringComparison.Ordinal);
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> agenda = LinkQuery(page, "Seznam");
        Assert.Equal("agenda,calendar", agenda["view"]);
        Assert.Equal(("2", "news"), (agenda["segment"].ToString(), agenda["utm_source"].ToString()));
    }

    [Fact]
    public async Task SwitchingThereAndBack_ShowsTheOtherView()
    {
        await using var app = new VisitorApp();
        app.Lessons.Slots.Add(Lesson(Local(6, 17)));
        string calendar = await app.GetAsync("/?view=calendar,agenda&segment=2");

        string agenda = await app.GetAsync("/" + Href(calendar, "Seznam"));
        string back = await app.GetAsync("/" + Href(agenda, "Kalendář"));

        Assert.Contains("av-agenda", agenda, StringComparison.Ordinal);
        Assert.DoesNotContain("av-calendar__grid", agenda, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"page\">Seznam<", agenda, StringComparison.Ordinal);
        Assert.Contains("av-calendar__grid", back, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Switcher_IsAlsoOnEmptyAgenda()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/?view=agenda,calendar&segment=2");

        Assert.Contains("V tomto období nejsou žádné volné termíny.", page, StringComparison.Ordinal);
        Assert.Contains("av-views", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotAvailable_HasNoSwitcherOrCalendar()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/?view=calendar,agenda&segment=999");

        Assert.DoesNotContain("av-views", page, StringComparison.Ordinal);
        Assert.DoesNotContain("av-calendar", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownView_IsNotAvailable()
    {
        await using var app = new VisitorApp();

        using HttpResponseMessage response = await app.SendAsync("/?view=calendar,list");

        Assert.Contains("Tento pohled není dostupný.", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string[] DayHeadings(string page) =>
        [.. Regex.Matches(page, "<h2 class=\"av-calendar__date\">([^<]+)").Select(match => match.Groups[1].Value)];

    private static string Href(string page, string text) =>
        WebUtility.HtmlDecode(Regex.Match(page, $"href=\"([^\"]*)\"[^>]*>[‹ ]*{Regex.Escape(text)}").Groups[1].Value);

    private static Dictionary<string, Microsoft.Extensions.Primitives.StringValues> LinkQuery(string page, string text)
    {
        string href = Href(page, text);
        Assert.NotEmpty(href);
        return QueryHelpers.ParseQuery(href);
    }
}
