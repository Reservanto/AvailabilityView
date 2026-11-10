namespace Reservanto.ApiClient.Core;

/// <summary>
/// Základ všech requestů. API vyžaduje v každém requestu <c>TimeStamp</c> — doplňuje ho
/// klient těsně před odesláním, volající kód ho nenastavuje.
/// </summary>
public abstract class ApiRequest
{
    /// <summary>Čas odeslání requestu (na drátě unix sekundy).</summary>
    public DateTimeOffset TimeStamp { get; internal set; }
}

/// <summary>Request stránkovaného seznamu. Stránky prochází klient sám, volající dostane všechny položky.</summary>
public abstract class PagedApiRequest : ApiRequest
{
    /// <summary>Největší velikost stránky, kterou API přijme.</summary>
    internal const int MaxPageSize = 100;

    /// <summary>Číslo stránky od 1.</summary>
    public int Page { get; internal set; } = 1;

    /// <summary>Počet záznamů na stránku.</summary>
    public int PageSize { get; internal set; } = MaxPageSize;
}
