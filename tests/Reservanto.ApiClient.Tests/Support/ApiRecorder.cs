using System.Text;
using System.Text.RegularExpressions;
using AvailabilityView.Tests.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Reservanto.ApiClient.Tests.Support;

/// <summary>
/// Ukládá odpovědi API do <c>tests/api-recordings</c>, když je nastavená proměnná <c>RESERVANTO_RECORD</c>. Formát
/// popisuje README v téže složce. Tokeny, kontakty a GUID se do souboru nedostanou, hlavičky se neukládají vůbec.
/// Názvy a adresy z účtu merchanta se nahradí neutrálním textem.
/// </summary>
internal sealed partial class ApiRecorder
{
    private const string ApiVersion = "1.26.9.14";
    private const string Redacted = "REDACTED";

    // Kontakty v účtu vypadají jako skutečné a aplikace je nepotřebuje, viz docs/decisions.md.
    private static readonly string[] _secretFields =
    [
        "LongTimeToken",
        "ShortTimeToken",
        "Email",
        "Phone",
    ];

    private const string EventEndpoint = "Event/GetList";

    // Rezervace nesou údaje zákazníků. Nahrávka má jen pole, která aplikace čte, ostatní se
    // zahodí dřív, než se soubor zapíše. Kontrolu nahrávek dělá RecordingRules.
    private static readonly string[] _eventFields = ["AppointmentId", "SourceId", "StartDate", "EndDate", "Status"];
    private static readonly string[] _eventResponseFields = ["Events", "IsError", "ErrorMessage", "ErrorMessages", "ErrorParameter", "ErrorParameters"];

    private readonly string _root = Path.Combine(RepositoryRoot.Path, "tests", "api-recordings");
    private readonly string _recordedAt = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    public static bool IsEnabled => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RESERVANTO_RECORD"));

    /// <summary>Scénář, pod kterým se ukládají další odpovědi.</summary>
    public string Scenario { get; set; } = "ok";

    public DelegatingHandler CreateHandler() => new Handler(this);

    /// <summary>Text bez tajných údajů.</summary>
    private static string Clean(string json)
    {
        var token = (JContainer)JToken.Parse(json);
        foreach (JProperty property in token.Descendants().OfType<JProperty>().Where(IsSecret).ToArray())
        {
            property.Value = Redacted;
        }

        return GuidPattern().Replace(token.ToString(Formatting.Indented), Redacted);
    }

    internal static JToken TrimEvents(JToken body)
    {
        if (body is not JObject response)
        {
            return body;
        }

        foreach (JProperty property in response.Properties().Where(property => !_eventResponseFields.Contains(property.Name, StringComparer.Ordinal)).ToArray())
        {
            property.Remove();
        }

        // Texty chyb mohou nést údaje zákazníka a aplikace z nich nic nečte. Chybová odpověď nemá Events.
        // Text v nečekaném tvaru (seznam místo textu a naopak) se nahradí také.
        foreach (JProperty property in response.Properties().Where(property => property.Name is "ErrorMessage" or "ErrorMessages").ToArray())
        {
            if (property.Value is JArray messages)
            {
                foreach (JToken message in messages.ToArray())
                {
                    message.Replace(new JValue(Redacted));
                }
            }
            else if (property.Value.Type != JTokenType.Null)
            {
                property.Value = Redacted;
            }
        }

        if (response["Events"] is not JArray events)
        {
            return body;
        }

        foreach (JObject item in events.OfType<JObject>())
        {
            foreach (JProperty property in item.Properties().Where(property => !_eventFields.Contains(property.Name, StringComparer.Ordinal)).ToArray())
            {
                property.Remove();
            }
        }

        return body;
    }

    /// <summary>Názvy, popisy a adresy nahradí neutrálním textem podle <see cref="NeutralNames"/>.</summary>
    internal static JToken Neutralize(string endpoint, JToken body) => Apply(NeutralNames.For(endpoint), body);

    /// <summary>Dotaz klienta podle <see cref="NeutralNames.RequestRules"/>, třeba Id kalendáře.</summary>
    internal static JToken NeutralizeRequest(string endpoint, JToken request) => Apply(NeutralNames.ForRequest(endpoint), request);

    private static JToken Apply(IEnumerable<NeutralNames.Rule> rules, JToken body)
    {
        foreach (NeutralNames.Rule rule in rules)
        {
            foreach (JObject item in Objects(body, rule.Path))
            {
                if (rule.IsNumber)
                {
                    if (item[rule.Field] is JValue { Type: not JTokenType.Null } number)
                    {
                        number.Replace(new JValue(long.Parse(rule.Text!, System.Globalization.CultureInfo.InvariantCulture)));
                    }
                }
                else if (rule.Text is null)
                {
                    // Vymazané pole: text i číslo, třeba poloha adresy.
                    if (item[rule.Field] is JValue { Type: not JTokenType.Null } cleared)
                    {
                        cleared.Replace(JValue.CreateNull());
                    }
                }
                else if (item[rule.Field] is JValue { Type: JTokenType.String } value && !string.IsNullOrEmpty((string?)value))
                {
                    // Id s hodnotou null by Newtonsoft převedl na prázdný text a náhrada by končila mezerou.
                    string? id = rule.IdField is not null && item[rule.IdField] is JValue { Type: not JTokenType.Null } idValue ? idValue.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
                    value.Replace(new JValue(rule.Value(id)));
                }
            }
        }

        return body;
    }

    private static IEnumerable<JObject> Objects(JToken body, IEnumerable<string> path)
    {
        IEnumerable<JToken> current = [body];
        foreach (string name in path)
        {
            current = current.OfType<JObject>().Select(item => item[name]).SelectMany(child => child switch
            {
                JArray array => array.Children(),
                JObject single => [single],
                _ => Enumerable.Empty<JToken>(),
            });
        }

        return current.OfType<JObject>();
    }

    private static bool IsSecret(JProperty property) =>
        _secretFields.Contains(property.Name, StringComparer.Ordinal) && property.Value.Type != JTokenType.Null;

    private void Save(string path, string request, int status, string body)
    {
        string endpoint = path.Replace("/v1/", string.Empty, StringComparison.Ordinal);
        string file = Path.Combine(_root, endpoint.Replace('/', Path.DirectorySeparatorChar), $"{Scenario}.json");
        var recording = new JObject
        {
            ["source"] = "production",
            ["apiVersion"] = ApiVersion,
            ["recordedAt"] = _recordedAt,
            ["endpoint"] = endpoint,
            ["scenario"] = Scenario,
            ["request"] = NeutralizeRequest(endpoint, JToken.Parse(request)),
            ["response"] = new JObject { ["status"] = status, ["body"] = Neutralize(endpoint, endpoint == EventEndpoint ? TrimEvents(JToken.Parse(body)) : JToken.Parse(body)) },
        };

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, Clean(recording.ToString(Formatting.None)).ReplaceLineEndings("\n") + "\n", new UTF8Encoding(false));
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    private sealed class Handler(ApiRecorder recorder) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (JToken.Parse(body)["Page"] is { } page && (int)page > 1)
            {
                // Další stránka by přepsala první a přehrávání stránky nerozlišuje.
                throw new InvalidOperationException("Nahrávání seznamu přes 100 položek není podporované.");
            }

            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
            string answer = await response.Content.ReadAsStringAsync(cancellationToken);
            recorder.Save(request.RequestUri!.AbsolutePath, body, (int)response.StatusCode, answer);
            response.Content = new StringContent(answer, Encoding.UTF8, "application/json");
            return response;
        }
    }
}
