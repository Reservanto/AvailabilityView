using System.Reflection;
using System.Xml.Linq;
using AvailabilityView.Tests.Shared;
using AvailabilityView.Web.Availability;
using AvailabilityView.Web.Generator;

namespace AvailabilityView.Web.Tests;

/// <summary>Překlady stránky návštěvníka a co jde do prohlížeče.</summary>
public sealed class VisitorTextTests
{
    private static readonly string _resources = Path.Combine(RepositoryRoot.Path, "src", "AvailabilityView.Web", "Resources");

    // Přidání pole je rozhodnutí uživatele (profil projektu), seznam se proto mění jen s jeho souhlasem.
    private static readonly Dictionary<Type, string[]> _allowedFields = new()
    {
        [typeof(VisitorPage)] = ["Agenda", "Calendar", "Filters", "Message", "Month", "Next", "ServiceHeading", "Timegrid", "Views"],
        // Filtry: Id a název veřejných objektů a parametry adresy, kterou návštěvník už má.
        [typeof(FilterForm)] = ["Fields", "Kept"],
        [typeof(FilterField)] = ["AllowsAll", "Filter", "Name", "Options"],
        [typeof(FilterChoice)] = ["Id", "IsSelected", "Name"],
        [typeof(KeptParameter)] = ["Name", "Value"],
        [typeof(AgendaModel)] = ["Days"],
        [typeof(AgendaDay)] = ["Heading", "IsToday", "Slots"],
        // Odkazy na týdny a zobrazení nesou parametry adresy, kterou návštěvník už má.
        [typeof(ViewLink)] = ["IsCurrent", "Url", "View"],
        [typeof(CalendarModel)] = ["Days", "Nav"],
        // Navigace týdnů a měsíců na jednom místě, odkazy jako dřív.
        [typeof(RangeNav)] = ["NextUrl", "PreviousUrl", "Title", "Unit"],
        [typeof(CalendarDay)] = ["Heading", "IsOffered", "IsToday", "Slots"],
        [typeof(SlotModel)] = ["IsFull", "OccupancyText", "ResourceName", "ServiceName", "Time"],
        // Hodinová osa: pozice v řádcích osy, druh úseku a hotové texty.
        [typeof(TimegridModel)] = ["Days", "Hours", "Legend", "Nav", "Rows"],
        [typeof(TimegridHour)] = ["Label", "Row"],
        [typeof(TimegridDay)] = ["Blocks", "Heading", "IsOffered", "IsToday", "Lanes", "Listed"],
        [typeof(TimegridBlock)] = ["Description", "From", "Kind", "Lane", "Text", "To"],
        [typeof(TimegridItem)] = ["Kind", "Text"],
        // Měsíc a nejbližší termín (rozhodnutí uživatele 2026-11-09): stav dne, hotové texty
        // a odkazy na týden se stejnými parametry.
        [typeof(MonthModel)] = ["Legend", "Nav", "Weekdays", "Weeks"],
        [typeof(MonthWeekday)] = ["Abbreviation", "Full"],
        [typeof(MonthWeek)] = ["Days"],
        [typeof(MonthDay)] = ["Description", "IsToday", "Kind", "Number", "Url"],
        [typeof(MonthKey)] = ["Kind", "Text"],
        [typeof(NextModel)] = ["Found", "Text"],
        // Generátor: volby správce, nabídka filtrů jako výše a odkaz z nich.
        [typeof(GeneratorPage)] = ["Failure", "Filters", "Form", "MaxDays", "Message", "Output"],
        [typeof(GeneratorForm)] = ["Also", "Days", "Language", "LocationId", "Pickers", "ResourceId", "SegmentId", "ServiceId", "View"],
        [typeof(GeneratorOutput)] = ["Code", "Link", "PreviewUrl"],
    };

    [Fact]
    public void EveryVisitorText_HasCzechAndEnglish()
    {
        Dictionary<string, string> czech = Texts("SharedResource.resx");
        Dictionary<string, string> english = Texts("SharedResource.en.resx");

        string[] visitorKeys = [.. czech.Keys.Where(IsVisitorKey).Order(StringComparer.Ordinal)];
        Assert.NotEmpty(visitorKeys);
        Assert.Equal(visitorKeys, english.Keys.Order(StringComparer.Ordinal));
        Assert.All(english.Values.Concat(czech.Values), value => Assert.False(string.IsNullOrWhiteSpace(value)));
    }

    [Fact]
    public void EveryPageMessage_HasText()
    {
        Dictionary<string, string> czech = Texts("SharedResource.resx");

        Assert.All(Enum.GetNames<PageMessage>(), name => Assert.Contains($"Visitor.Message.{name}", czech.Keys));
    }

