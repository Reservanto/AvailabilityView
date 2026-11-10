using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AvailabilityView.Web.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AvailabilityView.Web.Tests;

/// <summary>
/// Generátor ukáže jen veřejné objekty a jen jejich Id a název. Katalog je z nahrávek
/// produkčního API, čte ho skutečný klient a katalog z Infrastructure. Do nahrávek test podstrčí neveřejné zaměření,
/// provozovnu, službu a zdroj s kontakty.
/// </summary>
public sealed partial class GeneratorPublicDataTests
{
    private const string Secret = "Tajn";
    private const string SecretEmail = "tajny@example.cz";
    private const string SecretPhone = "+420 777 000 111";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryCombination_ShowsOnlyPublicIdsAndNames()
    {
        var api = new RecordedApi();
        PlantPrivateObjects(api);
        await using WebApplicationFactory<Program> app = TestApp.Create(token: "ltt-testu", services: api.Use());
        using HttpClient client = TestApp.Client(app);

        List<string> pages = [await GetAsync(client, "/Generator")];
        foreach (string segment in Values(pages[0], "segment"))
        {
            string ofSegment = await GetAsync(client, $"/Generator?segment={segment}");
            pages.Add(ofSegment);
            foreach (string location in Values(ofSegment, "location"))
            {
                pages.Add(await GetAsync(client, $"/Generator?segment={segment}&location={location}"));
            }
        }

        // Kontrola, že stránky opravdu ukazují katalog z nahrávek.
        Assert.True(Values(pages[0], "segment").Length > 1);
        Assert.Contains(pages, page => page.Contains("Zdroj 10173", StringComparison.Ordinal));
        Assert.All(pages, page =>
        {
            Assert.DoesNotContain(Secret, page, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretEmail, page, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretPhone, page, StringComparison.Ordinal);
            Assert.DoesNotContain("REDACTED", page, StringComparison.Ordinal); // kontakty z nahrávek
            Assert.DoesNotContain("Popis zdroje", page, StringComparison.Ordinal);
            Assert.DoesNotContain("Ulice 1", page, StringComparison.Ordinal); // adresa provozovny
            Assert.DoesNotContain("Popis služby", page, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Planting_ReallyAddsPrivateObjects()
    {
        // Negativní kontrola testu výše: podstrčené objekty jsou v odpovědích API, ne jen v názvu testu.
        var api = new RecordedApi();
        PlantPrivateObjects(api);

        Assert.All(RecordedApi.Catalog, list =>
            Assert.Contains(api.Items(list.Endpoint, list.List), item => item["State"]?.GetValue<string>() == "Private"));
    }

    // Neveřejné objekty, které by se bez kontroly veřejnosti v nabídce opravdu ukázaly: patří k zaměření a provozovně
    // se zdroji a službami. Kopie nového objektu má nové Id i ve všech polích s Id, jinak ji katalog vyřadí jako
    // duplicitu.
    private static void PlantPrivateObjects(RecordedApi api)
    {
        MakePrivate(api.Item("Segment/GetList", "Items", 6), "CustomizedName");
        MakePrivate(api.Item("Location/GetList", "Items", 10151), "Name");
        MakePrivate(api.Item("BookingService/GetList", "BookingServices", 15649), "Name");

        // Neveřejný zdroj ve veřejné provozovně a veřejném zaměření, s kontakty. Poskytuje veřejnou službu, jinak by ho
        // nabídka vynechala i bez kontroly veřejnosti.
        JsonObject resource = api.CopyOf("BookingResource/GetList", "Items", 10173);
        resource["BookingResource"] = 999_003;
        resource["Id"] = 999_003;
        MakePrivate(resource, "Name");
        api.Add("BookingResource/GetList", "Items", resource);
        ((JsonArray)api.Item("BookingService/GetList", "BookingServices", 4948)["BookingResourceIds"]!).Add(999_003);
    }

    private static void MakePrivate(JsonObject item, string nameField)
    {
        item[nameField] = $"{Secret}ý objekt {item["Id"]}";
        item["State"] = "Private";
        if (item.ContainsKey("Email"))
        {
            item["Email"] = SecretEmail;
            item["Phone"] = SecretPhone;
        }
    }

    private static async Task<string> GetAsync(HttpClient client, string url)
    {
        using HttpResponseMessage response = await client.GetAsync(new Uri(url, UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private static string[] Values(string page, string name)
    {
        string select = Regex.Match(page, $"<select[^>]*name=\"{name}\">(?<options>.*?)</select>", RegexOptions.Singleline).Groups["options"].Value;
        return [.. ValueRegex().Matches(select).Select(match => match.Groups["value"].Value)];
    }

    [GeneratedRegex("<option value=\"(?<value>[0-9]+)\"")]
    private static partial Regex ValueRegex();
}
