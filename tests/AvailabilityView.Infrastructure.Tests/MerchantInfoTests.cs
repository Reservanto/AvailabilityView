using System.Net;
using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Connection;
using AvailabilityView.Infrastructure.Tests.Support;
using Reservanto.ApiClient.Core;

namespace AvailabilityView.Infrastructure.Tests;

/// <summary>Název účtu pro stránku připojení. Odpověď API je ručně psaná, skutečná se nenahrává (docs/decisions.md).</summary>
public sealed class MerchantInfoTests
{
    private const string Info = """
        {"IsError":false,"Result":{"Id":7,"Name":"  Studio Testovací ","PersonalIdentifier":"12345678","VatId":null,
        "ContactEmail":"kontakt@example.invalid","ContactPhone":"+420000000000","InvoiceAddress":null,"IsVatPayer":false,
        "MailingAddress":null,"VatNumber":null,"Web":null}}
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Name_IsReadFromApi()
    {
        using var harness = new Harness();
        harness.Api.Respond(ApiEndpoints.MerchantGetInfo.Path, HttpStatusCode.OK, Info);

        Assert.Equal("Studio Testovací", await harness.Get<IMerchantInfo>().GetNameAsync(Ct));
    }

    [Fact]
    public async Task Name_IsCached()
    {
        using var harness = new Harness();
        harness.Api.Respond(ApiEndpoints.MerchantGetInfo.Path, HttpStatusCode.OK, Info);

        await harness.Get<IMerchantInfo>().GetNameAsync(Ct);
        await harness.Get<IMerchantInfo>().GetNameAsync(Ct);

        Assert.Equal(1, harness.Api.CountFor(ApiEndpoints.MerchantGetInfo.Path));
    }

    [Fact]
    public async Task MissingName_IsEmpty()
    {
        using var harness = new Harness();
        harness.Api.Respond(ApiEndpoints.MerchantGetInfo.Path, HttpStatusCode.OK, """{"IsError":false,"Result":{"Id":7,"Name":null,"IsVatPayer":false}}""");

        Assert.Equal(string.Empty, await harness.Get<IMerchantInfo>().GetNameAsync(Ct));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, ConnectionState.MissingRights)]
    [InlineData(HttpStatusCode.Unauthorized, ConnectionState.TokenRejected)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ConnectionState.Unavailable)]
    public async Task ApiFailure_BecomesReason(HttpStatusCode status, ConnectionState reason)
    {
        // Produkční API bez práva Merchant_r odpoví 403 s prázdným tělem (ověřeno 2026-11-07).
        using var harness = new Harness();
        harness.Api.Respond(ApiEndpoints.MerchantGetInfo.Path, status, string.Empty);

        ReservationSystemException exception = await Assert.ThrowsAsync<ReservationSystemException>(() => harness.Get<IMerchantInfo>().GetNameAsync(Ct));

        Assert.Equal(reason, exception.Reason);
    }
}
