using System.Text.RegularExpressions;
using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Connection;
using AvailabilityView.Tests.Shared;
using AvailabilityView.Web.Availability;
using AvailabilityView.Web.Tests.Support;
using static AvailabilityView.Web.Tests.Support.VisitorApp;

namespace AvailabilityView.Web.Tests;

/// <summary>
/// Veřejné CSS rozhraní: <c>docs/vzhled.md</c> souhlasí se styly i s vykreslenou
/// stránkou. Třídy skládané za běhu (druh úseku, pozice na ose) jsou vidět jen ve vykreslené stránce, proto se
/// stránky vykreslí ve všech zobrazeních a stavech.
/// </summary>
public sealed partial class AppearanceContractTests
{
    private static readonly string _document = Path.Combine(RepositoryRoot.Path, "docs", "vzhled.md");
    private static readonly string _styles = Path.Combine(RepositoryRoot.Path, "src", "AvailabilityView.Web", "Styles", "availability.scss");

    [Fact]
    public void StyleClasses_AreDocumented()
    {
        Names documented = Document(File.ReadAllText(_document));

        Assert.Empty(Styles(File.ReadAllText(_styles)).Classes.Except(documented.Classes).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void StyleVariables_AreDocumented_AndDocumentedVariablesExist()
    {
        Names documented = Document(File.ReadAllText(_document));
        Names styles = Styles(File.ReadAllText(_styles));

        Assert.Empty(styles.Variables.Except(documented.Variables).Order(StringComparer.Ordinal));
        Assert.Empty(documented.Variables.Except(styles.Variables).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task RenderedClasses_AreDocumented()
    {
        Names documented = Document(File.ReadAllText(_document));

        string[] rendered = [.. RenderedClasses(await RenderAllAsync())];

        Assert.NotEmpty(rendered);
        Assert.Empty(rendered.Except(documented.Classes).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task DocumentedClasses_ExistInStylesOrPage()
    {
        Names documented = Document(File.ReadAllText(_document));
        HashSet<string> existing = [.. Styles(File.ReadAllText(_styles)).Classes, .. RenderedClasses(await RenderAllAsync())];

        Assert.Empty(documented.Classes.Except(existing).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task RenderedPages_HaveNoInlineStyles()
    {
        // CSP inline styly nepovolí, prohlížeč by je potichu zahodil.
        foreach (string page in await RenderAllAsync())
        {
            Assert.DoesNotMatch("<[^>]+\\sstyle=", page);
            Assert.DoesNotContain("<style", page, StringComparison.OrdinalIgnoreCase);
        }
    }

    // Negativní testy: čtení dokumentu, stylů a stránky opravdu najde, co má.
    [Fact]
    public void EveryState_HasDocumentedClass()
    {
        // Třídy stavů se neodvozují z názvů výčtů. Nový stav bez třídy shodí StateClass,
        // třída, která v popisu chybí, shodí tento test i tehdy, když se stav ve vykreslených stránkách neobjeví.
        string document = File.ReadAllText(_document);
        string[] classes =
        [
            .. Enum.GetValues<DayState>().SelectMany(state => new[] { $"av-month__day--{StateClass.Of(state)}", $"av-month__key--{StateClass.Of(state)}" }),
            .. Enum.GetValues<TimeBandState>().SelectMany(state => new[] { $"av-legend__item--{StateClass.Of(state)}", $"av-block--{StateClass.Of(state)}" }),
        ];

        Assert.All(classes, name => Assert.Contains($"`{name}`", document, StringComparison.Ordinal));
    }

    [Fact]
    public void EmbedScriptAndHeightMessage_MatchDocumentation()
    {
        // Adresa skriptu a formát zprávy jsou veřejné rozhraní. Skript i stránka musí používat
        // přesně to, co je zdokumentované.
        string document = File.ReadAllText(_document);
        string scripts = Path.Combine(RepositoryRoot.Path, "src", "AvailabilityView.Web", "Scripts");
        string embed = File.ReadAllText(Path.Combine(scripts, "embed.ts"));
        string page = File.ReadAllText(Path.Combine(scripts, "availability.ts"));

        Assert.Contains("`embed.js`", document, StringComparison.Ordinal);
        Assert.Contains("{ \"type\": \"av:height\", \"height\": 812 }", document, StringComparison.Ordinal);
        Assert.Contains("message.type !== 'av:height'", embed, StringComparison.Ordinal);
        Assert.Contains("postMessage({ type: 'av:height', height }", page, StringComparison.Ordinal);
        Assert.Contains("{ \"type\": \"av:height-request\" }", document, StringComparison.Ordinal);
        Assert.Contains("postMessage({ type: 'av:height-request' }, instance.origin)", embed, StringComparison.Ordinal);
        Assert.Contains(".type === 'av:height-request'", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Reading_FindsClassesVariablesAndPatterns()
    {
        Names styles = Styles(
            """
            // .av-v-komentari
            .av-a, .av-b--c:hover > .av-d__e { color: var(--av-x); --av-y: 1px; margin: 0.85em; }
            @for $i from 0 through 3 { .av-at-#{$i} { --av-at: #{$i}; } }
            """);
        Names document = Document("`--av-x` a `av-a`, `.av-b--c` a `av-at-{N}`, `aria-current=\"page\"`, `data-av-filters`");

        Assert.Equal(["av-a", "av-at-{N}", "av-b--c", "av-d__e"], styles.Classes.Order(StringComparer.Ordinal));
        Assert.Equal(["--av-at", "--av-x", "--av-y"], styles.Variables.Order(StringComparer.Ordinal));
        Assert.Equal(["av-a", "av-at-{N}", "av-b--c"], document.Classes.Order(StringComparer.Ordinal));
        Assert.Equal(["--av-x"], document.Variables);
        Assert.Equal(
            ["av-at-{N}", "av-lane-{N}", "av-slot", "av-slot--full"],
            RenderedClasses(["<li class=\"av-slot av-slot--full\" id=\"av-filter-segment\"><div class=\"av-at-12 av-lane-0 x\">"]).Order(StringComparer.Ordinal));
    }

    private static async Task<string[]> RenderAllAsync()
    {
        List<string> pages = [];

        await using (var app = new VisitorApp())
        {
            // Lekce: dnes, plně obsazená, souběžné lekce v pruzích, přepínač, filtry, týden s dny mimo nabídku.
            app.Lessons.Slots.AddRange([
                Lesson(Local(6, 17)),
                Lesson(Local(7, 10), "obsazeno", full: true),
                Lesson(Local(10, 17)),
                Lesson(Local(10, 17), "obsazeno", full: true),
            ]);
            pages.Add(await app.GetAsync("/?segment=2&view=agenda,calendar,timegrid&pickers=segment,service"));
            pages.Add(await app.GetAsync("/?segment=2&view=calendar,agenda"));
            pages.Add(await app.GetAsync("/?segment=2&view=timegrid&week=2026-11-09"));

            // Měsíc: volno s odkazem na týden, plno, bez lekcí, mimo nabídku, prázdné buňky, dnešek. Nejbližší termín.
            pages.Add(await app.GetAsync("/?segment=2&view=month,calendar"));
            pages.Add(await app.GetAsync("/?segment=2&view=next"));
        }

        await using (var app = new VisitorApp())
        {
            // Individuální typ: volno, obsazeno, nedostupné, legenda, služba nad termíny.
            app.Individual.Slots.Add(new(Local(6, 10), Local(6, 12), null, 100, null, false));
            app.Individual.Timeline = new SourceTimeline(
                [new ResourceInterval(100, Local(6, 9), Local(6, 17))],
                [new ResourceInterval(100, Local(6, 13), Local(6, 14))]);
            pages.Add(await app.GetAsync("/?service=1001&view=timegrid,agenda"));
            pages.Add(await app.GetAsync("/?service=1001&view=agenda"));
            pages.Add(await app.GetAsync("/?view=month"));
        }

        await using (var app = new VisitorApp())
        {
            // Nic volného: věta bez termínu.
            pages.Add(await app.GetAsync("/?view=next"));
        }

        await using (var app = new VisitorApp())
        {
            app.Catalog.Failure = ConnectionState.Unavailable;
            pages.Add(await app.GetAsync("/"));
        }

        await using (var app = new VisitorApp(connected: false))
        {
            pages.Add(await app.GetAsync("/"));
        }

        return [.. pages];
    }

    private static IEnumerable<string> RenderedClasses(IEnumerable<string> pages) =>
        pages.SelectMany(page => ClassAttribute().Matches(page))
            .SelectMany(match => match.Groups["value"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(name => name.StartsWith("av-", StringComparison.Ordinal))
            .Select(name => Position().Replace(name, "${name}-{N}"))
            .Distinct(StringComparer.Ordinal);

    private static Names Styles(string scss)
    {
        string code = Comment().Replace(scss, string.Empty);
        return new(
            [.. StyleClass().Matches(code)
                .Select(match => match.Groups["interpolation"].Success ? $"{match.Groups["name"].Value}{{N}}" : match.Groups["name"].Value)
                .Distinct(StringComparer.Ordinal)],
            [.. Variable().Matches(code).Select(match => match.Value).Distinct(StringComparer.Ordinal)]);
    }

    private static Names Document(string markdown)
    {
        string[] items = [.. Quoted().Matches(markdown).Select(match => match.Groups["item"].Value)];
        return new(
            [.. items.Select(item => DocumentedClass().Match(item)).Where(match => match.Success).Select(match => match.Groups["name"].Value).Distinct(StringComparer.Ordinal)],
            [.. items.Where(item => Variable().Match(item) is { Success: true } match && match.Value == item).Distinct(StringComparer.Ordinal)]);
    }

    private sealed record Names(IReadOnlyList<string> Classes, IReadOnlyList<string> Variables);

    [GeneratedRegex("class=\"(?<value>[^\"]*)\"")]
    private static partial Regex ClassAttribute();

    [GeneratedRegex("^(?<name>av-(?:at|to|lane|lanes))-[0-9]+$")]
    private static partial Regex Position();

    [GeneratedRegex("//[^\\n]*|/\\*.*?\\*/", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex("\\.(?<name>av-[a-zA-Z0-9_-]+)(?<interpolation>#\\{[^}]*\\})?")]
    private static partial Regex StyleClass();

    [GeneratedRegex("--av-[a-z0-9-]*[a-z0-9]")]
    private static partial Regex Variable();

    [GeneratedRegex("`(?<item>[^`]+)`")]
    private static partial Regex Quoted();

    [GeneratedRegex("^\\.?(?<name>av-[a-z0-9_-]+(?:\\{N\\})?)$")]
    private static partial Regex DocumentedClass();
}
