using System.Text.Json;
using System.Text.RegularExpressions;
using AvailabilityView.Tests.Shared;

namespace AvailabilityView.Repository.Tests;

/// <summary>
/// Nahrávky API v <c>tests/api-recordings</c>: bez tokenů, hlaviček, e-mailů a telefonů. Vrací popis porušení bez
/// samotné hodnoty, aby se neobjevila ve výstupu.
/// </summary>
internal static partial class RecordingRules
{
    private const string Root = "tests/api-recordings/";
    private const string Redacted = "REDACTED";

    private static readonly HashSet<string> _hiddenFields = new(StringComparer.Ordinal)
    {
        "LongTimeToken",
        "ShortTimeToken",
        "Email",
        "Phone",
    };

    private const string EventEndpoint = "Event/GetList";

    private const string RedactedText = "REDACTED";

    private static readonly HashSet<string> _eventFields = new(StringComparer.Ordinal)
    {
        "AppointmentId",
        "SourceId",
        "StartDate",
        "EndDate",
        "Status",
    };

    private static readonly HashSet<string> _eventResponseFields = new(StringComparer.Ordinal)
    {
        "Events",
        "IsError",
        "ErrorMessage",
        "ErrorMessages",
        "ErrorParameter",
        "ErrorParameters",
    };

    public static bool AppliesTo(string path) =>
        path.StartsWith(Root, StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.Ordinal);

    public static IReadOnlyList<string> Check(string path, string text)
    {
        List<string> violations = [];
        using var document = JsonDocument.Parse(text);
        JsonElement root = document.RootElement;

        string expected = path[Root.Length..^".json".Length];
        string actual = $"{Text(root, "endpoint")}/{Text(root, "scenario")}";
        if (expected != actual)
        {
            violations.Add($"{path}: endpoint a scénář neodpovídají cestě souboru.");
        }

        Walk(root, path, violations);
        CheckEvents(root, path, violations);
        CheckNeutralNames(root, Text(root, "endpoint") ?? string.Empty, path, violations);
        if (EmailPattern().IsMatch(text))
        {
            violations.Add($"{path}: nahrávka obsahuje e-mailovou adresu.");
        }

        return violations;
    }

    // Rezervace nesou údaje zákazníků. V nahrávce smí zůstat jen pole, která aplikace čte.
    private static void CheckEvents(JsonElement root, string path, List<string> violations)
    {
        if (Text(root, "endpoint") != EventEndpoint)
        {
            return;
        }

        if (!root.TryGetProperty("response", out JsonElement response)
            || !response.TryGetProperty("body", out JsonElement body)
            || body.ValueKind != JsonValueKind.Object)
        {
            violations.Add($"{path}: nahrávka rezervací nemá tělo odpovědi.");
            return;
        }

        foreach (JsonProperty property in body.EnumerateObject().Where(property => !_eventResponseFields.Contains(property.Name)))
        {
            violations.Add($"{path}: odpověď rezervací má pole {property.Name} navíc.");
        }

        // Texty chyb mohou nést údaje zákazníka, nahrávač je nahradí.
        foreach (JsonElement message in ErrorTexts(body).Where(message => message.ValueKind != JsonValueKind.Null
            && !(message.ValueKind == JsonValueKind.String && message.GetString() == RedactedText)))
        {
            violations.Add($"{path}: odpověď rezervací má text chyby.");
        }

        if (!body.TryGetProperty("Events", out JsonElement events) || events.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement item in events.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                violations.Add($"{path}: rezervace není objekt.");
                continue;
            }

            foreach (JsonProperty property in item.EnumerateObject().Where(property => !_eventFields.Contains(property.Name)))
            {
                violations.Add($"{path}: rezervace má pole {property.Name}, které aplikace nečte.");
            }
        }
    }

    // Názvy a adresy z účtu merchanta: jen prázdné nebo neutrální podle NeutralNames, nic jiného.
    private static void CheckNeutralNames(JsonElement root, string endpoint, string path, List<string> violations)
    {
        if (root.TryGetProperty("request", out JsonElement request))
        {
            CheckRules(NeutralNames.ForRequest(endpoint), request, path, violations);
        }

        if (root.TryGetProperty("response", out JsonElement response) && response.TryGetProperty("body", out JsonElement body))
        {
            CheckRules(NeutralNames.For(endpoint), body, path, violations);
        }
    }

    private static void CheckRules(IEnumerable<NeutralNames.Rule> rules, JsonElement body, string path, List<string> violations)
    {
        foreach (NeutralNames.Rule rule in rules)
        {
            foreach (JsonElement item in Objects(body, rule.Path))
            {
                if (!item.TryGetProperty(rule.Field, out JsonElement value) || value.ValueKind == JsonValueKind.Null
                    || (value.ValueKind == JsonValueKind.String && value.GetString()!.Length == 0))
                {
                    continue;
                }

                if (rule.IsNumber)
                {
                    if (value.ValueKind != JsonValueKind.Number || value.GetRawText() != rule.Text)
                    {
                        violations.Add($"{path}: pole {rule.Field} nemá vymyšlenou hodnotu.");
                    }

                    continue;
                }

                string? id = rule.IdField is not null && item.TryGetProperty(rule.IdField, out JsonElement idValue) && idValue.ValueKind != JsonValueKind.Null
                    ? idValue.ToString()
                    : null;
                if (value.ValueKind != JsonValueKind.String || value.GetString() != rule.Value(id))
                {
                    violations.Add($"{path}: pole {rule.Field} nemá neutrální hodnotu.");
                }
            }
        }
    }

    private static IEnumerable<JsonElement> Objects(JsonElement body, IEnumerable<string> names)
    {
        IEnumerable<JsonElement> current = [body];
        foreach (string name in names)
        {
            current = [.. current
                .Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out _))
                .Select(item => item.GetProperty(name))
                .SelectMany(child => child.ValueKind == JsonValueKind.Array ? [.. child.EnumerateArray()] : new[] { child })];
        }

        return current.Where(item => item.ValueKind == JsonValueKind.Object);
    }

    private static IEnumerable<JsonElement> ErrorTexts(JsonElement body)
    {
        // Seznam i jediný text, v kterémkoli z obou polí: API je plní nekonzistentně.
        foreach (JsonProperty property in body.EnumerateObject().Where(property => property.Name is "ErrorMessage" or "ErrorMessages"))
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                yield return property.Value;
                continue;
            }

            foreach (JsonElement item in property.Value.EnumerateArray())
            {
                yield return item;
            }
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) ? value.GetString() : null;

    private static void Walk(JsonElement element, string path, List<string> violations)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                Walk(item, path, violations);
            }
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Name == "Authorization")
            {
                violations.Add($"{path}: hlavička {property.Name} do nahrávky nepatří.");
            }

            bool isHidden = property.Value.ValueKind == JsonValueKind.Null
                || (property.Value.ValueKind == JsonValueKind.String && property.Value.ValueEquals(Redacted));
            if (_hiddenFields.Contains(property.Name) && !isHidden)
            {
                violations.Add($"{path}: pole {property.Name} musí být {Redacted}.");
            }

            Walk(property.Value, path, violations);
        }
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailPattern();
}
