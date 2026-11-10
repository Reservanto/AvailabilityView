using System.Collections;
using System.Reflection;
using AvailabilityView.Tests.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reservanto.ApiClient.Authorization;
using Reservanto.ApiClient.Core;
using Reservanto.ApiClient.Endpoints;
using Reservanto.ApiClient.Models;
using Reservanto.ApiClient.Tests.Support;

namespace Reservanto.ApiClient.Tests;

/// <summary>
/// Klient proti snímku produkčního OpenAPI v <c>tests/Reservanto.ApiClient.Tests/OpenApi/</c>. Snímek se obnovuje
/// ručně ze stejné adresy, kterou uvádí <c>CLAUDE.md</c>.
/// </summary>
public sealed class OpenApiContractTests
{
    private static readonly OpenApiDocument _document = OpenApiDocument.Load(
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "tests", "Reservanto.ApiClient.Tests", "OpenApi"), "*.yaml").Single());

    // Požadavek a odpověď každého endpointu. Úplnost hlídá Contract_CoversEveryEndpoint.
    private static readonly Dictionary<string, (Type Request, Type Response)> _contract = new()
    {
        [ApiEndpoints.AuthorizeGetShortTimeToken.Path] = (typeof(ShortTimeTokenRequest), typeof(ShortTimeTokenResponse)),
        [ApiEndpoints.SegmentGetList.Path] = (typeof(ObjectStateListRequest), typeof(ListResponse<SegmentApiModel>)),
        [ApiEndpoints.LocationGetList.Path] = (typeof(ObjectStateListRequest), typeof(ListResponse<LocationApiModel>)),
        [ApiEndpoints.BookingServiceGetList.Path] = (typeof(ObjectStateModifiedAfterListRequest), typeof(LoadAllBookingServiceResponse)),
        [ApiEndpoints.BookingResourceGetList.Path] = (typeof(BookingResourceGetListRequest), typeof(ListResponse<BookingResourceApiModel>)),
        [ApiEndpoints.OneToOneGetAvailableStartsForLocation.Path] = (typeof(AvailableStartsForLocationRequest), typeof(AvailableStartsForLocationResponse)),
        [ApiEndpoints.ClassesGetAvailableAppointments.Path] = (typeof(AvailableClassesRequest), typeof(ListResponse<ExtendedAppointmentClassApiModel>)),
        [ApiEndpoints.MerchantGetInfo.Path] = (typeof(EmptyRequest), typeof(ItemResponse<MerchantInfoApiModel>)),
        [ApiEndpoints.EventGetList.Path] = (typeof(EventListRequest), typeof(EventListResponse)),
        [ApiEndpoints.WorkingHoursGetForPeriod.Path] = (typeof(WorkingHoursForPeriodRequest), typeof(WorkingHoursResponse)),
    };

    // Pole s daty podle OpenAPI a ukázková hodnota. Hlídá, že metoda klienta opravdu čte to, co API posílá.
    private static readonly Dictionary<string, (string Field, string Sample)> _responses = new()
    {
        [ApiEndpoints.SegmentGetList.Path] = ("Items", """[{"Id":1,"State":"Public"}]"""),
        [ApiEndpoints.LocationGetList.Path] = ("Items", """[{"Id":1,"State":"Public","CreatedAt":1,"ModifiedAt":1}]"""),
        [ApiEndpoints.BookingServiceGetList.Path] = ("BookingServices", """[{"Id":1,"State":"Public"}]"""),
        [ApiEndpoints.BookingResourceGetList.Path] = ("Items", """[{"Id":1,"State":"Public"}]"""),
        [ApiEndpoints.OneToOneGetAvailableStartsForLocation.Path] = ("Starts", """{"1":[1747924200]}"""),
        [ApiEndpoints.ClassesGetAvailableAppointments.Path] = ("Items", """[{"Id":1,"StartDate":1,"EndDate":2}]"""),
        [ApiEndpoints.MerchantGetInfo.Path] = ("Result", """{"Id":1,"Name":"Merchant"}"""),
        [ApiEndpoints.EventGetList.Path] = ("Events", """[{"AppointmentId":1,"SourceId":1,"StartDate":1,"EndDate":2,"Status":"Confirmed"}]"""),
        [ApiEndpoints.WorkingHoursGetForPeriod.Path] = ("Items", """[{"DayOfWeek":3,"Start":1,"End":2}]"""),
    };

    public static TheoryData<string> Paths => [.. ApiEndpoints.All.Select(endpoint => endpoint.Path)];

    public static TheoryData<string> ClientPaths => [.. _responses.Keys];

    [Fact]
    public void Contract_CoversEveryEndpoint()
    {
        Assert.Equal(ApiEndpoints.All.Select(endpoint => endpoint.Path).Order(), _contract.Keys.Order());
        Assert.Equal(
            ApiEndpoints.All.Select(endpoint => endpoint.Path).Where(path => path != ApiEndpoints.AuthorizeGetShortTimeToken.Path).Order(),
            _responses.Keys.Order());
    }

    [Theory]
    [MemberData(nameof(ClientPaths))]
    public async Task ClientMethod_UsesContractRequest_AndReadsFieldFromOpenApi(string path)
    {
        (string field, string sample) = _responses[path];
        Assert.True(_document.ResponseHasField(path, field), $"{path}: pole {field} v OpenAPI není.");
        using var api = new TestApi();
        api.Api.Respond(path, System.Net.HttpStatusCode.OK, $$"""{"IsError":false,"{{field}}":{{sample}}}""");
        (object group, MethodInfo method) = ClientMethods.For(api.Client, path);
        Type request = method.GetParameters()[0].ParameterType;

        var call = (Task)method.Invoke(group, [Activator.CreateInstance(request), TestContext.Current.CancellationToken])!;
        await call;

        Assert.Equal(_contract[path].Request, request);
        object result = call.GetType().GetProperty("Result")!.GetValue(call)!;
        if (result is IEnumerable items)
        {
            Assert.Single(items.Cast<object>());
        }
        else
        {
            // Jediný výsledek: každé pole z ukázky musí klient opravdu přečíst.
            var read = JObject.FromObject(result);
            Assert.All(JObject.Parse(sample).Properties(), property => Assert.True(JToken.DeepEquals(property.Value, read[property.Name]), property.Name));
        }
    }

    [Fact]
    public async Task SingleResult_MissingInSuccessfulResponse_IsError()
    {
        using var api = new TestApi();
        api.Api.Respond(ApiEndpoints.MerchantGetInfo.Path, System.Net.HttpStatusCode.OK, """{"IsError":false,"Result":null}""");

        await Assert.ThrowsAsync<ReservantoApiException>(() => api.Client.Merchant.GetInfoAsync(new EmptyRequest(), TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void Endpoint_ExistsInOpenApi_WithSameRights(string path)
    {
        Assert.True(_document.HasPost(path), $"{path} v OpenAPI není.");
        Assert.Equal(_document.Rights(path).Order(), ApiEndpoints.All.Single(endpoint => endpoint.Path == path).Rights.Order());
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void Request_MatchesOpenApi(string path)
    {
        Assert.Empty(_document.Compare(_contract[path].Request, _document.RequestSchema(path)));
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void Response_MatchesOpenApi(string path)
    {
        Assert.Empty(_document.Compare(_contract[path].Response, _document.ResponseSchema(path)));
    }

    [Fact]
    public void UndocumentedMembers_AreMissingInOpenApiAndModels()
    {
        // Až pole přinese nová verze OpenAPI, tento test na to upozorní a pole se přesune do modelu nebo ze seznamu.
        Assert.NotEmpty(UndocumentedMembers.ByType);
        foreach ((Type type, IReadOnlyList<string> names) in UndocumentedMembers.ByType)
        {
            Assert.All(names, name =>
            {
                Assert.False(_document.SchemaHasField(type.Name, name), $"{type.Name}.{name} už v OpenAPI je.");
                Assert.Null(type.GetProperty(name));
            });
        }
    }

    [Fact]
    public void StrictReading_SkipsKnownFields_ButNotNewOnes()
    {
        const string Known = """{"Id":1,"BookingResource":1,"State":"Public"}""";
        const string Unknown = """{"Id":1,"Vymyslene":1,"State":"Public"}""";

        Assert.NotNull(JsonConvert.DeserializeObject<BookingResourceApiModel>(Known, ApiJson.StrictSettings));
        Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<BookingResourceApiModel>(Unknown, ApiJson.StrictSettings));
    }

    [Fact]
    public void Compare_CatchesFieldMissingInOpenApi()
    {
        Assert.Contains(_document.Compare(typeof(ExtraField), nameof(SegmentApiModel)), d => d.Contains(nameof(ExtraField.Invented), StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_CatchesWrongFieldType()
    {
        Assert.Contains(_document.Compare(typeof(WrongType), nameof(SegmentApiModel)), d => d.Contains(nameof(WrongType.Id), StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_CatchesEnumDifferenceInBothDirections()
    {
        IReadOnlyList<string> differences = _document.Compare(typeof(WrongEnum), nameof(SegmentApiModel));

        Assert.Contains(differences, d => d.Contains(nameof(DriftedState.Teleported), StringComparison.Ordinal));
        Assert.Contains(differences, d => d.Contains(nameof(ObjectState.Deleted), StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_CatchesDriftInsideListItems()
    {
        Assert.Contains(_document.Compare(typeof(ListResponse<ExtraField>), "ListResponseOfSegmentApiModel"), d => d.Contains(nameof(ExtraField.Invented), StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_UsesJsonName_NotPropertyName()
    {
        Assert.Contains(_document.Compare(typeof(RenamedField), nameof(SegmentApiModel)), d => d.Contains("JinyNazev", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_CatchesValueTypeThatCannotHoldNull()
    {
        Assert.Contains(_document.Compare(typeof(NotNullable), nameof(ExtendedAppointmentClassApiModel)), d => d.Contains(nameof(NotNullable.CourseId), StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_CatchesMissingSchema()
    {
        Assert.NotEmpty(_document.Compare(typeof(ExtraField), "SchemaKteryNeni"));
    }

    [Fact]
    public void Document_DoesNotKnowInventedEndpoint()
    {
        Assert.False(_document.HasPost("/v1/Vymyslene/GetList"));
    }

    private sealed class ExtraField
    {
        public int Id { get; init; }

        public string? Invented { get; init; }
    }

    private sealed class RenamedField
    {
        [JsonProperty("JinyNazev")]
        public int Id { get; init; }
    }

    private sealed class NotNullable
    {
        public int CourseId { get; init; }
    }

    private sealed class WrongType
    {
        public string? Id { get; init; }
    }

    private enum DriftedState
    {
        Private,
        Public,
        Teleported,
    }

    private sealed class WrongEnum
    {
        public DriftedState State { get; init; }
    }
}
