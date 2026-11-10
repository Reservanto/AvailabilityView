using System.Net;
using Reservanto.ApiClient.Core;
using Reservanto.ApiClient.Models;

namespace Reservanto.ApiClient.Endpoints;

/// <summary>Prázdný požadavek, nese jen časové razítko.</summary>
public sealed class EmptyRequest : ApiRequest;

/// <summary><c>/v1/Merchant/*</c></summary>
public sealed class MerchantEndpoints
{
    private readonly ApiTransport _transport;

    internal MerchantEndpoints(ApiTransport transport) => _transport = transport;

    /// <summary><c>/v1/Merchant/GetInfo</c> — základní údaje o merchantovi, se kterým se pracuje.</summary>
    public async Task<MerchantInfoApiModel> GetInfoAsync(EmptyRequest request, CancellationToken cancellationToken)
    {
        ItemResponse<MerchantInfoApiModel> response = await _transport.SendAsync<ItemResponse<MerchantInfoApiModel>>(
            ApiEndpoints.MerchantGetInfo, request, cancellationToken);
        return response.Result
            ?? throw new ReservantoApiException(ApiEndpoints.MerchantGetInfo, "odpověď neobsahuje výsledek", HttpStatusCode.OK, response);
    }
}
