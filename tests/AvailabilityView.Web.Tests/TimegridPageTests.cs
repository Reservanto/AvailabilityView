using System.Text.RegularExpressions;
using AvailabilityView.Application.Availability;
using AvailabilityView.Web.Tests.Support;
using static AvailabilityView.Web.Tests.Support.VisitorApp;

namespace AvailabilityView.Web.Tests;

/// <summary>
/// Hodinová osa. Je pátek 6. 11. 2026, týden od 9. 11. je celý v nabídce. Osa začíná
/// v 8:00 a má řádky po pěti minutách, 9:00 je řádek 12.
/// </summary>
public sealed partial class TimegridPageTests
{
    private const string Week = "/?view=timegrid&week=2026-11-09";

    [Fact]
    public async Task Individual_ShowsFreeBookedAndUnavailable_OnlyWithinWorkingHours()
    {
        await using var app = new VisitorApp();
        app.Individual.Slots.Add(new(Local(9, 9), Local(9, 12), null, 100, null, false));
        app.Individual.Timeline = new SourceTimeline(
            [new ResourceInterval(100, Local(9, 9), Local(9, 17))],
            [new ResourceInterval(100, Local(9, 14), Local(9, 15))]);

        string page = await app.GetAsync(Week);

        Assert.Equal(
            [
                "free 12 48 9:00–12:00",
                "unavailable 48 72 Nedostupné",
                "busy 72 84 Obsazeno",
                "unavailable 84 108 Nedostupné",
            ],
            Blocks(page));
        Assert.Contains("<span class=\"av-visually-hidden\">Obsazeno 14:00–15:00</span>", page, StringComparison.Ordinal);
        Assert.Equal(["Volno", "Obsazeno", "Nedostupné"], Legend(page));
        Assert.Contains("<li class=\"av-timegrid__item av-timegrid__item--free\">9:00–12:00</li>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Sportovní", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutBookings_LegendHasNoBooked()
    {
        await using var app = new VisitorApp();
        app.Individual.Timeline = new SourceTimeline([new ResourceInterval(100, Local(9, 9), Local(9, 17))], []);

        string page = await app.GetAsync(Week);

        Assert.Equal(["Nedostupné"], Legend(page));
        Assert.Equal(["unavailable 12 108 Nedostupné"], Blocks(page));
    }

    [Fact]
    public async Task Lessons_AreBlocks_FullOnesMarked_SideBySideWhenAtSameTime()
    {
        await using var app = new VisitorApp();
        app.Lessons.Slots.AddRange([Lesson(Local(10, 17)), Lesson(Local(10, 17), "obsazeno", full: true)]);

        string page = await app.GetAsync("/?segment=2&view=timegrid&week=2026-11-09");

        Assert.Equal(
            ["lesson 108 120 17:00–18:00 Jóga Sál 6/12", "lesson-full 108 120 17:00–18:00 Jóga Sál obsazeno"],
            Blocks(page));
        Assert.Contains("av-lane-0", page, StringComparison.Ordinal);
        Assert.Contains("av-lane-1", page, StringComparison.Ordinal);
        Assert.Contains("av-lanes-2", page, StringComparison.Ordinal);
        Assert.Contains("17:00–18:00 Jóga Sál obsazeno, plně obsazeno", page, StringComparison.Ordinal);
        Assert.DoesNotContain("av-legend", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task English_TranslatesStatesAndTimes()
    {
        await using var app = new VisitorApp();
        app.Individual.Timeline = new SourceTimeline(
            [new ResourceInterval(100, Local(9, 9), Local(9, 17))],
            [new ResourceInterval(100, Local(9, 14), Local(9, 15))]);

        string page = await app.GetAsync(Week + "&lang=en");

        Assert.Equal(["Booked", "Unavailable"], Legend(page));
        Assert.Contains("Booked 14:00–15:00", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Positions_UseClasses_NotInlineStyles()
    {
        // CSP bez unsafe-inline.
        await using var app = new VisitorApp();
        app.Individual.Timeline = new SourceTimeline([new ResourceInterval(100, Local(9, 9), Local(9, 17))], []);

        string page = await app.GetAsync(Week);

        Assert.DoesNotContain("style=", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AxisFollowsBlocks_OutsideUsualHours()
    {
        await using var app = new VisitorApp();
        app.Individual.Timeline = new SourceTimeline([new ResourceInterval(100, Local(9, 6, 30), Local(9, 20))], []);

        string page = await app.GetAsync(Week);

        // Osa 6:00–20:00, úsek od řádku 6 do konce.
        Assert.Equal(["unavailable 6 168 Nedostupné"], Blocks(page));
        Assert.Contains(">6:00</span>", page, StringComparison.Ordinal);
        Assert.Contains(">20:00</span>", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LessonInRepeatedHourOfFallBack_IsDrawn()
    {
        // 31. 10. 2027 se ve 3:00 letního času vrací na 2:00. Lekce 2:30 letního až 2:30 zimního
        // času trvá hodinu, na místních hodinách ale nulu.
        await using var app = new VisitorApp();
        app.Time.SetUtcNow(new DateTimeOffset(2027, 10, 27, 8, 0, 0, TimeSpan.Zero));
        app.Lessons.Slots.Add(Lesson(new DateTimeOffset(2027, 10, 31, 2, 30, 0, TimeSpan.FromHours(2))));

        string page = await app.GetAsync("/?segment=2&view=timegrid&week=2027-10-25");

        Assert.Equal(["lesson 6 18 2:30–2:30 Jóga Sál 6/12"], Blocks(page));
        Assert.Contains("<li class=\"av-timegrid__item av-timegrid__item--lesson\">2:30–2:30 Jóga Sál 6/12</li>", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FreeTimeOverMidnight_HasTextOfItsDay()
    {
        // Rozhodnutí uživatele 2026-11-09: úsek přes půlnoc nese v každém dni svou část, stejně jako agenda.
        await using var app = new VisitorApp();
        app.Individual.Slots.Add(new(Local(9, 22), Local(10, 2), null, 100, null, false));
        app.Individual.Timeline = new SourceTimeline([new ResourceInterval(100, Local(9, 22), Local(10, 2))], []);

        string page = await app.GetAsync(Week);

        Assert.Equal(["free 264 288 22:00–24:00", "free 0 24 0:00–2:00"], Blocks(page));
    }

    [Fact]
    public async Task BookingInRepeatedHourOfFallBack_DoesNotCoverFreeTime()
    {
        // Rezervace 2:30 letního až 2:30 zimního času nakreslená podle skutečné délky
        // by překryla volno 2:30–4:00 zimního času u téhož zdroje.
        await using var app = new VisitorApp();
        app.Time.SetUtcNow(new DateTimeOffset(2027, 10, 27, 8, 0, 0, TimeSpan.Zero));
        var summer = TimeSpan.FromHours(2);
        var winter = TimeSpan.FromHours(1);
        app.Individual.Slots.Add(new(new(2027, 10, 31, 2, 30, 0, winter), new(2027, 10, 31, 4, 0, 0, winter), null, 100, null, false));
        app.Individual.Timeline = new SourceTimeline(
            [new ResourceInterval(100, new(2027, 10, 31, 1, 0, 0, summer), new(2027, 10, 31, 4, 0, 0, winter))],
            [new ResourceInterval(100, new(2027, 10, 31, 2, 30, 0, summer), new(2027, 10, 31, 2, 30, 0, winter))]);

        string page = await app.GetAsync("/?view=timegrid&week=2027-10-25");

        Assert.Equal(["unavailable 0 18 Nedostupné", "free 18 36 2:30–4:00"], Blocks(page));
    }

    [Fact]
    public async Task LessonOverMidnight_IsNotDrawnOnDayOutsideOffer()
    {
        // S days=1 je sobota mimo nabídku, lekce z pátku do ní nesmí zasáhnout.
        await using var app = new VisitorApp();
        app.Lessons.Slots.Add(Lesson(Local(6, 23, 30)));

        string page = await app.GetAsync("/?segment=2&view=timegrid&days=1");

        Assert.Equal(["lesson 186 192 23:30–0:30 Jóga Sál 6/12"], Blocks(page));
        Assert.Single(Regex.Matches(page, "av-timegrid__item--lesson"));
    }

    private static string[] Blocks(string page) =>
        [.. BlockPattern().Matches(page).Select(match =>
            $"{match.Groups["kind"].Value} {match.Groups["from"].Value} {match.Groups["to"].Value} {System.Net.WebUtility.HtmlDecode(match.Groups["text"].Value)}")];

    private static string[] Legend(string page) =>
        [.. LegendPattern().Matches(page).Select(match => match.Groups[1].Value)];

    [GeneratedRegex("av-block av-block--(?<kind>[a-z-]+) av-at-(?<from>\\d+) av-to-(?<to>\\d+)[^>]*>\\s*<span class=\"av-block__text\" aria-hidden=\"true\">(?<text>[^<]*)</span>")]
    private static partial Regex BlockPattern();

    [GeneratedRegex("<li class=\"av-legend__item[^\"]*\">([^<]*)</li>")]
    private static partial Regex LegendPattern();
}
