using System.Collections.Specialized;
using System.Web;
using Reservanto.ApiClient.Authorization;

namespace Reservanto.ApiClient.Tests;

/// <summary>Adresa souhlasu a tvar tokenu (<c>docs/SPEC.md</c> sekce 6).</summary>
public sealed class MerchantConnectTests
{
    private static readonly Uri _api = new("https://api.test.invalid/");
    private static readonly Uri _callback = new("https://localhost:5001/Auth/Callback");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsentUrl_LeadsToConsentPage_WithClientRightsAndRedirect(bool occupancy)
    {
        Uri url = MerchantConnect.BuildConsentUrl(_api, " klient-testu ", _callback, occupancy);
        NameValueCollection query = HttpUtility.ParseQueryString(url.Query);

        Assert.Equal("https://api.test.invalid/v1/Authorize/GetLongTimeToken", url.GetLeftPart(UriPartial.Path));
        Assert.Equal("klient-testu", query["clientId"]);
        Assert.Equal(_callback.AbsoluteUri, query["redirectUrl"]);
        Assert.Equal(MerchantConnect.RightsFor(occupancy), query.GetValues("rights"));
        Assert.Equal(occupancy, query.GetValues("rights")!.Contains("Event_r"));
    }

    [Fact]
    public void ConsentUrl_WithBaseUrlWithoutSlash_KeepsPath()
    {
        Uri url = MerchantConnect.BuildConsentUrl(new Uri("https://api.test.invalid/vnoreno"), "klient", _callback, occupancy: false);

        Assert.Equal("/vnoreno/v1/Authorize/GetLongTimeToken", url.AbsolutePath);
    }

    [Fact]
    public void ConsentUrl_EscapesClientId_SoItCannotAddParameters()
    {
        Uri url = MerchantConnect.BuildConsentUrl(_api, "x&rights=Customer_w#", _callback, occupancy: false);
        NameValueCollection query = HttpUtility.ParseQueryString(url.Query);

        Assert.Equal("x&rights=Customer_w#", query["clientId"]);
        Assert.DoesNotContain("Customer_w", query.GetValues("rights")!);
        Assert.Empty(url.Fragment);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ConsentUrl_WithoutClientId_Fails(string clientId)
    {
        Assert.Throws<ArgumentException>(() => MerchantConnect.BuildConsentUrl(_api, clientId, _callback, occupancy: false));
    }

    [Theory]
    [InlineData("abcdef0123456789")]
    [InlineData("abc_def-ghi.jkl0123")]
    public void TokenShape_AcceptsPlainTokens(string token)
    {
        Assert.True(MerchantConnect.HasLongTimeTokenShape(token));
    }

    [Fact]
    public void TokenShape_AcceptsGuid()
    {
        Assert.True(MerchantConnect.HasLongTimeTokenShape(Guid.NewGuid().ToString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kratky")]
    [InlineData("abcdef0123456789\"; rm -rf ~; \"")]
    [InlineData("abcdef0123456789$(whoami)")]
    [InlineData("abcdef0123456789`whoami`")]
    [InlineData("abcdef0123456789 mezera")]
    [InlineData("abcdef0123456789\n")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("abcdef0123456789\",\"BaseUrl\":\"https://cizi.invalid/")]
    public void TokenShape_RefusesAnythingElse(string? token)
    {
        Assert.False(MerchantConnect.HasLongTimeTokenShape(token));
    }

    [Fact]
    public void TokenShape_RefusesOverlongValue()
    {
        Assert.False(MerchantConnect.HasLongTimeTokenShape(new string('a', 513)));
    }
}