    [Fact]
    public void EveryView_HasText()
    {
        Dictionary<string, string> czech = Texts("SharedResource.resx");

        Assert.All(Enum.GetNames<DisplayView>(), name => Assert.Contains($"Visitor.View.{name}", czech.Keys));
    }

    [Fact]
    public void EveryRangeAndDayState_HasText()
    {
        // Klíče se skládají za běhu, kontrola textů v pohledech je nenajde.
        Dictionary<string, string> czech = Texts("SharedResource.resx");
        string[] keys =
        [
            .. Enum.GetNames<AvailabilityView.Application.Availability.ShownRange>()
                .SelectMany(unit => new[] { $"Visitor.{unit}.Navigation", $"Visitor.{unit}.Previous", $"Visitor.{unit}.Next" }),
            .. Enum.GetNames<AvailabilityView.Application.Availability.DayState>().Select(state => $"Visitor.Day.{state}"),
            "Visitor.Day.NoLessons", "Visitor.Format.Month", "Visitor.Format.NextDay", "Visitor.Format.Until",
            "Visitor.Next.Found", "Visitor.Next.None", "Visitor.Next.Today", "Visitor.Next.Tomorrow",
        ];

        Assert.All(keys, key => Assert.Contains(key, czech.Keys));
    }

    [Fact]
    public void EveryFilter_HasText()
    {
        Dictionary<string, string> czech = Texts("SharedResource.resx");

        Assert.All(Enum.GetNames<AvailabilityView.Application.Availability.AvailabilityFilter>(), name => Assert.Contains($"Visitor.Filter.{name}", czech.Keys));
    }

    [Fact]
    public void EveryTextKeyInVisitorViews_Exists()
    {
        // Chybějící klíč by Razor vypsal jako název klíče, test stránky by si toho nevšiml.
        Dictionary<string, string> czech = Texts("SharedResource.resx");
        string views = string.Concat(
            string.Concat(Directory.GetFiles(Path.Combine(RepositoryRoot.Path, "src", "AvailabilityView.Web", "Views", "Visitor"), "*.cshtml").Select(File.ReadAllText)),
            File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src", "AvailabilityView.Web", "Views", "Shared", "_Layout.cshtml")),
            File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src", "AvailabilityView.Web", "Security", "RequestLimits.cs")),
            File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src", "AvailabilityView.Web", "Availability", "VisitorController.cs")));

        string[] used = [.. System.Text.RegularExpressions.Regex.Matches(views, "\\[\"([A-Za-z.]+)\"\\]").Select(match => match.Groups[1].Value)];

        Assert.NotEmpty(used);
        Assert.All(used, key => Assert.Contains(key, czech.Keys));
    }

    [Fact]
    public void EnglishFile_HasNoSetupTexts()
    {
        // Průvodce zůstává česky (SPEC sekce 6), anglický text by se ukázal jen napůl.
        Assert.DoesNotContain(Texts("SharedResource.en.resx").Keys, key => !IsVisitorKey(key));
    }

    public static TheoryData<Type> OutputTypes => [.. _allowedFields.Keys];

    [Theory]
    [MemberData(nameof(OutputTypes))]
    public void PageModel_HasOnlyAllowedFields(Type type)
    {
        string[] fields = [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Where(name => name != "EqualityContract")
            .Order(StringComparer.Ordinal)];

        Assert.Equal(_allowedFields[type], fields);
    }

    [Theory]
    [InlineData(typeof(VisitorPage), typeof(SlotModel))]
    [InlineData(typeof(GeneratorPage), typeof(FilterChoice))]
    public void EveryTypeReachableFromPageModel_IsInAllowList(Type page, Type deep)
    {
        HashSet<Type> reachable = [];
        Reach(page, reachable);

        Assert.Contains(deep, reachable);
        Assert.Empty(reachable.Where(type => !_allowedFields.ContainsKey(type)).Select(type => type.Name));
    }

    private static bool IsVisitorKey(string key) => key.StartsWith("Visitor.", StringComparison.Ordinal) || key == "Page.Title";

    private static Dictionary<string, string> Texts(string file) =>
        XDocument.Load(Path.Combine(_resources, file)).Root!.Elements("data")
            .ToDictionary(data => data.Attribute("name")!.Value, data => data.Element("value")!.Value, StringComparer.Ordinal);

    private static void Reach(Type type, HashSet<Type> reachable)
    {
        if (type.Assembly != typeof(VisitorPage).Assembly || type.IsEnum || !reachable.Add(type))
        {
            return;
        }

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Type propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            foreach (Type candidate in propertyType.IsGenericType ? propertyType.GetGenericArguments() : [propertyType])
            {
                Reach(candidate, reachable);
            }
        }
    }
}
