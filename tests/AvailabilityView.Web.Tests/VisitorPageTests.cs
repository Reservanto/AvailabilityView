using System.Net;
using AvailabilityView.Application.Connection;
using AvailabilityView.Web.Tests.Support;
using static AvailabilityView.Web.Tests.Support.VisitorApp;

namespace AvailabilityView.Web.Tests;

/// <summary>Stránka návštěvníka: agenda, hlášky a jazyk.</summary>
public sealed class VisitorPageTests
{
    [Fact]
    public async Task Lessons_AreListedByDay_WithTimeServiceResourceAndOccupancy()
    {
        await using var app = new VisitorApp();
        app.Lessons.Slots.AddRange([Lesson(Local(9, 7), "obsazeno", full: true), Lesson(Local(6, 17))]);

        string page = await app.GetAsync("/?segment=2");

        Assert.Contains("pátek 6. listopadu", page, StringComparison.Ordinal);
        Assert.Contains("<span class=\"av-agenda__date-today\">dnes</span>", page, StringComparison.Ordinal);
        Assert.Contains("17:00–18:00", page, StringComparison.Ordinal);
        Assert.Contains("Jóga<span class=\"av-slot__resource\">Sál</span>", page, StringComparison.Ordinal);
        Assert.Contains("<span class=\"av-slot__occupancy\">6/12</span>", page, StringComparison.Ordinal);
        Assert.Contains(">7:00–8:00<", page, StringComparison.Ordinal);
        Assert.True(page.IndexOf("pátek 6. listopadu", StringComparison.Ordinal) < page.IndexOf("pondělí 9. listopadu", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SlotAfterLocalMidnight_BelongsToNextLocalDay()
    {
        // V UTC je ještě pátek 23:30.
        await using var app = new VisitorApp();
        app.Lessons.Slots.Add(Lesson(Local(7, 0, 30)));

        string page = await app.GetAsync("/?segment=2");

        Assert.Contains("sobota 7. listopadu", page, StringComparison.Ordinal);
        Assert.DoesNotContain("pátek", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FullLesson_StaysVisible_AndIsMarked()
    {
        await using var app = new VisitorApp();
        app.Lessons.Slots.Add(Lesson(Local(9, 7), "obsazeno", full: true));

        string page = await app.GetAsync("/?segment=2");

        Assert.Contains("<li class=\"av-slot av-slot--full\">", page, StringComparison.Ordinal);
        Assert.Contains("obsazeno<span class=\"av-visually-hidden\">, plně obsazeno</span>", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FullSlotWithoutText_SaysFullVisibly()
    {
        await using var app = new VisitorApp();
        app.Lessons.Slots.Add(Lesson(Local(9, 7), occupancy: null, full: true));

        Assert.Contains("<span class=\"av-slot__occupancy\">plně obsazeno</span>", await app.GetAsync("/?segment=2"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FreeSlotWithoutText_HasNoOccupancy()
    {
        await using var app = new VisitorApp();
        app.Individual.Slots.Add(new(Local(6, 10), Local(6, 10, 30), 1001, 100, null, false));

        string page = await app.GetAsync("/");

        Assert.Contains("10:00–10:30", page, StringComparison.Ordinal);
        Assert.Contains("<span class=\"av-slot__resource\">Jana</span>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("av-slot__occupancy", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/?view=calendar")]
    public async Task FreeTimeOfResource_HasNoServiceName(string path)
    {
        // Individuální typy ukazují volno zdrojů, název služby ne.
        await using var app = new VisitorApp();
        app.Individual.Slots.Add(new(Local(6, 10), Local(6, 12), null, 100, null, false));

        string page = await app.GetAsync(path);

        Assert.Contains("10:00–12:00", page, StringComparison.Ordinal);
        Assert.Contains("Jana", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Sportovní", page, StringComparison.Ordinal);
        Assert.DoesNotContain("av-service", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/", "av-agenda__day", "pátek 6. listopadu", "sobota 7. listopadu", "neděle 8. listopadu")]
    [InlineData("/?view=calendar", "av-calendar__day", "pá 6. 11.", "so 7. 11.", "ne 8. 11.")]
    public async Task FreeTimeOverMidnight_IsShownByDays_EndingAt24(string path, string dayClass, string friday, string saturday, string sunday)
    {
        // Rozhodnutí uživatele 2026-11-09: úsek od pátku 22:00 do neděle 2:00 má v každém dni svou část.
        await using var app = new VisitorApp();
        app.Individual.Slots.Add(new(Local(6, 22), Local(8, 2), null, 100, null, false));

        string page = await app.GetAsync(path);

        string[] days = [.. page.Split($"<section class=\"{dayClass}").Skip(1)];
        string Day(string heading) => Assert.Single(days, day => day.Contains(heading, StringComparison.Ordinal));
        Assert.Contains("22:00–24:00", Day(friday), StringComparison.Ordinal);
        Assert.Contains("0:00–24:00", Day(saturday), StringComparison.Ordinal);
        Assert.Contains("0:00–2:00", Day(sunday), StringComparison.Ordinal);
        Assert.DoesNotContain("22:00–2:00", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/?service=1001")]
    [InlineData("/?service=1001&view=calendar")]
    public async Task ChosenService_IsNamedOnceAboveFreeTime(string path)
    {
        await using var app = new VisitorApp();
        app.Individual.Slots.AddRange([
            new(Local(6, 10), Local(6, 12), null, 100, null, false),
            new(Local(6, 14), Local(6, 15), null, 100, null, false),
        ]);

        string page = await app.GetAsync(path);

        Assert.Contains("<p class=\"av-service\">Sportovní</p>", page, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(page, "Sportovní"));
    }

    [Fact]
    public async Task LessonsWithChosenService_KeepNamePerLesson_WithoutHeading()
    {
        await using var app = new VisitorApp();
        app.Lessons.Slots.Add(Lesson(Local(6, 17)));

        string page = await app.GetAsync("/?segment=2&service=1002");

        Assert.Contains("Jóga", page, StringComparison.Ordinal);
        Assert.DoesNotContain("av-service", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/?segment=2&service=1002")]
    [InlineData("/?segment=2&service=1002&view=calendar")]
    [InlineData("/?segment=2&service=1002&view=timegrid")]
    public async Task LessonsWithChosenService_AndNothingFree_HaveNoHeading(string path)
    {
        // Bez termínů se nad hláškou ukázal název služby, jako by šlo o volno zdroje.
        await using var app = new VisitorApp();

        string page = await app.GetAsync(path);

        Assert.Contains("av-message", page, StringComparison.Ordinal);
        Assert.DoesNotContain("av-service", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/?service=1001")]
    [InlineData("/?service=1001&view=timegrid")]
    public async Task FreeTimeWithChosenService_AndNothingFree_KeepsHeading(string path)
    {
        await using var app = new VisitorApp();

        Assert.Contains("<p class=\"av-service\">Sportovní</p>", await app.GetAsync(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task English_ChangesTextsDatesTimesAndLangAttribute()
    {
        await using var app = new VisitorApp();
        app.Lessons.Slots.AddRange([Lesson(Local(6, 17)), Lesson(Local(9, 7), "obsazeno", full: true)]);

        string page = await app.GetAsync("/?segment=2&lang=en");

        Assert.Contains("<html lang=\"en\">", page, StringComparison.Ordinal);
        Assert.Contains("Friday 6 November", page, StringComparison.Ordinal);
        Assert.Contains(">today</span>", page, StringComparison.Ordinal);
        Assert.Contains("07:00–08:00", page, StringComparison.Ordinal);
        Assert.Contains(", fully booked", page, StringComparison.Ordinal);
        Assert.Contains("<title>Free slots</title>", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/?lang=de")]
    [InlineData("/?lang=")]
    [InlineData("/")]
    public async Task UnknownOrMissingLanguage_GivesDefault(string url)
    {
        await using var app = new VisitorApp();

        Assert.Contains("<html lang=\"cs\">", await app.GetAsync(url), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DefaultLanguage_ComesFromConfiguration()
    {
        await using var app = new VisitorApp(settings: new Dictionary<string, string?> { ["AvailabilityView:DefaultLanguage"] = "en" });

        Assert.Contains("<html lang=\"en\">", await app.GetAsync("/"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BrowserLanguage_IsIgnored_AndNoCookieIsSet()
    {
        await using var app = new VisitorApp();

        using HttpResponseMessage response = await app.SendAsync("/", acceptLanguage: "en-GB,en;q=0.9");

        Assert.Contains("<html lang=\"cs\">", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("/Setup?lang=en", null)]
    [InlineData("/Setup?culture=en-GB&ui-culture=en-GB", null)]
    [InlineData("/Setup", "en-GB,en;q=0.9")]
    public async Task SetupPage_StaysCzech_WhateverTheRequestSays(string url, string? acceptLanguage)
    {
        await using var app = new VisitorApp(connected: false);

        using HttpResponseMessage response = await app.SendAsync(url, acceptLanguage);

        Assert.Contains("<html lang=\"cs\">", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Europe/Prague", "17:00–18:00")]
    [InlineData("Europe/London", "16:00–17:00")]
    public async Task Times_AreInMerchantTimeZone(string timeZone, string expected)
    {
        await using var app = new VisitorApp(settings: new Dictionary<string, string?> { ["AvailabilityView:TimeZone"] = timeZone });
        app.Lessons.Slots.Add(Lesson(new DateTimeOffset(2026, 11, 6, 16, 0, 0, TimeSpan.Zero)));

        Assert.Contains(expected, await app.GetAsync("/?segment=2"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/?segment=2", "V tomto období nejsou žádné volné termíny.")]
    [InlineData("/?segment=2&lang=en", "There are no free slots in this period.")]
    [InlineData("/?segment=999", "Tento pohled není dostupný.")]
    [InlineData("/?segment=999&lang=en", "This view is not available.")]
    [InlineData("/?segment=3", "Tento druh nabídky stránka zatím neumí zobrazit.")]
    [InlineData("/?segment=3&lang=en", "This page cannot show this kind of offer yet.")]
    public async Task EveryOutcome_HasMessage_InBothLanguages(string url, string message)
    {
        await using var app = new VisitorApp();

        Assert.Contains($"<p class=\"av-message\">{message}</p>", await app.GetAsync(url), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/", "Tato stránka ještě není připojená k Reservantu.", "Připojit účet Reservanto")]
    [InlineData("/?lang=en", "This page is not connected to Reservanto yet.", "Connect a Reservanto account")]
    public async Task NotConnected_LinksToSetup(string url, string message, string link)
    {
        await using var app = new VisitorApp(connected: false);

        string page = await app.GetAsync(url);

        Assert.Contains(message, page, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"/Setup\">{link}</a>", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connected_DoesNotShowToken()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/");

        Assert.DoesNotContain(Token, page, StringComparison.Ordinal);
        Assert.DoesNotContain("Setup", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ConnectionState.Unavailable, "/", "Dostupnost teď nejde načíst. Zkuste to prosím za chvíli.")]
    [InlineData(ConnectionState.TokenRejected, "/?lang=en", "Availability cannot be loaded right now. Please try again shortly.")]
    [InlineData(ConnectionState.MissingRights, "/", "Dostupnost teď nejde načíst. Zkuste to prosím za chvíli.")]
    public async Task Outage_SaysSo_WithoutDetailsOrOldData(ConnectionState failure, string url, string message)
    {
        await using var app = new VisitorApp();
        app.Lessons.Slots.Add(Lesson(Local(6, 17)));
        await app.GetAsync("/?segment=2");
        app.Catalog.Failure = failure;

        string page = await app.GetAsync(url + (url.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "segment=2");

        Assert.Contains($"<p class=\"av-message av-message--error\">{message}</p>", page, StringComparison.Ordinal);
        Assert.DoesNotContain(failure.ToString(), page, StringComparison.Ordinal);
        Assert.DoesNotContain("Jóga", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Setup", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotAvailable_LooksTheSame_ForMalformedUnknownAndUnrelatedValues()
    {
        // Odpověď nesmí prozradit, jestli objekt existuje (SPEC sekce 5).
        await using var app = new VisitorApp();
        string[] urls = ["/?segment=999", "/?segment=abc", "/?segment=2&location=10", "/?resource=100&service=1002", "/?days=0", "/?segment=1&segment=2"];

        string[] pages = [.. await Task.WhenAll(urls.Select(app.GetComparableAsync))];

        Assert.Contains("Tento pohled není dostupný.", pages[0], StringComparison.Ordinal);
        Assert.All(pages, page => Assert.Equal(pages[0], page));
    }

    [Fact]
    public async Task NamesWithMarkup_AreRenderedAsText()
    {
        await using var app = new VisitorApp();
        app.Catalog.Value = DefaultCatalog with
        {
            Services = [.. DefaultCatalog.Services.Select(item => item.Id == 1002 ? item with { Name = "<script>alert(1)</script>" } : item)],
        };
        app.Lessons.Slots.Add(Lesson(Local(6, 17), occupancy: "<b>2/10</b>"));

        string page = await app.GetAsync("/?segment=2");

        Assert.DoesNotContain("<script>alert", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", page, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SlotOfPrivateResourceOrService_IsNotOnPage()
    {
        await using var app = new VisitorApp();
        app.Lessons.Slots.AddRange([new(Local(6, 17), Local(6, 18), 1002, 777, "1/5", false), new(Local(6, 19), Local(6, 20), 778, 102, "1/5", false)]);

        // Bez nonce: náhodné šestnáctkové číslo v něm občas obsahuje 777 nebo 778 a test by selhal náhodně.
        string page = await app.GetComparableAsync("/?segment=2");

        Assert.Contains("V tomto období nejsou žádné volné termíny.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("777", page, StringComparison.Ordinal);
        Assert.DoesNotContain("778", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryState_Returns200()
    {
        // IIS by stránku s chybovým kódem mohl nahradit vlastní a ve webu merchanta by zmizela hláška.
        await using var app = new VisitorApp();
        app.Catalog.Failure = ConnectionState.Unavailable;

        using HttpResponseMessage response = await app.SendAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
