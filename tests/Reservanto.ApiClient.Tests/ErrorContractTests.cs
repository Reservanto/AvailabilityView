using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Reservanto.ApiClient.Core;
using Reservanto.ApiClient.Endpoints;
using Reservanto.ApiClient.Tests.Support;

namespace Reservanto.ApiClient.Tests;

/// <summary>Úspěch = hodnota, každá chyba = <see cref="ReservantoApiException"/>, nikdy tiché <c>null</c>.</summary>
public sealed class ErrorContractTests
{
    private const string LocationPath = "/v1/Location/GetList";

    [Fact]
    public async Task IsErrorResponse_ThrowsWithApiTextsAndParameters()
    {
        using var api = new TestApi();
        api.Api.Respond(LocationPath, HttpStatusCode.OK,
            """{"IsError":true,"ErrorMessage":"Provozovna neexistuje.","ErrorMessages":["Provozovna neexistuje."],"ErrorParameters":["LocationId"]}""");

        ReservantoApiException error = await Assert.ThrowsAsync<ReservantoApiException>(() => GetLocationsAsync(api));

        Assert.True(error.IsErrorResponse);
        Assert.Equal(HttpStatusCode.OK, error.StatusCode);
        Assert.Equal(["Provozovna neexistuje."], error.ErrorMessages);
        Assert.Equal(["LocationId"], error.ErrorParameters);
        Assert.Same(ApiEndpoints.LocationGetList, error.Endpoint);
    }

