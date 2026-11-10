using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Reservanto.ApiClient.Models;

namespace Reservanto.ApiClient.Core;

/// <summary>
/// Pole, která produkční API vrací, ale OpenAPI je nezná (ověřeno smoke testy 2026-11-06, verze 1.26.9.14). Modely je
/// záměrně nemají a klient je nepoužívá. Přísné čtení je přeskočí, aby hlásilo jen nové odchylky. Testy hlídají, že
/// položka opravdu chybí ve snímku OpenAPI i v modelu, takže obnova snímku na ni upozorní.
/// </summary>
internal static class UndocumentedMembers
{
    /// <summary>Typ modelu a nezdokumentovaná pole, která API u něj posílá.</summary>
    public static IReadOnlyDictionary<Type, IReadOnlyList<string>> ByType { get; } = new Dictionary<Type, IReadOnlyList<string>>
    {
        // Text státu nebo kraje a ISO kód země.
        [typeof(AddressApiModel)] = ["State", "CountryTwoLetterName"],
        // Duplikát Id.
        [typeof(BookingResourceApiModel)] = ["BookingResource"],
        // BookingServiceId je duplikát Id, VatRate je VatRateNullable s nulou místo null.
        [typeof(BookingServiceApiModel)] = ["BookingServiceId", "VatRate"],
        // Duplikát Id.
        [typeof(ItemPriceLevelApiModel)] = ["PriceLevelId"],
        // Duplikát Id.
        [typeof(LocationApiModel)] = ["LocationId"],
    };

    /// <summary>
    /// Resolver přísného čtení: nezdokumentovaná a záměrně zahozená pole (<see cref="DiscardedMembers"/>) přidá do
    /// kontraktu jako ignorovaná. V provozu se neznámá pole přeskakují i bez něj.
    /// </summary>
    public static IContractResolver StrictContractResolver { get; } = new IgnoringResolver();

    private sealed class IgnoringResolver : DefaultContractResolver
    {
        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            IList<JsonProperty> properties = base.CreateProperties(type, memberSerialization);
            string[] skipped = [.. ByType.GetValueOrDefault(type) ?? [], .. DiscardedMembers.ByType.GetValueOrDefault(type) ?? []];
            foreach (string name in skipped)
            {
                properties.Add(new JsonProperty
                {
                    PropertyName = name,
                    UnderlyingName = name,
                    DeclaringType = type,
                    PropertyType = typeof(object),
                    Ignored = true,
                });
            }

            return properties;
        }
    }
}
