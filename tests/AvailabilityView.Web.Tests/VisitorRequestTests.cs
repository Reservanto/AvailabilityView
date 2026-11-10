using AvailabilityView.Application.Availability;
using AvailabilityView.Web.Availability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace AvailabilityView.Web.Tests;

/// <summary>Převod parametrů URL na dotaz (<c>docs/SPEC.md</c> sekce 5).</summary>
public sealed class VisitorRequestTests
{
    private static readonly AvailabilitySettings _settings = new(TimeZoneInfo.Utc, 60, "cs");

    [Fact]
    public void NoParameters_GiveDefaults()
    {
        VisitorRequest request = Parse("");

        AvailabilityQuery query = request.Query;
        Assert.Equal((null, null, null, null, VisitorRequest.DefaultDays), (query.SegmentId, query.LocationId, query.ServiceId, query.ResourceId, query.Days));
        Assert.Null(query.Range);
        Assert.Empty(query.Pickers);
        Assert.False(query.IsMalformed);
        Assert.Equal([DisplayView.Agenda], request.Views);
        Assert.Equal("cs", request.Language);
    }

    [Fact]
    public void AllParameters_AreRead()
    {
        VisitorRequest request = Parse("?view=calendar,agenda&segment=1&location=2&service=3&resource=4&pickers=location,service&days=30&week=2026-11-18&lang=en");

        AvailabilityQuery query = request.Query;
        Assert.Equal((1, 2, 3, 4, 30), (query.SegmentId, query.LocationId, query.ServiceId, query.ResourceId, query.Days));
        Assert.Equal(new RangeQuery(ShownRange.Week, new DateOnly(2026, 11, 18)), query.Range);
        Assert.Equal([AvailabilityFilter.Location, AvailabilityFilter.Service], query.Pickers.Order());
        Assert.Equal([DisplayView.Calendar, DisplayView.Agenda], request.Views);
        Assert.Equal(DisplayView.Calendar, request.View);
        Assert.Equal("en", request.Language);
        Assert.False(query.IsMalformed);
    }

    [Theory]
    [InlineData("?segment=")]
    [InlineData("?segment")]
    [InlineData("?resource=&service=&location=&days=&week=&view=&pickers=&lang=")]
    public void EmptyValue_IsMissing(string url)
    {
        // Formulář filtrů posílá prázdnou hodnotu za volbu „všechny“.
        VisitorRequest request = Parse(url);

        Assert.False(request.Query.IsMalformed);
        Assert.Null(request.Query.SegmentId);
        Assert.Equal(VisitorRequest.DefaultDays, request.Query.Days);
    }

    [Theory]
    [InlineData("?segment=abc")]
    [InlineData("?segment=1.0")]
    [InlineData("?segment=-1")]
    [InlineData("?segment=+1")]
    [InlineData("?segment=%201")]
    [InlineData("?segment=1%20")]
    [InlineData("?segment=0x10")]
    [InlineData("?segment=99999999999")]
    [InlineData("?location=abc")]
    [InlineData("?service=abc")]
    [InlineData("?resource=abc")]
    [InlineData("?segment=1&segment=2")]
    [InlineData("?segment=1&segment=")]
    [InlineData("?days=abc")]
    [InlineData("?days=-1")]
    [InlineData("?days=1,5")]
    [InlineData("?week=2026-13-01")]
    [InlineData("?week=2026-02-30")]
    [InlineData("?week=18.11.2026")]
    [InlineData("?week=2026-11-18T00:00")]
    [InlineData("?week=26-11-18")]
    [InlineData("?week=2026-1-5")]
    [InlineData("?week=%202026-11-18")]
    [InlineData("?week=2026-11-18%20")]
    [InlineData("?view=list")]
    [InlineData("?view=Agenda")]
    [InlineData("?view=agenda,")]
    [InlineData("?view=agenda,agenda")]
    [InlineData("?view=agenda, calendar")]
    [InlineData("?view=agenda&view=calendar")]
    [InlineData("?pickers=foo")]
    [InlineData("?pickers=location,location")]
    [InlineData("?pickers=location,,service")]
    public void MalformedValue_GivesMalformedQuery(string url)
    {
        Assert.Same(AvailabilityQuery.Malformed, Parse(url).Query);
    }

    [Fact]
    public void MalformedWeek_InAgenda_IsStillMalformed()
    {
        // Odkaz se pak nechová jinak podle toho, které zobrazení je zrovna první.
        Assert.Same(AvailabilityQuery.Malformed, Parse("?view=agenda,calendar&week=abc").Query);
    }

