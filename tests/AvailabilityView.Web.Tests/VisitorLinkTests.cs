using AvailabilityView.Application.Availability;
using AvailabilityView.Web.Availability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace AvailabilityView.Web.Tests;

/// <summary>
/// Odkaz z generátoru se přečte na stejný pohled: parametry → adresa →
/// parametry.
/// </summary>
public sealed class VisitorLinkTests
{
    private static readonly AvailabilitySettings _settings = new(TimeZoneInfo.Utc, 60, "cs");

    [Fact]
    public void EveryCombination_ReadsBackToSameView()
    {
        AvailabilityFilter[] filters = Enum.GetValues<AvailabilityFilter>();
        DisplayView[] views = Enum.GetValues<DisplayView>();
        int checkedLinks = 0;
        foreach (DisplayView first in views)
        {
            foreach (int pickerMask in Enumerable.Range(0, 1 << filters.Length))
            {
                foreach (bool withIds in new[] { false, true })
                {
                    foreach ((int days, string? language) in new[] { (VisitorRequest.DefaultDays, (string?)null), (60, "en"), (1, "cs") })
                    {
                        DisplayView[] offered = withIds ? [first, .. views.Where(view => view != first)] : [first];
                        AvailabilityFilter[] pickers = [.. filters.Where(filter => (pickerMask & (1 << (int)filter)) != 0)];
                        AssertReadsBack(new VisitorLink(
                            offered,
                            withIds ? 6 : null,
                            withIds ? 2023 : null,
                            withIds ? 34324 : null,
                            withIds ? 10173 : null,
                            pickers,
                            days,
                            language));
                        checkedLinks++;
                    }
                }
            }
        }

        Assert.Equal(views.Length * 16 * 2 * 3, checkedLinks);
    }

    [Fact]
    public void Defaults_AreNotWritten()
    {
        Assert.Equal(string.Empty, new VisitorLink([DisplayView.Agenda], null, null, null, null, [], VisitorRequest.DefaultDays, null).Query());
    }

    [Fact]
    public void Link_IsReadable_InFixedOrder()
    {
        var link = new VisitorLink(
            [DisplayView.Month, DisplayView.Calendar],
            6,
            2023,
            null,
            10173,
            [AvailabilityFilter.Service, AvailabilityFilter.Location],
            60,
            "en");

        Assert.Equal("?view=month,calendar&segment=6&location=2023&resource=10173&pickers=location,service&days=60&lang=en", link.Query());
    }

    private static void AssertReadsBack(VisitorLink link)
    {
        var request = VisitorRequest.From(new QueryCollection(QueryHelpers.ParseQuery(link.Query())), _settings);

        AvailabilityQuery query = request.Query;
        Assert.False(query.IsMalformed, link.Query());
        Assert.Equal(link.Views, request.Views);
        Assert.Equal(
            (link.SegmentId, link.LocationId, link.ServiceId, link.ResourceId, link.Days),
            (query.SegmentId, query.LocationId, query.ServiceId, query.ResourceId, query.Days));
        Assert.Equal(link.Pickers.Order(), query.Pickers.Order());
        Assert.Equal(link.Language ?? _settings.DefaultLanguage, request.Language);
    }
}
