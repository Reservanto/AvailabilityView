using Reservanto.ApiClient.Models;

namespace Reservanto.ApiClient.Core;

/// <summary>
/// Pole, která API posílá i OpenAPI zná, ale klient je záměrně nečte, protože nesou údaje zákazníků
/// (<c>docs/SPEC.md</c> sekce 10). Modely je nemají a JSON čtečka jejich hodnotu přeskočí v provozu i při přísném
/// čtení, takže se do paměti aplikace, logu ani chyb nedostanou. Testy hlídají, že pole v OpenAPI jsou a v modelu ne.
/// </summary>
internal static class DiscardedMembers
{
    /// <summary>Typ modelu a pole, která se při čtení zahodí.</summary>
    public static IReadOnlyDictionary<Type, IReadOnlyList<string>> ByType { get; } = new Dictionary<Type, IReadOnlyList<string>>
    {
        // Zákazník, platba, poznámky a vlastní pole. Názvy objektů aplikace bere z katalogu, ne z rezervace.
        [typeof(EventApiModel)] =
        [
            "CustomerId", "CustomerFullName", "CustomerNote", "CustomValues", "MerchantNote", "NoShowStatus",
            "IsPaid", "PaidPartOfPrice", "PaymentMethodFormatted", "Price",
            "CalendarName", "InternalSourceName", "LocationName", "SegmentName", "ServiceName", "SourceName",
        ],
    };
}
