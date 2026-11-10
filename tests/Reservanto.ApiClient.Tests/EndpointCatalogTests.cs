using System.Reflection;
using System.Text.RegularExpressions;
using AvailabilityView.Tests.Shared;
using Reservanto.ApiClient.Authorization;
using Reservanto.ApiClient.Core;
using Reservanto.ApiClient.Tests.Support;

namespace Reservanto.ApiClient.Tests;

/// <summary>Katalog endpointů: je úplný, jen ke čtení a žádá jen práva ze SPEC.</summary>
public sealed partial class EndpointCatalogTests
{
    private const string OccupancyMarker = "**Jen s volbou obsazenosti:**";

    private static readonly HashSet<string> _specRights = ReadRightsFromSpec(occupancyOnly: null);
    private static readonly HashSet<string> _specOccupancyRights = ReadRightsFromSpec(occupancyOnly: true);
    private static readonly HashSet<string> _specBaseRights = ReadRightsFromSpec(occupancyOnly: false);

    [Fact]
    public void Catalog_ListsEveryDeclaredEndpoint()
    {
        string[] declared = [.. typeof(ApiEndpoints).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(ApiEndpoint))
            .Select(field => ((ApiEndpoint)field.GetValue(null)!).Path)];

        Assert.NotEmpty(declared);
        Assert.Equal(declared.Order(), ApiEndpoints.All.Select(endpoint => endpoint.Path).Order());
    }

    [Fact]
    public void Catalog_EveryApiEndpointHasClientMethod_AndViceVersa()
    {
        string[] methods = [.. ClientMethods.All().Select(pair => ClientMethods.PathOf(pair.Group, pair.Method))];
        string[] endpoints = [.. ApiEndpoints.All.Select(endpoint => endpoint.Path).Where(path => path != ApiEndpoints.AuthorizeGetShortTimeToken.Path)];

        Assert.Equal(endpoints.Order(), methods.Order());
    }

    [Fact]
    public void Catalog_IsReadOnly_AndUsesOnlyRightsFromSpec()
    {
        Assert.Empty(ReadOnlyCatalogRule.Check(ApiEndpoints.All, _specRights));
    }

    [Fact]
    public void Catalog_NeedsEveryRightFromSpec()
    {
        // Právo navíc ve SPEC by merchanta žádalo o přístup, který aplikace nepotřebuje.
        Assert.Equal(_specRights.Order(), ApiEndpoints.All.SelectMany(endpoint => endpoint.Rights).Distinct().Order());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Consent_AsksForExactlyRightsFromSpec(bool occupancy)
    {
        HashSet<string> expected = occupancy ? [.. _specBaseRights, .. _specOccupancyRights] : _specBaseRights;

        Assert.Equal(expected.Order(), MerchantConnect.RightsFor(occupancy).Order());
    }

    [Fact]
    public void Spec_SeparatesOccupancyRight()
    {
        // Bez oddělení by test výše prošel, i kdyby souhlas žádal o rezervace vždy.
        Assert.Equal(["Event_r"], _specOccupancyRights);
        Assert.DoesNotContain("Event_r", _specBaseRights);
    }

    [Fact]
    public void Spec_ListsOnlyReadRights()
    {
        Assert.NotEmpty(_specRights);
        Assert.All(_specRights, right => Assert.EndsWith("_r", right, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/v1/Classes/CreateBooking", "FreeSpace_r")]
    [InlineData("/v1/Customer/Create", "Customer_w")]
    [InlineData("/v1/Customer/GetList", "Customer_r")]
    [InlineData("/v1/Voucher/GetList", "Voucher_r")]
    public void Rule_CatchesWriteActionOrForeignRight(string path, string right)
    {
        Assert.NotEmpty(ReadOnlyCatalogRule.Check([new ApiEndpoint(path, right)], _specRights));
    }

    // occupancyOnly: null = všechna práva, true = jen věta za značkou obsazenosti, false = jen část před ní.
    // Další věty řádku práva jen vysvětlují.
    private static HashSet<string> ReadRightsFromSpec(bool? occupancyOnly)
    {
        string spec = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "docs", "SPEC.md"));
        string line = spec.Split('\n').Single(l => l.StartsWith("**Žádaná práva:**", StringComparison.Ordinal));
        int marker = line.IndexOf(OccupancyMarker, StringComparison.Ordinal);
        string part = occupancyOnly switch
        {
            null => line,
            true => line[marker..line.IndexOf(". ", marker, StringComparison.Ordinal)],
            false => line[..marker],
        };
        return [.. RightPattern().Matches(part).Select(match => match.Groups[1].Value)];
    }

    [GeneratedRegex("`(\\w+_\\w+)`")]
    private static partial Regex RightPattern();
}