    [Fact]
    public void MalformedValue_KeepsLanguageAndValidViews()
    {
        VisitorRequest request = Parse("?segment=abc&lang=en&view=timegrid");

        Assert.Equal("en", request.Language);
        Assert.Equal([DisplayView.Timegrid], request.Views);
    }

    [Fact]
    public void MalformedView_FallsBackToAgenda()
    {
        Assert.Equal([DisplayView.Agenda], Parse("?view=list").Views);
    }

    [Theory]
    [InlineData("?days=0", 0)] // nula je platný tvar, čtení ji odmítne jako jiné neplatné období
    [InlineData("?days=007", 7)]
    [InlineData("?days=500", 500)] // zkrátí až čtení podle MaxDays
    [InlineData("?days=99999999999", int.MaxValue)]
    public void Days_KeepTheirValue_OverflowMeansAsManyAsPossible(string url, int days)
    {
        Assert.Equal(days, Parse(url).Query.Days);
    }

    [Theory]
    [InlineData("?lang=de")]
    [InlineData("?lang=EN")]
    [InlineData("?lang=en&lang=cs")]
    [InlineData("?lang=")]
    public void UnknownLanguage_GivesDefault_AndIsNotMalformed(string url)
    {
        VisitorRequest request = Parse(url);

        Assert.Equal("cs", request.Language);
        Assert.False(request.Query.IsMalformed);
    }

    [Fact]
    public void DefaultLanguage_ComesFromSettings()
    {
        Assert.Equal("en", Parse("", _settings with { DefaultLanguage = "en" }).Language);
    }

    [Fact]
    public void LanguageFromAddress_WinsOverDefault()
    {
        Assert.Equal("cs", Parse("?lang=cs", _settings with { DefaultLanguage = "en" }).Language);
    }

    [Theory]
    [InlineData("?view=calendar")]
    [InlineData("?view=timegrid,agenda")]
    public void WeekViews_AskForWeek_CurrentWhenMissing(string url)
    {
        Assert.Equal(new RangeQuery(ShownRange.Week, null), Parse(url).Query.Range);
    }

    [Theory]
    [InlineData("?week=2026-11-18")]
    [InlineData("?view=agenda,calendar&week=2026-11-18")]
    public void Agenda_IgnoresWeek(string url)
    {
        Assert.Null(Parse(url).Query.Range);
    }

    [Fact]
    public void AllViews_AndAllPickers_AreKnown()
    {
        VisitorRequest request = Parse("?view=agenda,calendar,timegrid,month,next&pickers=segment,location,service,resource");

        Assert.Equal(Enum.GetValues<DisplayView>(), request.Views);
        Assert.Equal(Enum.GetValues<AvailabilityFilter>(), request.Query.Pickers.Order());
    }

    [Fact]
    public void ParameterNames_IgnoreCase_UnknownParametersAreIgnored()
    {
        // Weby merchantů přidávají do odkazů vlastní parametry, třeba pro měření kampaní.
        VisitorRequest request = Parse("?Segment=5&utm_source=newsletter&fbclid=abc");

        Assert.Equal(5, request.Query.SegmentId);
        Assert.False(request.Query.IsMalformed);
    }

    [Fact]
    public void LinkToView_PutsItFirst_AndKeepsEverythingElse()
    {
        VisitorRequest request = Parse("?view=calendar,agenda,timegrid&segment=2&utm_source=news&lang=en");

        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> link = QueryHelpers.ParseQuery(request.LinkTo(DisplayView.Timegrid));

        Assert.Equal("timegrid,calendar,agenda", link["view"]);
        Assert.Equal(("2", "news", "en"), (link["segment"].ToString(), link["utm_source"].ToString(), link["lang"].ToString()));
    }

    [Fact]
    public void LinkToWeek_ReplacesWeek_AndKeepsEverythingElse()
    {
        VisitorRequest request = Parse("?view=calendar&Week=2026-11-18&q=a%26b%3D");

        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> link = QueryHelpers.ParseQuery(request.LinkToRange(ShownRange.Week, new DateOnly(2026, 11, 9)));

        Assert.Equal(["2026-11-09"], link["week"].ToArray());
        Assert.Equal("calendar", link["view"]);
        Assert.Equal("a&b=", link["q"]);
    }

    [Fact]
    public void Links_AreRelative_SoTheyWorkUnderIisApplicationPath()
    {
        Assert.StartsWith("?", Parse("?view=calendar").LinkToRange(ShownRange.Week, new DateOnly(2026, 11, 9)), StringComparison.Ordinal);
    }

    private static VisitorRequest Parse(string url, AvailabilitySettings? settings = null) =>
        VisitorRequest.From(new QueryCollection(QueryHelpers.ParseQuery(url)), settings ?? _settings);
}
