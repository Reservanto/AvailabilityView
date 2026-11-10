using Newtonsoft.Json.Linq;
using Reservanto.ApiClient.Tests.Support;

namespace Reservanto.ApiClient.Tests;

/// <summary>
/// Nahrávač: z rezervací zůstanou jen pole, která aplikace čte, protože ostatní nesou údaje zákazníků. Názvy z účtu
/// merchanta se nahradí neutrálním textem.
/// </summary>
public sealed class RecorderTests
{
    [Fact]
    public void Names_AreReplacedByTypeAndId_IdsAndEmptyTextsStay()
    {
        var body = JToken.Parse("""
            {"BookingServices":[{"Id":7,"Name":"Masáž u Jany","Description":"","SegmentId":3,"SegmentName":"Wellness",
            "CategoryId":null,"CategoryName":null,"AvailablePriceLevels":[{"Id":216,"Name":"VIP","Price":0}],"Duration":60}]}
            """);

        JToken neutral = ApiRecorder.Neutralize("BookingService/GetList", body);

        var service = (JObject)neutral["BookingServices"]![0]!;
        Assert.Equal("Služba 7", (string?)service["Name"]);
        Assert.Equal(string.Empty, (string?)service["Description"]);
        Assert.Equal("Zaměření 3", (string?)service["SegmentName"]);
        Assert.Null((string?)service["CategoryName"]);
        Assert.Equal("Cenová hladina 216", (string?)service["AvailablePriceLevels"]![0]!["Name"]);
        Assert.Equal(60, (int)service["Duration"]!);
    }

    [Fact]
    public void Address_IsReplacedByFixedText()
    {
        var body = JToken.Parse("""{"Items":[{"Id":2,"Name":"Pobočka Brno","Address":{"Street":"Dlouhá 12","City":"Brno","ZipCode":"602 00","Country":"Česká republika"}}]}""");

        var address = (JObject)ApiRecorder.Neutralize("Location/GetList", body)["Items"]![0]!["Address"]!;

        Assert.Equal(["Ulice 1", "Město", "000 00", "Česká republika"], address.Properties().Select(property => (string?)property.Value));
    }

    [Fact]
    public void AddressNameRegionAndPosition_AreCleared()
    {
        var body = JToken.Parse("""{"Items":[{"Id":2,"Address":{"Name":"Areál Pod Lesem","State":"Liberecký kraj","Latitude":50.7663,"Longitude":15.0543}}]}""");

        var address = (JObject)ApiRecorder.Neutralize("Location/GetList", body)["Items"]![0]!["Address"]!;

        Assert.All(["Name", "State", "Latitude", "Longitude"], field => Assert.Equal(JTokenType.Null, address[field]!.Type));
    }

    [Fact]
    public void Calendar_GetsExampleIdAndBookingAddress()
    {
        var body = JToken.Parse("""
            {"Items":[{"Id":27032410,"CalendarId":5678,"CalendarName":"Výchozí kalendář",
            "BookingUrl":"https://booking.reservanto.cz/Modal?id=5678&appId=27032410"}]}
            """);

        var item = (JObject)ApiRecorder.Neutralize("Classes/GetAvailableAppointments", body)["Items"]![0]!;

        Assert.Equal(1234, (int)item["CalendarId"]!);
        Assert.Equal("Kalendář 1234", (string?)item["CalendarName"]);
        Assert.Equal("https://booking.reservanto.cz/Modal?id=1234", (string?)item["BookingUrl"]);
        Assert.Equal(27032410, (int)item["Id"]!);
    }

    [Theory]
    [InlineData("""{"CalendarId":5678,"LocationId":2023}""", "1234")]
    [InlineData("""{"CalendarId":null,"LocationId":2023}""", "")]
    public void RequestCalendar_GetsExampleId(string request, string expected)
    {
        JToken neutral = ApiRecorder.NeutralizeRequest("Classes/GetAvailableAppointments", JToken.Parse(request));

        Assert.Equal(expected, neutral["CalendarId"]!.ToString());
        Assert.Equal(2023, (int)neutral["LocationId"]!);
    }

    [Fact]
    public void NameWithoutId_GetsTextWithoutTrailingSpace()
    {
        var body = JToken.Parse("""{"BookingServices":[{"Id":7,"CategoryId":null,"CategoryName":"Bez kategorie"}]}""");

        Assert.Equal("Kategorie", (string?)ApiRecorder.Neutralize("BookingService/GetList", body)["BookingServices"]![0]!["CategoryName"]);
    }

    [Fact]
    public void OtherEndpoints_StayUntouched()
    {
        var body = JToken.Parse("""{"Items":[{"Id":2,"Name":"Text"}]}""");

        Assert.Equal("Text", (string?)ApiRecorder.Neutralize("OneToOne/GetAvailableStartsForLocation", body)["Items"]![0]!["Name"]);
    }

    [Fact]
    public void Events_KeepOnlyFieldsTheAppReads()
    {
        var body = JToken.Parse("""
            {"IsError":false,"Events":[{"AppointmentId":1,"SourceId":2,"StartDate":3,"EndDate":4,"Status":"Confirmed",
            "CustomerFullName":"Jan Novák","CustomerNote":"alergie","Price":500,"CustomValues":[{"Value":"x"}],"MerchantNote":null}]}
            """);

        JToken trimmed = ApiRecorder.TrimEvents(body);

        JObject item = Assert.IsType<JObject>(Assert.Single((JArray)trimmed["Events"]!));
        Assert.Equal(["AppointmentId", "EndDate", "SourceId", "StartDate", "Status"], item.Properties().Select(property => property.Name).Order());
        Assert.DoesNotContain("Novák", trimmed.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void EventError_KeepsNoTextsOrFieldsTheAppDoesNotRead()
    {
        // Chybová odpověď nemá Events, ořezání ji dřív propustilo celou.
        var body = JToken.Parse("""
            {"IsError":true,"ErrorMessage":"Jan Novák: alergie","ErrorMessages":["Jan Novák"],"ErrorParameters":["Interval"],
            "Events":null,"Customer":{"Name":"Jan Novák"}}
            """);

        JToken trimmed = ApiRecorder.TrimEvents(body);

        Assert.DoesNotContain("Novák", trimmed.ToString(), StringComparison.Ordinal);
        Assert.Null(trimmed["Customer"]);
        Assert.Equal("Interval", trimmed["ErrorParameters"]![0]!.ToString());
    }

    [Theory]
    [InlineData("""{"IsError":true,"ErrorMessages":"Jan Novák: alergie","Events":null}""")]
    [InlineData("""{"IsError":true,"ErrorMessage":["Jan Novák"],"Events":null}""")]
    [InlineData("""{"IsError":true,"ErrorMessages":[{"Text":"Jan Novák"}],"Events":null}""")]
    public void EventError_InUnexpectedShape_KeepsNoText(string json)
    {
        // Nahrávač běží před čtením odpovědi, tvar mu nikdo nezaručí.
        JToken trimmed = ApiRecorder.TrimEvents(JToken.Parse(json));

        Assert.DoesNotContain("Novák", trimmed.ToString(), StringComparison.Ordinal);
    }
}
