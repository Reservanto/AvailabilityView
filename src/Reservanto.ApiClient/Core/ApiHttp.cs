using System.Net;
using System.Text;
using Newtonsoft.Json;

namespace Reservanto.ApiClient.Core;

/// <summary>
/// Jeden POST na Reservanto API a převod výsledku: úspěch = odpověď, cokoli jiného = výjimka.
/// Sdílí ho volání endpointů i získání STT.
/// </summary>
internal static class ApiHttp
{
    public static async Task<TResponse> PostAsync<TResponse>(
        HttpClient http,
        ApiEndpoint endpoint,
        ApiRequest request,
        TimeProvider timeProvider,
        JsonSerializerSettings responseSettings,
        CancellationToken cancellationToken)
        where TResponse : ApiResponse
    {
        request.TimeStamp = timeProvider.GetUtcNow();
        using var content = new StringContent(
            JsonConvert.SerializeObject(request, ApiJson.Settings), Encoding.UTF8, "application/json");

        HttpStatusCode status;
        string body;
        try
        {
            using HttpResponseMessage response = await http.PostAsync(RelativeUri(endpoint), content, cancellationToken);
            status = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Zrušení bez požadavku volajícího = vypršel timeout HttpClientu.
            throw new ReservantoApiUnavailableException(endpoint, "vypršel časový limit požadavku", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new ReservantoApiUnavailableException(endpoint, "chyba spojení", exception);
        }

        if (status != HttpStatusCode.OK)
        {
            throw new ReservantoApiException(endpoint, $"HTTP {(int)status}", status, TryReadError(body));
        }

        TResponse parsed;
        try
        {
            parsed = JsonConvert.DeserializeObject<TResponse>(body, responseSettings)
                ?? throw new JsonSerializationException("Prázdná odpověď.");
        }
        catch (JsonException exception)
        {
            // Text výjimky Newtonsoft obsahuje hodnotu, kterou nepřečetl, a ta může být kontakt nebo adresa. Výjimka
            // proto nese jen místo v odpovědi, původní výjimku ne.
            throw new ReservantoApiException(endpoint, $"odpověď nejde přečíst, místo: {PlaceOf(exception)}", status);
        }

        if (parsed.IsError)
        {
            throw new ReservantoApiException(endpoint, "API vrátilo chybu", status, parsed);
        }

        return parsed;
    }

    /// <summary>Cesta bez úvodního lomítka — relativně k <see cref="HttpClient.BaseAddress"/> (ten končí lomítkem).</summary>
    public static Uri RelativeUri(ApiEndpoint endpoint) => new(endpoint.Path.TrimStart('/'), UriKind.Relative);

    private static string PlaceOf(JsonException exception) =>
        (exception switch
        {
            JsonSerializationException serialization => serialization.Path,
            JsonReaderException reader => reader.Path,
            _ => null,
        }) is { Length: > 0 } path ? path : "celá odpověď";

    // Chybový status může (ale nemusí) nést tělo s error kontraktem; když ne, výjimka nese aspoň status.
    private static ErrorOnlyResponse? TryReadError(string body)
    {
        try
        {
            return JsonConvert.DeserializeObject<ErrorOnlyResponse>(body, ApiJson.Settings);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class ErrorOnlyResponse : ApiResponse
    {
    }
}
