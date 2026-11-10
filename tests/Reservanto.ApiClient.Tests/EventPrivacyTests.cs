using AvailabilityView.Tests.Shared;
using Newtonsoft.Json;
using Reservanto.ApiClient.Core;
using Reservanto.ApiClient.Endpoints;
using Reservanto.ApiClient.Models;
using Reservanto.ApiClient.Tests.Support;

namespace Reservanto.ApiClient.Tests;

/// <summary>
/// Rezervace nesou údaje zákazníků (<c>docs/SPEC.md</c> sekce 10). Klient z nich nesmí nic podržet: ani v modelu,
/// ani v logu, ani v textu chyby.
/// </summary>
public sealed class EventPrivacyTests
{
    private const string CustomerName = "Zakaznik Testovaci-Jmeno";
    private const string CustomerNote = "poznamka-zakaznika-123";
    private const string CustomValue = "vlastni-pole-zakaznika";
    private const string PaymentText = "platba-kartou-zakaznika";

    private static readonly string[] _markers = [CustomerName, CustomerNote, CustomValue, PaymentText, "9876543"];

    // Rezervace se všemi poli z OpenAPI, údaje zákazníka vyplněné značkami.
    private const string FullEvent = $$"""
        {
          "AppointmentId": 11, "BookingServiceId": 12, "CalendarId": 13, "CourseId": null, "CustomerId": 9876543,
          "InternalSourceId": null, "LocationId": 14, "SourceId": 15, "CalendarName": "Kalendar",
          "Count": 1, "CustomerFullName": "{{CustomerName}}", "CustomerNote": "{{CustomerNote}}",
          "CustomValues": [{ "Id": 1, "BooleanValue": null, "DateValue": null, "Definition": { "Id": 2 }, "StringValue": "{{CustomValue}}" }],
          "EndDate": 1794222000, "InternalSourceName": null, "IsPaid": true, "LocationName": "Provozovna",
          "MerchantNote": "{{CustomerNote}}", "NoShowStatus": 0, "PaidPartOfPrice": 500.0,
          "PaymentMethodFormatted": "{{PaymentText}}", "Price": 500.0, "SegmentName": "Zamereni",
          "ServiceName": "Sluzba", "SourceName": "Zdroj", "StartDate": 1794218400, "Status": "Confirmed"
        }
        """;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Event_KeepsOnlyOccupancyFields_AndNoCustomerValue(bool strict)
    {
        using var api = new TestApi(strict: strict);
        api.Api.Respond(ApiEndpoints.EventGetList.Path, System.Net.HttpStatusCode.OK, $$"""{"IsError":false,"Events":[{{FullEvent}}]}""");

        IReadOnlyList<EventApiModel> events = await api.Client.Event.GetListAsync(new EventListRequest(), TestContext.Current.CancellationToken);

        EventApiModel read = Assert.Single(events);
        Assert.Equal((15, 14, AppointmentStatus.Confirmed), (read.SourceId, read.LocationId, read.Status));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1794218400), read.StartDate);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1794222000), read.EndDate);
        string kept = JsonConvert.SerializeObject(events);
        Assert.All(_markers, marker => Assert.DoesNotContain(marker, kept, StringComparison.Ordinal));
        AssertNotLogged(api);
    }

    [Fact]
    public async Task UnreadableEvent_ErrorAndLog_CarryNoCustomerValue()
    {
        // Nové pole shodí přísné čtení. Chyba ani log nesmí nést hodnoty z odpovědi.
        using var api = new TestApi();
        string broken = FullEvent.Replace("\"Count\": 1,", "\"Count\": 1, \"NovePole\": 1,", StringComparison.Ordinal);
        api.Api.Respond(ApiEndpoints.EventGetList.Path, System.Net.HttpStatusCode.OK, $$"""{"IsError":false,"Events":[{{broken}}]}""");

        ReservantoApiException exception = await Assert.ThrowsAsync<ReservantoApiException>(
            () => api.Client.Event.GetListAsync(new EventListRequest(), TestContext.Current.CancellationToken));

        Assert.Contains("NovePole", exception.ToString(), StringComparison.Ordinal);
        Assert.All(_markers, marker => Assert.DoesNotContain(marker, exception.ToString(), StringComparison.Ordinal));
        AssertNotLogged(api);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.OK)]
    [InlineData(System.Net.HttpStatusCode.BadRequest)]
    public async Task ErrorTextsOfEvents_AreNeitherKeptNorLogged_EvenAtTrace(System.Net.HttpStatusCode status)
    {
        // Texty chyb jiných endpointů jdou na Debug a výjimka je nese. U rezervací
        // mohou obsahovat údaje zákazníka, výjimka je proto nesmí převzít vůbec, ani u chybového HTTP stavu.
        using var api = new TestApi();
        api.Api.Respond(
            ApiEndpoints.EventGetList.Path,
            status,
            $$"""{"IsError":true,"ErrorMessage":"{{CustomerName}}","ErrorMessages":["{{CustomerNote}}"],"ErrorParameters":["Interval"],"Events":null}""");

        ReservantoApiException exception = await Assert.ThrowsAsync<ReservantoApiException>(
            () => api.Client.Event.GetListAsync(new EventListRequest(), TestContext.Current.CancellationToken));

        Assert.Empty(exception.ErrorMessages);
        Assert.All(_markers, marker => Assert.DoesNotContain(marker, exception.ToString(), StringComparison.Ordinal));
        AssertNotLogged(api);
        Assert.All(
            api.Logs.GetSnapshot().Select(record => record.Exception).OfType<ReservantoApiException>(),
            logged => Assert.Empty(logged.ErrorMessages));
    }

    [Fact]
    public void DiscardedMembers_AreInOpenApi_ButNotInModels()
    {
        // Pole zákazníka, které by se objevilo v modelu, by se četlo do paměti aplikace.
        var document = OpenApiDocument.Load(
            Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "tests", "Reservanto.ApiClient.Tests", "OpenApi"), "*.yaml").Single());
        Assert.NotEmpty(DiscardedMembers.ByType);
        foreach ((Type type, IReadOnlyList<string> names) in DiscardedMembers.ByType)
        {
            Assert.All(names, name =>
            {
                Assert.True(document.SchemaHasField(type.Name, name), $"{type.Name}.{name} v OpenAPI není.");
                Assert.Null(type.GetProperty(name));
            });
        }
    }

    [Fact]
    public void EventModel_ReadsOnlyAllowedFields()
    {
        // Povolený seznam: nové pole z rezervace je rozhodnutí uživatele (docs/project-profile.md, Autonomie).
        string[] allowed = ["AppointmentId", "BookingServiceId", "CalendarId", "CourseId", "Count", "EndDate", "InternalSourceId", "LocationId", "SourceId", "StartDate", "Status"];

        Assert.Equal(allowed.Order(), typeof(EventApiModel).GetProperties().Select(property => property.Name).Order());
    }

    private static void AssertNotLogged(TestApi api)
    {
        string log = string.Join('\n', api.Logs.GetSnapshot().Select(record => $"{record.Message} {record.Exception}"));
        Assert.All(_markers, marker => Assert.DoesNotContain(marker, log, StringComparison.Ordinal));
    }
}
