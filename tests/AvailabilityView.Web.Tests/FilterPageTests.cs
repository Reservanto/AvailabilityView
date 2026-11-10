using System.Text.RegularExpressions;
using AvailabilityView.Application.Availability;
using AvailabilityView.Web.Tests.Support;
using static AvailabilityView.Web.Tests.Support.VisitorApp;

namespace AvailabilityView.Web.Tests;

/// <summary>Filtry z <c>pickers</c> na stránce návštěvníka.</summary>
public sealed partial class FilterPageTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/?pickers=")]
    [InlineData("/?location=10")]
    public async Task WithoutPickers_HasNoForm(string path)
    {
        await using var app = new VisitorApp();

        Assert.DoesNotContain("av-filters", await app.GetAsync(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pickers_OfferOnlySupportedPublicOptions_InDependencyOrder()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/?pickers=location,segment");

        // Půjčovna je nepodporovaný typ, Brno k masážím nepatří.
        Assert.Equal(["segment", "location"], Selects(page));
        Assert.Equal(["1:Masáže:selected", "2:Lekce"], Options(page, "segment"));
        Assert.Equal(["10:Praha:selected"], Options(page, "location"));
        Assert.Contains("<label class=\"av-filters__label\" for=\"av-filter-segment\">Zaměření</label>", page, StringComparison.Ordinal);
        Assert.Contains("<button class=\"av-filters__submit\" type=\"submit\">Zobrazit</button>", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceAndResource_OfferAll_SelectedWhenNothingChosen()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/?pickers=service,resource");

        Assert.Equal([":Všechny:selected", "1001:Sportovní"], Options(page, "service"));
        Assert.Equal([":Všechny:selected", "100:Jana"], Options(page, "resource"));
    }

    [Fact]
    public async Task ChosenValue_IsSelected()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/?pickers=service&service=1001");

        Assert.Equal([":Všechny", "1001:Sportovní:selected"], Options(page, "service"));
    }

    [Fact]
    public async Task SegmentChangedWithStaleLocation_DropsLocation_AndShowsNewSegment()
    {
        // Bez JavaScriptu formulář pošle i provozovnu, která k novému zaměření nepatří (SPEC sekce 5).
        await using var app = new VisitorApp();
        app.Lessons.Slots.Add(Lesson(Local(6, 17)));

        string page = await app.GetAsync("/?pickers=segment,location&segment=2&location=10");

        Assert.Equal(["1:Masáže", "2:Lekce:selected"], Options(page, "segment"));
        Assert.Equal(["20:Brno:selected"], Options(page, "location"));
        Assert.Contains("17:00–18:00", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Form_KeepsOtherParameters_AsHidden_WithoutPickedOnes()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/?view=calendar,agenda&pickers=location&Location=10&segment=1&days=7&lang=cs&utm_source=web%26x");

        Assert.Equal(
            ["view=calendar,agenda", "pickers=location", "segment=1", "days=7", "lang=cs", "utm_source=web&amp;x"],
            Hidden(page));
        Assert.Matches("<form class=\"av-filters\" method=\"get\" aria-label=\"Filtry\" data-av-filters>", page);
        Assert.DoesNotContain("action=", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FixedValueOutsidePickers_IsHidden_AndLimitsOptions()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/?pickers=segment&location=20");

        // Pevná provozovna Brno: masáže v ní nejsou, nabízí se jen lekce.
        Assert.Equal(["2:Lekce:selected"], Options(page, "segment"));
        Assert.Contains("location=20", Hidden(page));
        Assert.Equal(["segment"], Selects(page));
    }

    [Fact]
    public async Task NothingFree_StillShowsFilters()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/?pickers=segment");

        Assert.Contains("av-message", page, StringComparison.Ordinal);
        Assert.Equal(["1:Masáže:selected", "2:Lekce"], Options(page, "segment"));
    }

    [Theory]
    [InlineData("/?pickers=segment&location=999")]
    [InlineData("/?pickers=segment&days=x")]
    [InlineData("/?pickers=segment&segment=3")]
    public async Task ViewNotAvailableOrUnsupported_HasNoForm(string path)
    {
        await using var app = new VisitorApp();

        Assert.DoesNotContain("av-filters", await app.GetAsync(path), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/?pickers=segment&view=calendar")]
    [InlineData("/?pickers=segment&view=timegrid")]
    public async Task EveryView_ShowsFilters(string path)
    {
        await using var app = new VisitorApp();

        Assert.Equal(["segment"], Selects(await app.GetAsync(path)));
    }

    [Fact]
    public async Task English_TranslatesLabels()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/?pickers=segment,service&lang=en");

        Assert.Contains("aria-label=\"Filters\"", page, StringComparison.Ordinal);
        Assert.Contains(">Category</label>", page, StringComparison.Ordinal);
        Assert.Contains(">Service</label>", page, StringComparison.Ordinal);
        Assert.Contains(">All</option>", page, StringComparison.Ordinal);
        Assert.Contains(">Show</button>", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NamesWithMarkup_AreText()
    {
        await using var app = new VisitorApp();
        app.Catalog.Value = DefaultCatalog with
        {
            Segments = [new PublicSegment(1, "<script>alert(1)</script>", "OneToOne"), new PublicSegment(2, "Lekce\" onclick=\"x", "Classes")],
        };

        string page = await app.GetAsync("/?pickers=segment");

        Assert.DoesNotContain("<script>alert", page, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", page, StringComparison.Ordinal);
        Assert.DoesNotContain("\" onclick=\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Page_LoadsScript_WithNonce()
    {
        await using var app = new VisitorApp();

        Assert.Matches("<script type=\"module\" src=\"/js/availability.js\" nonce=\"[0-9A-F]+\"></script>", await app.GetAsync("/?pickers=segment"));
    }

    private static string[] Selects(string page) =>
        [.. SelectPattern().Matches(page).Select(match => match.Groups["name"].Value)];

    private static string[] Options(string page, string name)
    {
        Match select = Regex.Match(page, $"<select [^>]*name=\"{name}\">(?<body>.*?)</select>", RegexOptions.Singleline);
        Assert.True(select.Success, $"Filtr {name} na stránce není.");
        return [.. OptionPattern().Matches(select.Groups["body"].Value)
            .Select(match => $"{match.Groups["value"].Value}:{match.Groups["text"].Value}{(match.Groups["selected"].Success ? ":selected" : string.Empty)}")];
    }

    private static string[] Hidden(string page) =>
        [.. HiddenPattern().Matches(page).Select(match => $"{match.Groups["name"].Value}={match.Groups["value"].Value}")];

    [GeneratedRegex("<select class=\"av-filters__select\" id=\"av-filter-(?<name>[a-z]+)\" name=\"\\k<name>\">")]
    private static partial Regex SelectPattern();

    [GeneratedRegex("<option value=\"(?<value>[^\"]*)\"(?<selected> selected=\"selected\")?>(?<text>[^<]*)</option>")]
    private static partial Regex OptionPattern();

    [GeneratedRegex("<input type=\"hidden\" name=\"(?<name>[^\"]*)\" value=\"(?<value>[^\"]*)\">")]
    private static partial Regex HiddenPattern();
}
