using System.Net;

namespace Reservanto.ApiClient.Authorization;

/// <summary>
/// Přidá merchantův STT do hlavičky <c>Authorization</c> a při 401 jednou obnoví token a požadavek zopakuje.
/// Druhé 401 propadne volajícímu jako chyba.
/// </summary>
/// <remarks>
/// API čeká v hlavičce holý token bez schématu (<c>Authorization: &lt;STT&gt;</c>, apiKey scheme <c>STTAuth</c>
/// v OpenAPI) — proto <see cref="System.Net.Http.Headers.HttpHeaders.TryAddWithoutValidation(string, string)"/>.
/// </remarks>
internal sealed class ShortTimeTokenHandler(ShortTimeTokenProvider tokenProvider) : DelegatingHandler
{
    private const string AuthorizationHeader = "Authorization";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string token = await tokenProvider.GetTokenAsync(cancellationToken);
        SetToken(request, token);
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        string refreshed = await tokenProvider.RefreshAfterRejectionAsync(token, cancellationToken);
        SetToken(request, refreshed);
        // Tělo requestu je StringContent (ApiHttp) — jde odeslat znovu.
        return await base.SendAsync(request, cancellationToken);
    }

    private static void SetToken(HttpRequestMessage request, string token)
    {
        request.Headers.Remove(AuthorizationHeader);
        request.Headers.TryAddWithoutValidation(AuthorizationHeader, token);
    }
}
