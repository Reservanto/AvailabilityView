namespace Reservanto.ApiClient.Core;

/// <summary>Endpoint Reservanto API a práva, která vyžaduje (<c>x-api-rights</c> v OpenAPI).</summary>
/// <param name="Path">Cesta včetně prefixu, např. <c>/v1/Segment/GetList</c>.</param>
/// <param name="Rights">Práva uvedená u endpointu v OpenAPI.</param>
public sealed record ApiEndpoint(string Path, params string[] Rights)
{
    /// <summary>
    /// Odpověď nese údaje zákazníků (rezervace). Výjimka pak texty chyb z API nenese a nejdou ani do logu, mohou je
    /// obsahovat (<c>docs/SPEC.md</c> sekce 10).
    /// </summary>
    public bool CarriesCustomerData { get; init; }

    /// <inheritdoc />
    public override string ToString() => Path;
}
