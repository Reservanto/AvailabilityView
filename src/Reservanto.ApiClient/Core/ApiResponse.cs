namespace Reservanto.ApiClient.Core;

/// <summary>
/// Jednotný error kontrakt API: každá odpověď nese <c>IsError</c> a texty chyb. HTTP 200 ještě
/// neznamená úspěch — klient odpověď s <see cref="IsError"/> převede na <see cref="ReservantoApiException"/>,
/// takže volající dostane jen úspěšné odpovědi.
/// </summary>
public abstract class ApiResponse
{
    /// <summary>Indikuje, zda vznikla chyba.</summary>
    public bool IsError { get; init; }

    /// <summary>Chybová hláška doplňující chybu (pro debug, uživateli se nezobrazuje).</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Všechny chybové hlášky.</summary>
    public IReadOnlyList<string>? ErrorMessages { get; init; }

    /// <summary>Chybně zadaný parametr requestu.</summary>
    public string? ErrorParameter { get; init; }

    /// <summary>Všechny chybně zadané parametry requestu.</summary>
    public IReadOnlyList<string>? ErrorParameters { get; init; }
}

/// <summary>Odpověď s právě jedním výsledkem (<c>ItemResponseOf…</c> v OpenAPI).</summary>
/// <typeparam name="T">Model výsledku.</typeparam>
public sealed class ItemResponse<T> : ApiResponse
    where T : class
{
    /// <summary>Výsledek.</summary>
    public T? Result { get; init; }
}

/// <summary>Odpověď s kolekcí výsledků (<c>ListResponseOf…</c> v OpenAPI).</summary>
/// <typeparam name="T">Model položky.</typeparam>
public sealed class ListResponse<T> : ApiResponse
{
    /// <summary>Položky.</summary>
    public IReadOnlyList<T>? Items { get; init; }
}
