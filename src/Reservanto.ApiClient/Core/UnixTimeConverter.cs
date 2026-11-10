using Newtonsoft.Json;

namespace Reservanto.ApiClient.Core;

/// <summary>
/// Převod <see cref="DateTimeOffset"/> ↔ unix sekundy (typ <c>UnixTimeStamp</c> v OpenAPI: „počet vteřin od
/// 1.1.1970 0:00:00 UTC“). Jediné místo převodu času v klientu; časy se čtou v UTC, zónu merchanta řeší konzument.
/// </summary>
internal sealed class UnixTimeConverter : JsonConverter
{
    private static readonly long _minUnixSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds();
    private static readonly long _maxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    public override bool CanConvert(Type objectType) =>
        objectType == typeof(DateTimeOffset) || objectType == typeof(DateTimeOffset?);

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return objectType == typeof(DateTimeOffset?)
                ? null
                : throw new JsonSerializationException($"Čas nesmí být null (cesta {reader.Path}).");
        }

        if (reader.TokenType != JsonToken.Integer)
        {
            throw new JsonSerializationException($"Čas má být unix timestamp (celé číslo), přišlo {reader.TokenType} (cesta {reader.Path}).");
        }

        // Mimo rozsah DateTimeOffset (nebo mimo long) = nečitelná odpověď, ne jiný typ výjimky mimo kontrakt klienta.
        if (reader.Value is not long seconds
            || seconds < _minUnixSeconds
            || seconds > _maxUnixSeconds)
        {
            throw new JsonSerializationException($"Čas {reader.Value} je mimo rozsah (cesta {reader.Path}).");
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is DateTimeOffset time)
        {
            writer.WriteValue(time.ToUnixTimeSeconds());
        }
        else
        {
            writer.WriteNull();
        }
    }
}