    [Fact]
    public async Task UnreadableResponse_KeepsItsValuesOutOfExceptionAndLog()
    {
        // Newtonsoft dává hodnotu, kterou nepřečte, do textu výjimky. Odpověď může nést adresu nebo kontakt.
        using var api = new TestApi();
        api.Api.Respond(ApiEndpoints.MerchantGetInfo.Path, HttpStatusCode.OK, """{"IsError":false,"Result":{"Id":1,"Name":"Studio","InvoiceAddress":"Tajná ulice 12"}}""");

        ReservantoApiException error = await Assert.ThrowsAsync<ReservantoApiException>(() => api.Client.Merchant.GetInfoAsync(new EmptyRequest(), CancellationToken.None));

        string logged = string.Join('\n', api.Logs.GetSnapshot().Select(record => $"{record.Message} {record.Exception}"));
        Assert.NotEmpty(api.Logs.GetSnapshot());
        Assert.DoesNotContain("Tajná ulice", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Tajná ulice", logged, StringComparison.Ordinal);
        Assert.Contains("Result.InvoiceAddress", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ErrorTexts_AreNotPartOfExceptionMessage()
    {
        // Texty API mohou obsahovat osobní údaje, výjimka je nese jen ve vlastnosti.
        using var api = new TestApi();
        api.Api.Respond(LocationPath, HttpStatusCode.OK, """{"IsError":true,"ErrorMessage":"jan.novak@example.cz neexistuje"}""");

        ReservantoApiException error = await Assert.ThrowsAsync<ReservantoApiException>(() => GetLocationsAsync(api));

        Assert.DoesNotContain("jan.novak", error.Message, StringComparison.Ordinal);
        Assert.Contains("jan.novak@example.cz neexistuje", error.ErrorMessages);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task NonOkStatus_ThrowsWithStatus_AndIsNotRetried(HttpStatusCode status)
    {
        using var api = new TestApi();
        api.Api.Respond(LocationPath, status, string.Empty);

        ReservantoApiException error = await Assert.ThrowsAsync<ReservantoApiException>(() => GetLocationsAsync(api));

        Assert.Equal(status, error.StatusCode);
        Assert.False(error.IsErrorResponse);
        Assert.IsNotType<ReservantoApiUnavailableException>(error);
        Assert.Equal(1, api.Api.CountFor(LocationPath));
    }

    [Fact]
    public async Task NonOkStatus_WithErrorBody_KeepsApiTexts()
    {
        using var api = new TestApi();
        api.Api.Respond(LocationPath, HttpStatusCode.BadRequest, """{"IsError":true,"ErrorMessages":["Špatný požadavek"]}""");

        ReservantoApiException error = await Assert.ThrowsAsync<ReservantoApiException>(() => GetLocationsAsync(api));

        Assert.Equal(["Špatný požadavek"], error.ErrorMessages);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("""{"IsError":false,"Items":[{"Id":"abc"}]}""")]
    [InlineData("""{"IsError":false,"Items":[{"Id":1,"State":"Public","CreatedAt":999999999999999,"ModifiedAt":1}]}""")]
    public async Task UnreadableResponse_Throws(string body)
    {
        using var api = new TestApi(strict: false);
        api.Api.Respond(LocationPath, HttpStatusCode.OK, body);

        ReservantoApiException error = await Assert.ThrowsAsync<ReservantoApiException>(() => GetLocationsAsync(api));

        Assert.Contains("nejde přečíst", error.Message, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task SuccessWithoutItems_InProduction_ReturnsEmptyList()
    {
        using var api = new TestApi(strict: false);
        api.Api.Respond(LocationPath, HttpStatusCode.OK, """{"IsError":false}""");

        Assert.Empty(await GetLocationsAsync(api));
    }

    [Theory]
    [InlineData("/v1/Segment/GetList")]
    [InlineData("/v1/Location/GetList")]
    [InlineData("/v1/BookingService/GetList")]
    [InlineData("/v1/BookingResource/GetList")]
    [InlineData("/v1/OneToOne/GetAvailableStartsForLocation")]
    [InlineData("/v1/Classes/GetAvailableAppointments")]
    public async Task StrictMode_MissingCollection_Throws(string path)
    {
        // „Žádné volné termíny“ nesmí schovávat jiný stav.
        using var api = new TestApi(strict: true);
        api.Api.Respond(path, HttpStatusCode.OK, """{"IsError":false}""");
        (object group, System.Reflection.MethodInfo method) = ClientMethods.For(api.Client, path);

        ReservantoApiException error = await Assert.ThrowsAsync<ReservantoApiException>(
            () => (Task)method.Invoke(group, [Activator.CreateInstance(method.GetParameters()[0].ParameterType), CancellationToken.None])!);

        Assert.Contains("kolekci", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ErrorTexts_AreLoggedOnlyAtDebug_ParametersAtWarning()
    {
        using var api = new TestApi();
        api.Api.Respond(LocationPath, HttpStatusCode.OK, """{"IsError":true,"ErrorMessage":"jan.novak@example.cz už existuje","ErrorParameters":["Email"]}""");

        await Assert.ThrowsAsync<ReservantoApiException>(() => GetLocationsAsync(api));

        FakeLogRecord[] records = [.. api.Logs.GetSnapshot()];
        Assert.All(
            records.Where(r => r.Level >= LogLevel.Information),
            r => Assert.DoesNotContain("jan.novak", $"{r.Message} {r.Exception}", StringComparison.Ordinal));
        Assert.Contains(records, r => r.Level == LogLevel.Warning && r.Message.Contains("Email", StringComparison.Ordinal));
        Assert.Contains(records, r => r.Level == LogLevel.Debug && r.Message.Contains("jan.novak", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StrictMode_UnknownFieldInResponse_Throws()
    {
        using var api = new TestApi(strict: true);
        api.Api.Respond(LocationPath, HttpStatusCode.OK, """{"IsError":false,"Items":[{"Id":1,"State":"Public","CreatedAt":1,"ModifiedAt":1,"NewField":42}]}""");

        ReservantoApiException error = await Assert.ThrowsAsync<ReservantoApiException>(() => GetLocationsAsync(api));

        Assert.Contains("NewField", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProductionMode_UnknownFieldInResponse_IsIgnored()
    {
        using var api = new TestApi(strict: false);
        api.Api.Respond(LocationPath, HttpStatusCode.OK, """{"IsError":false,"Items":[{"Id":1,"State":"Public","CreatedAt":1,"ModifiedAt":1,"NewField":42}]}""");

        Assert.Equal(1, Assert.Single(await GetLocationsAsync(api)).Id);
    }

    [Fact]
    public async Task UnknownEnumValue_Throws_EvenInProductionMode()
    {
        // Tichá výchozí hodnota by soukromý objekt mohla ukázat jako veřejný.
        using var api = new TestApi(strict: false);
        api.Api.Respond(LocationPath, HttpStatusCode.OK, """{"IsError":false,"Items":[{"Id":1,"State":"Teleported","CreatedAt":1,"ModifiedAt":1}]}""");

        await Assert.ThrowsAsync<ReservantoApiException>(() => GetLocationsAsync(api));
    }

    private static Task<IReadOnlyList<Models.LocationApiModel>> GetLocationsAsync(TestApi api) =>
        api.Client.Location.GetListAsync(new ObjectStateListRequest(), CancellationToken.None);
}
