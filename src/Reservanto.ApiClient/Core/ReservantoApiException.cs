using System.Net;

namespace Reservanto.ApiClient.Core;

/// <summary>
/// Volání Reservanto API se nepovedlo: API vrátilo <c>IsError</c>, neúspěšný HTTP status nebo nečitelnou
/// odpověď. Klient chyby nikdy nepolyká — žádné tiché <c>null</c> místo chyby.
/// </summary>
/// <remarks>
/// <see cref="Exception.Message"/> nese jen endpoint a status: texty z API (<see cref="ErrorMessages"/>) mohou
/// obsahovat údaje zákazníka, proto nejsou v hlášce výjimky.
/// </remarks>
public class ReservantoApiException : Exception
{
    /// <summary>Vytvoří výjimku.</summary>
    /// <param name="endpoint">Volaný endpoint.</param>
    /// <param name="message">Popis bez údajů z těla requestu ani odpovědi.</param>
    /// <param name="statusCode">HTTP status odpovědi, pokud nějaká přišla.</param>
    /// <param name="response">Chybová odpověď API, pokud ji šlo přečíst.</param>
    /// <param name="innerException">Původní výjimka.</param>
    public ReservantoApiException(
        ApiEndpoint endpoint,
        string message,
        HttpStatusCode? statusCode = null,
        ApiResponse? response = null,
        Exception? innerException = null)
        : base($"{endpoint.Path}: {message}", innerException)
    {
        Endpoint = endpoint;
        StatusCode = statusCode;
        IsErrorResponse = statusCode == HttpStatusCode.OK && response?.IsError == true;
        // Texty chyby endpointu s údaji zákazníků se nedrží vůbec, výjimka může skončit v logu celá.
        ErrorMessages = endpoint.CarriesCustomerData ? [] : CollectTexts(response?.ErrorMessages, response?.ErrorMessage);
        ErrorParameters = CollectTexts(response?.ErrorParameters, response?.ErrorParameter);
    }

    /// <summary>Volaný endpoint.</summary>
    public ApiEndpoint Endpoint { get; }

    /// <summary>HTTP status odpovědi; <c>null</c>, pokud odpověď nepřišla.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// API odpovědělo HTTP 200 s <c>IsError = true</c> — požadavek zpracovalo a odmítlo (validace, obsazený slot, …).
    /// Ostatní chyby jsou technické.
    /// </summary>
    public bool IsErrorResponse { get; }

    /// <summary>Chybové hlášky API (do logu, uživateli se nezobrazují syrově).</summary>
    public IReadOnlyList<string> ErrorMessages { get; }

    /// <summary>Chybně zadané parametry requestu podle API.</summary>
    public IReadOnlyList<string> ErrorParameters { get; }

    // API plní jednotné i množné pole nekonzistentně — sloučí se bez duplicit.
    private static string[] CollectTexts(IReadOnlyList<string>? many, string? single) =>
        (many ?? []).Append(single ?? string.Empty)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}

/// <summary>
/// API odmítlo LongTimeToken: je neplatný nebo ho merchant zrušil. Opakování nepomůže, správce musí účet připojit
/// znovu (<c>docs/SPEC.md</c> sekce 6 krok 4). Záměrně nenese původní výjimku, ta může obsahovat tělo odpovědi.
/// </summary>
public sealed class ReservantoTokenRejectedException : ReservantoApiException
{
    /// <summary>Vytvoří výjimku.</summary>
    /// <param name="endpoint">Volaný endpoint.</param>
    /// <param name="statusCode">HTTP status odmítnutí.</param>
    public ReservantoTokenRejectedException(ApiEndpoint endpoint, HttpStatusCode? statusCode)
        : base(endpoint, "API odmítlo LongTimeToken", statusCode)
    {
    }
}

/// <summary>
/// Reservanto API není dostupné: timeout, chyba spojení, stav „API degraded“ nebo nejde získat krátkodobý token.
/// Technická chyba, Infrastructure ji převede na typ z Application.
/// </summary>
public sealed class ReservantoApiUnavailableException : ReservantoApiException
{
    /// <summary>Vytvoří výjimku.</summary>
    /// <param name="endpoint">Volaný endpoint.</param>
    /// <param name="message">Popis příčiny.</param>
    /// <param name="innerException">Původní výjimka.</param>
    public ReservantoApiUnavailableException(ApiEndpoint endpoint, string message, Exception? innerException = null)
        : base(endpoint, message, innerException: innerException)
    {
    }
}
