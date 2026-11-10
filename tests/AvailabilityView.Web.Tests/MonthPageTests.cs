using System.Net;
using System.Text.RegularExpressions;
using AvailabilityView.Application.Availability;
using AvailabilityView.Web.Tests.Support;
using Microsoft.AspNetCore.WebUtilities;
using static AvailabilityView.Web.Tests.Support.VisitorApp;

namespace AvailabilityView.Web.Tests;

/// <summary>
/// Měsíční přehled a nejbližší volný termín. Je pátek 6. 11. 2026 v 9:00, se 14 dny se
/// nabízí 6. až 19. 11. Listopad 2026 začíná nedělí.
/// </summary>
public sealed partial class MonthPageTests
{
    [Fact]
    public async Task Month_ShowsStateOfEveryDay_FromMonday()
    {
        await using var app = new VisitorApp();
        app.Lessons.Slots.AddRange([Lesson(Local(6, 17)), Lesson(Local(7, 10), "obsazeno", full: true)]);

        string page = await app.GetAsync("/?segment=2&view=month");

        Assert.Contains("<span class=\"av-nav__title\">listopad 2026</span>", page, StringComparison.Ordinal);
        Assert.Equal(["po", "út", "st", "čt", "pá", "so", "ne"], Weekdays(page));
        string[] cells = Cells(page);
        Assert.Equal(42, cells.Length); // 6 prázdných buněk před nedělí 1. 11., 30 dní a 6 prázdných do konce týdne
        Assert.All([.. cells[..6], .. cells[^6..]], cell => Assert.Contains("av-month__day--empty", cell, StringComparison.Ordinal));
        Assert.Contains("av-month__day--outside", Day(page, 5), StringComparison.Ordinal);
        Assert.Contains("pátek 6. listopadu, volno, dnes", Day(page, 6), StringComparison.Ordinal);
        Assert.Contains("av-month__day--today", Day(page, 6), StringComparison.Ordinal);
        Assert.Contains("sobota 7. listopadu, plno", Day(page, 7), StringComparison.Ordinal);
        Assert.Contains("neděle 8. listopadu, bez lekcí", Day(page, 8), StringComparison.Ordinal);
        Assert.Contains("av-month__day--outside", Day(page, 20), StringComparison.Ordinal);
        Assert.DoesNotContain("av-month__link", page, StringComparison.Ordinal); // bez týdenního zobrazení
        Assert.DoesNotContain("av-message", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FreeDay_LinksToItsWeek_InFirstWeekView_KeepingParameters()
    {
        await using var app = new VisitorApp();
        app.Lessons.Slots.AddRange([Lesson(Local(10, 17)), Lesson(Local(11, 17), "obsazeno", full: true)]);

        string page = await app.GetAsync("/?segment=2&view=month,timegrid,calendar&utm_source=web");

        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> link = QueryHelpers.ParseQuery(WebUtility.HtmlDecode(
            Regex.Match(Day(page, 10), "href=\"(?<url>[^\"]+)\"").Groups["url"].Value));
        Assert.Equal(("timegrid,month,calendar", "2026-11-10", "web", "2"), (link["view"].ToString(), link["week"].ToString(), link["utm_source"].ToString(), link["segment"].ToString()));
        Assert.DoesNotContain("href", Day(page, 11), StringComparison.Ordinal); // plný den odkaz nemá
    }

    [Fact]
    public async Task Individual_FullWhenWorkingWithoutFreeTime_ClosedWhenNotWorking()
    {
        await using var app = new VisitorApp();
        app.Individual.Slots.Add(new(Local(9, 10), Local(9, 12), null, 100, null, false));
        app.Individual.Timeline = new SourceTimeline(
            [new ResourceInterval(100, Local(9, 9), Local(9, 17)), new ResourceInterval(100, Local(10, 9), Local(10, 17))],
            []);

        string page = await app.GetAsync("/?view=month");

        Assert.Contains("pondělí 9. listopadu, volno", Day(page, 9), StringComparison.Ordinal);
        Assert.Contains("úterý 10. listopadu, plno", Day(page, 10), StringComparison.Ordinal);
        Assert.Contains("středa 11. listopadu, zavřeno", Day(page, 11), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Months_GoOnlyWithinOffer()
    {
        await using var app = new VisitorApp();

        string current = await app.GetAsync("/?segment=2&view=month&days=60");
        string december = await app.GetAsync("/?segment=2&view=month&days=60&month=2026-12");

        Assert.Contains("av-nav__link--disabled\">‹ Předchozí měsíc", current, StringComparison.Ordinal);
        Assert.Contains("month=2026-12", current, StringComparison.Ordinal);
        Assert.Contains("prosinec 2026", december, StringComparison.Ordinal);
        Assert.Contains("month=2026-11", december, StringComparison.Ordinal);
        Assert.Contains("leden 2027", await app.GetAsync("/?segment=2&view=month&days=60&month=2027-05"), StringComparison.Ordinal); // poslední nabízený den je 4. 1.
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("2026-1")]
    [InlineData("2026-11-01")]
    [InlineData("26-11")]
    [InlineData("listopad")]
    public async Task MalformedMonth_IsNotAvailable(string month)
    {
        await using var app = new VisitorApp();

        // Stejná odpověď jako neexistující objekt (SPEC sekce 5).
        Assert.Equal(
            await app.GetComparableAsync("/?segment=999&view=month"),
            await app.GetComparableAsync($"/?segment=2&view=month&month={month}"));
    }

    [Fact]
    public async Task English_MonthAndStates()
    {
        await using var app = new VisitorApp();
        app.Lessons.Slots.Add(Lesson(Local(6, 17)));

        string page = await app.GetAsync("/?segment=2&view=month&lang=en");

        Assert.Contains("November 2026", page, StringComparison.Ordinal);
        Assert.Equal(["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"], Weekdays(page));
        Assert.Contains("Friday 6 November, available", Day(page, 6), StringComparison.Ordinal);
        Assert.Contains("Saturday 7 November, no classes", Day(page, 7), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(6, 17, "dnes 17:00, Jóga")]
    [InlineData(7, 10, "zítra 10:00, Jóga")]
    [InlineData(9, 8, "pondělí 9. 11. 8:00, Jóga")]
    public async Task Next_IsFirstLessonThatIsNotFull(int day, int hour, string expected)
    {
        await using var app = new VisitorApp();
        app.Lessons.Slots.AddRange([Lesson(Local(6, 12), "obsazeno", full: true), Lesson(Local(day, hour))]);

        string page = await app.GetAsync("/?segment=2&view=next");

        Assert.Contains($"<p class=\"av-next\">Nejbližší volný termín: {expected}</p>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("av-message", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Next_OfFreeTime_HasNoServiceName_EvenWhenServiceIsChosen()
    {
        await using var app = new VisitorApp();
        app.Individual.Slots.Add(new(Local(6, 10), Local(6, 12), null, 100, null, false));

        Assert.Contains("Nejbližší volný termín: dnes 10:00</p>", await app.GetAsync("/?view=next"), StringComparison.Ordinal);
        Assert.Contains("Nejbližší volný termín: dnes 10:00</p>", await app.GetAsync("/?view=next&service=1001"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Next_OfFreeTimeRunningNow_IsNow()
    {
        // Probíhající volno: čtení volna sestaví úsek z prvního začátku od teď (IndividualFreeTimeTests), tady 9:00.
        await using var app = new VisitorApp();
        app.Individual.Slots.Add(new(Local(6, 9), Local(6, 12), null, 100, null, false));

        Assert.Contains("Nejbližší volný termín: dnes 9:00</p>", await app.GetAsync("/?view=next"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Next_OfLessonsWithChosenService_HasNoServiceName()
    {
        await using var app = new VisitorApp();
        app.Lessons.Slots.Add(Lesson(Local(6, 17)));

        Assert.Contains("Nejbližší volný termín: dnes 17:00</p>", await app.GetAsync("/?segment=2&service=1002&view=next"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Next_WithNothingFree_SaysUntilWhen()
    {
        await using var app = new VisitorApp();
        app.Lessons.Slots.Add(Lesson(Local(6, 12), "obsazeno", full: true));

        string page = await app.GetAsync("/?segment=2&view=next");

        Assert.Contains("<p class=\"av-next av-next--none\">Do 19. 11. není volný termín.</p>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("av-message", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Next_InEnglish()
    {
        await using var app = new VisitorApp();
        app.Lessons.Slots.Add(Lesson(Local(9, 8)));

        Assert.Contains("Next available: Monday 9 November 08:00, Jóga", await app.GetAsync("/?segment=2&view=next&lang=en"), StringComparison.Ordinal);
        Assert.Contains("Nothing available until 19 November.", await app.GetAsync("/?view=next&lang=en"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Next_Outage_IsMessage_NotSentence()
    {
        await using var app = new VisitorApp();
        app.Catalog.Failure = AvailabilityView.Application.Connection.ConnectionState.Unavailable;

        string page = await app.GetAsync("/?view=next");

        Assert.Contains("av-message--error", page, StringComparison.Ordinal);
        Assert.DoesNotContain("av-next", page, StringComparison.Ordinal);
    }

    private static string[] Weekdays(string page) =>
        [.. WeekdayRegex().Matches(page).Select(match => match.Groups["name"].Value)];

    private static string[] Cells(string page) =>
        [.. CellRegex().Matches(page).Select(match => match.Value)];

    private static string Day(string page, int novemberDay) =>
        Assert.Single(Cells(page), cell => NumberRegex().Match(cell).Groups["number"].Value == novemberDay.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [GeneratedRegex("<abbr title=\"[^\"]+\">(?<name>[^<]+)</abbr>")]
    private static partial Regex WeekdayRegex();

    [GeneratedRegex("<td class=\"av-month__day.*?</td>", RegexOptions.Singleline)]
    private static partial Regex CellRegex();

    [GeneratedRegex("av-month__number\" aria-hidden=\"true\">(?<number>\\d+)<")]
    private static partial Regex NumberRegex();
}
