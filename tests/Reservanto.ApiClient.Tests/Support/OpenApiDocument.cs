using System.Reflection;
using Newtonsoft.Json;
using YamlDotNet.Serialization;

namespace Reservanto.ApiClient.Tests.Support;

/// <summary>
/// Snímek OpenAPI a porovnání typů klienta s jeho schématy. Hlídá pole, jejich typy a hodnoty výčtů: klient se
/// nesmí opírat o nic, co v API není. Pole, která v klientovi chybějí, chybou nejsou, klient nečte všechno.
/// </summary>
internal sealed class OpenApiDocument
{
    private const string SchemaPrefix = "#/components/schemas/";

    private readonly Dictionary<object, object> _paths;
    private readonly Dictionary<object, object> _schemas;

    private OpenApiDocument(Dictionary<object, object> root)
    {
        _paths = Map(root["paths"]);
        _schemas = Map(Map(root["components"])["schemas"]);
    }

    public static OpenApiDocument Load(string file)
    {
        IDeserializer deserializer = new DeserializerBuilder().Build();
        return new OpenApiDocument(deserializer.Deserialize<Dictionary<object, object>>(File.ReadAllText(file)));
    }

    public bool HasPost(string path) => _paths.TryGetValue(path, out object? item) && Map(item).ContainsKey("post");

    public IReadOnlyList<string> Rights(string path) =>
        Post(path).TryGetValue("x-api-rights", out object? rights) ? [.. List(rights).Cast<string>()] : [];

    public string RequestSchema(string path) =>
        SchemaName(Map(Map(Map(Map(Post(path)["requestBody"])["content"])["application/json"])["schema"]));

    public string ResponseSchema(string path) =>
        SchemaName(Map(Map(Map(Map(Map(Post(path)["responses"])["200"])["content"])["application/json"])["schema"]));

    public bool ResponseHasField(string path, string field) =>
        Map(Map(_schemas[ResponseSchema(path)])["properties"]).ContainsKey(field);

    /// <summary>Schémata, která dané pole mají.</summary>
    public bool SchemaHasField(string schemaName, string field) =>
        Map(Map(_schemas[schemaName])["properties"]).ContainsKey(field);

    /// <summary>Porovná typ se schématem. Vrací popis rozdílů, prázdný seznam = v pořádku.</summary>
    public IReadOnlyList<string> Compare(Type type, string schemaName)
    {
        List<string> differences = [];
        CompareObject(type, schemaName, schemaName, differences, []);
        return differences;
    }

    private Dictionary<object, object> Post(string path) => Map(Map(_paths[path])["post"]);

    private void CompareObject(Type type, string schemaName, string location, List<string> differences, HashSet<(Type, string)> visited)
    {
        if (!visited.Add((type, schemaName)))
        {
            return;
        }

        if (!_schemas.TryGetValue(schemaName, out object? schema))
        {
            differences.Add($"{location}: schéma {schemaName} v OpenAPI není.");
            return;
        }

        Dictionary<object, object> properties = Map(Map(schema)["properties"]);
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            string jsonName = property.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName ?? property.Name;
            string path = $"{location}.{jsonName}";
            if (properties.TryGetValue(jsonName, out object? propertySchema))
            {
                CompareValue(property.PropertyType, Map(propertySchema), path, differences, visited);
            }
            else
            {
                differences.Add($"{path}: pole v OpenAPI není.");
            }
        }
    }

    private void CompareValue(Type type, Dictionary<object, object> schema, string location, List<string> differences, HashSet<(Type, string)> visited)
    {
        Type target = Nullable.GetUnderlyingType(type) ?? type;
        if (IsModel(target) && schema.ContainsKey("$ref"))
        {
            CompareObject(target, SchemaName(schema), location, differences, visited);
            return;
        }

        Dictionary<object, object> resolved = schema.ContainsKey("$ref") ? Map(_schemas[SchemaName(schema)]) : schema;

        // null u hodnotového typu shodí čtení celé odpovědi, ne jen jedné položky.
        if (target.IsValueType && target == type && (IsNullable(schema) || IsNullable(resolved)))
        {
            differences.Add($"{location}: v OpenAPI může být null, typ {type.Name} ho nepřijme.");
        }

        if (target.IsEnum)
        {
            CompareEnum(target, resolved, location, differences);
        }
        else if (GenericArgument(target, typeof(IReadOnlyDictionary<,>), 1) is { } dictionaryValue)
        {
            CompareNested(dictionaryValue, resolved, "additionalProperties", location, differences, visited);
        }
        else if (GenericArgument(target, typeof(IReadOnlyList<>), 0) is { } listItem)
        {
            CompareNested(listItem, resolved, "items", location, differences, visited);
        }
        else if (ExpectedType(target) is not var (expectedType, expectedFormat))
        {
            differences.Add($"{location}: typ {target.Name} porovnání neumí.");
        }
        else if (!Equals(Text(resolved, "type"), expectedType) || (expectedFormat is not null && !Equals(Text(resolved, "format"), expectedFormat)))
        {
            differences.Add($"{location}: {target.Name} neodpovídá typu {Text(resolved, "type")}/{Text(resolved, "format")} v OpenAPI.");
        }
    }

    private void CompareNested(Type type, Dictionary<object, object> schema, string key, string location, List<string> differences, HashSet<(Type, string)> visited)
    {
        if (schema.TryGetValue(key, out object? nested))
        {
            CompareValue(type, Map(nested), location, differences, visited);
        }
        else
        {
            differences.Add($"{location}: v OpenAPI není kolekce.");
        }
    }

    // Oběma směry: hodnota z API, kterou výčet nezná, by při čtení shodila stránku.
    private static void CompareEnum(Type type, Dictionary<object, object> schema, string location, List<string> differences)
    {
        string[] expected = schema.TryGetValue("enum", out object? values) ? [.. List(values).Cast<string>()] : [];
        string[] actual = Enum.GetNames(type);
        differences.AddRange(expected.Except(actual).Select(value => $"{location}: hodnota {value} z OpenAPI ve výčtu chybí."));
        differences.AddRange(actual.Except(expected).Select(value => $"{location}: hodnota {value} v OpenAPI není."));
    }

    private static bool IsModel(Type type) =>
        type.IsClass && type != typeof(string) && !type.IsGenericType;

    private static Type? GenericArgument(Type type, Type definition, int index) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == definition ? type.GetGenericArguments()[index] : null;

    private static (string Type, string? Format)? ExpectedType(Type type) =>
        Type.GetTypeCode(type) switch
        {
            TypeCode.String => ("string", null),
            TypeCode.Boolean => ("boolean", null),
            TypeCode.Int32 => ("integer", "int32"),
            TypeCode.Int64 => ("integer", "int64"),
            TypeCode.Decimal => ("number", null),
            TypeCode.Double => ("number", null),
            _ when type == typeof(DateTimeOffset) => ("integer", "int64"),
            _ => null,
        };

    private static string SchemaName(Dictionary<object, object> schema) =>
        ((string)schema["$ref"])[SchemaPrefix.Length..];

    private static bool IsNullable(Dictionary<object, object> schema) => Text(schema, "nullable") == "true";

    private static string? Text(Dictionary<object, object> schema, string key) =>
        schema.TryGetValue(key, out object? value) ? value as string : null;

    private static Dictionary<object, object> Map(object node) => (Dictionary<object, object>)node;

    private static List<object> List(object node) => (List<object>)node;
}
