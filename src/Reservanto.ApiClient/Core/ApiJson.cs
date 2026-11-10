using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Reservanto.ApiClient.Core;

/// <summary>
/// Nastavení serializace pro Reservanto API (Newtonsoft.Json): názvy vlastností PascalCase jako
/// v OpenAPI, výčty jako text, časy jako unix sekundy.
/// </summary>
internal static class ApiJson
{
    /// <summary>Nastavení pro provoz: neznámé pole v odpovědi se ignoruje (API může přidat pole v nové verzi).</summary>
    public static JsonSerializerSettings Settings { get; } = Create(strict: false);

    /// <summary>
    /// Přísné nastavení pro testy nad nahrávkami a smoke testy: neznámé pole = chyba. Tak se pozná pole, které
    /// klient ztrácí (drift DTO proti API).
    /// </summary>
    public static JsonSerializerSettings StrictSettings { get; } = Create(strict: true);

    /// <summary>Nastavení podle konfigurace klienta.</summary>
    public static JsonSerializerSettings For(ReservantoApiOptions options) =>
        options.StrictDeserialization ? StrictSettings : Settings;

    private static JsonSerializerSettings Create(bool strict) => new()
    {
        MissingMemberHandling = strict ? MissingMemberHandling.Error : MissingMemberHandling.Ignore,
        // Známá pole mimo OpenAPI přísné čtení přeskočí, chybou je jen nová odchylka.
        ContractResolver = strict ? UndocumentedMembers.StrictContractResolver : null,
        // null v odpovědi u nenulovatelného pole = chyba (ne tichá výchozí hodnota); request posílá null explicitně
        // jako starý klient.
        NullValueHandling = NullValueHandling.Include,
        DateParseHandling = DateParseHandling.None,
        Converters =
        {
            // Neznámá hodnota výčtu je chyba i v provozu — tichý default by byl lež.
            new StringEnumConverter { AllowIntegerValues = false },
            new UnixTimeConverter(),
        },
    };
}
