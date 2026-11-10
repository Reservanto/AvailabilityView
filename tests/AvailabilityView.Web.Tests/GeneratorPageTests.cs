using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Connection;
using AvailabilityView.Tests.Shared;
using AvailabilityView.Web.Tests.Support;

namespace AvailabilityView.Web.Tests;

/// <summary>
/// Generátor odkazu. Katalog z <see cref="VisitorApp"/>: masáže v Praze, lekce v Brně,
/// půjčovna nepodporovaná.
/// </summary>
public sealed partial class GeneratorPageTests
{
    [Fact]
    public async Task NotConnected_LinksToSetup()
    {
        await using var app = new VisitorApp(connected: false);

        string page = await app.GetAsync("/Generator");

        Assert.Contains("Instance zatím není připojená k Reservantu.", page, StringComparison.Ordinal);
        Assert.Contains("href=\"/Setup\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<iframe", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unavailable_SaysWhy()
    {
        await using var app = new VisitorApp();
        app.Catalog.Failure = ConnectionState.Unavailable;

        string page = await app.GetAsync("/Generator");

        Assert.Contains("Data z Reservanta teď nejde načíst.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<iframe", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Default_OffersOnlySupportedSegments_AndFixesSegmentAndLocation()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/Generator");

        Assert.Equal(["1:Masáže", "2:Lekce"], Options(page, "segment"));
        Assert.Equal(["10:Praha"], Options(page, "location"));
        Assert.Equal(":Všechny", Assert.Single(Options(page, "service"), option => option.StartsWith(':')));
        Assert.Equal("https://localhost/?segment=1&location=10", Link(page));
        Assert.Equal("/?segment=1&location=10", Preview(page));
        Assert.Equal(
            "<iframe src=\"https://localhost/?segment=1&amp;location=10\" title=\"Volné termíny\" width=\"100%\" height=\"600\" style=\"border: 0;\"></iframe>\n" +
            "<script src=\"https://localhost/embed.js\" async></script>",
            Code(page));
    }

    [Fact]
    public async Task LowerChoice_FollowsHigher_AndStaleValueIsReplaced()
    {
        await using var app = new VisitorApp();

        // Bez JavaScriptu může správce změnit zaměření a nechat provozovnu, která k němu nepatří.
        string page = await app.GetAsync("/Generator?segment=2&location=10&service=1001");

        Assert.Equal(["20:Brno"], Options(page, "location"));
        Assert.Contains("value=\"2\" selected=\"selected\"", page, StringComparison.Ordinal);
        Assert.Equal("https://localhost/?segment=2&location=20", Link(page));
    }

    [Fact]
    public async Task ChosenServiceAndResource_GoToLink()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/Generator?segment=1&service=1001&resource=100");

        Assert.Equal("https://localhost/?segment=1&location=10&service=1001&resource=100", Link(page));
        Assert.Contains("value=\"1001\" selected=\"selected\"", page, StringComparison.Ordinal);
        Assert.Contains("value=\"100\" selected=\"selected\"", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/Generator?segment=999")]
    [InlineData("/Generator?segment=3")]
    [InlineData("/Generator?resource=abc")]
    public async Task UnknownOrUnsupportedValue_StartsFromDefaults(string url)
    {
        await using var app = new VisitorApp();

        Assert.Equal("https://localhost/?segment=1&location=10", Link(await app.GetAsync(url)));
    }

    [Fact]
    public async Task ViewsPickersDaysAndLanguage_GoToLink()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/Generator?view=month&also=next&also=calendar&also=month&also=nic&segment=2&pickers=service&pickers=location&days=60&lang=en");

        Assert.Equal("https://localhost/?view=month,calendar,next&segment=2&location=20&pickers=location,service&days=60&lang=en", Link(page));
        Assert.Contains("title=\"Free slots\"", Code(page), StringComparison.Ordinal);
        Assert.Contains("value=\"month\" selected=\"selected\"", page, StringComparison.Ordinal);
        Assert.Matches("name=\"also\"\\s+value=\"calendar\" checked=\"checked\"", page);
        Assert.Matches("name=\"also\"\\s+value=\"next\" checked=\"checked\"", page);
        Assert.DoesNotMatch("name=\"also\"\\s+value=\"month\"", page); // výchozí není mezi dalšími
        Assert.DoesNotMatch("name=\"also\"\\s+value=\"agenda\" checked", page);
    }

    [Theory]
    [InlineData("0", "&days=1")]
    [InlineData("999", "&days=60")]
    [InlineData("14", "")]
    [InlineData("x", "")]
    public async Task Days_StayWithinLimits(string days, string expected)
    {
        await using var app = new VisitorApp();

        Assert.Equal($"https://localhost/?segment=1&location=10{expected}", Link(await app.GetAsync($"/Generator?days={days}")));
    }

    [Fact]
    public async Task DefaultLanguageOfInstance_IsNotWritten()
    {
        await using var app = new VisitorApp(settings: new Dictionary<string, string?> { ["AvailabilityView:DefaultLanguage"] = "en" });

        Assert.Equal("https://localhost/?segment=1&location=10&lang=cs", Link(await app.GetAsync("/Generator?lang=cs")));
        Assert.Equal("https://localhost/?segment=1&location=10", Link(await app.GetAsync("/Generator?lang=en")));
    }

    [Fact]
    public async Task UnderIisApplication_LinkAndPreviewKeepPathBase()
    {
        await using var app = new VisitorApp(services: TestApp.UnderPathBase("/volno"));

        string page = await app.GetAsync("/volno/Generator");

        Assert.Equal("https://localhost/volno/?segment=1&location=10", Link(page));
        Assert.Equal("/volno/?segment=1&location=10", Preview(page));
        Assert.Contains("href=\"/volno/css/setup.css\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preview_IsTheVisitorPageOfLink()
    {
        await using var app = new VisitorApp();
        app.Lessons.Slots.Add(VisitorApp.Lesson(VisitorApp.Local(6, 17)));

        string page = await app.GetAsync("/Generator?segment=2&view=next");
        string preview = await app.GetAsync(Preview(page));

        Assert.Equal(new Uri(Link(page)).PathAndQuery, Preview(page));
        Assert.Contains("Nejbližší volný termín: dnes 17:00, Jóga", preview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generator_IsCzech_EvenWithLanguageOfLink()
    {
        await using var app = new VisitorApp();

        string page = await app.GetAsync("/Generator?lang=en");

        Assert.Contains("<html lang=\"cs\">", page, StringComparison.Ordinal);
        Assert.Contains("Generátor odkazu", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NamesWithMarkup_AreText_InPageAndInCode()
    {
        await using var app = new VisitorApp();
        app.Catalog.Value = VisitorApp.DefaultCatalog with
        {
            Locations = [new(10, "<script>alert(1)</script>"), .. VisitorApp.DefaultCatalog.Locations.Skip(1)],
        };

        string page = await app.GetAsync("/Generator");

        Assert.DoesNotContain("<script>alert", page, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", page, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryTextKeyOfGenerator_Exists()
    {
        // Chybějící klíč by Razor vypsal jako název klíče. Skládané klíče test pohledu nenajde, proto jsou vyjmenované.
        string web = Path.Combine(RepositoryRoot.Path, "src", "AvailabilityView.Web");
        HashSet<string> czech = [.. XDocument.Load(Path.Combine(web, "Resources", "SharedResource.resx")).Root!.Elements("data").Select(data => data.Attribute("name")!.Value)];
        string[] used =
        [
            .. Regex.Matches(File.ReadAllText(Path.Combine(web, "Views", "Generator", "Index.cshtml")), "Text\\[\"([A-Za-z.]+)\"").Select(match => match.Groups[1].Value),
            .. new[] { AvailabilityOutcome.NotConnected, AvailabilityOutcome.SourceUnavailable, AvailabilityOutcome.UnsupportedSegment, AvailabilityOutcome.ViewNotAvailable }
                .Select(outcome => $"Generator.Message.{outcome}"),
            .. AvailabilitySettings.Languages.Select(language => $"Generator.Language.{language}"),
            .. Enum.GetNames<ConnectionState>().Where(state => state != nameof(ConnectionState.Connected)).Select(state => $"Setup.Status.{state}.Explain"),
        ];

        Assert.Contains("Generator.Title", used);
        Assert.All(used, key => Assert.Contains(key, czech));
    }

    private static string[] Options(string page, string name)
    {
        string select = Regex.Match(page, $"<select[^>]*name=\"{name}\">(?<options>.*?)</select>", RegexOptions.Singleline).Groups["options"].Value;
        return [.. OptionRegex().Matches(select).Select(match => $"{match.Groups["value"].Value}:{WebUtility.HtmlDecode(match.Groups["text"].Value)}")];
    }

    private static string Link(string page) => WebUtility.HtmlDecode(Pre(page, "generator-link"));

    private static string Code(string page) => WebUtility.HtmlDecode(Pre(page, "generator-code"));

    private static string Preview(string page) => WebUtility.HtmlDecode(PreviewRegex().Match(page).Groups["src"].Value);

    private static string Pre(string page, string id) =>
        Regex.Match(page, $"<pre class=\"av-setup__value\" id=\"{id}\">(?<value>[^<]*)</pre>").Groups["value"].Value;

    [GeneratedRegex("<option value=\"(?<value>[^\"]*)\"[^>]*>(?<text>[^<]*)</option>")]
    private static partial Regex OptionRegex();

    [GeneratedRegex("<iframe class=\"av-generator__preview\" src=\"(?<src>[^\"]+)\"")]
    private static partial Regex PreviewRegex();
}
