using System.Text.RegularExpressions;

namespace AvailabilityView.Repository.Tests;

/// <summary>Pravidla konvencí, která build neohlídá. Vrací popis porušení, prázdný seznam = v pořádku.</summary>
internal static partial class CodeRules
{
    private const string EndpointCatalog = "src/Reservanto.ApiClient/Core/ApiEndpoints.cs";
    private const string AvailabilitySources = "src/AvailabilityView.Infrastructure/Availability/";

    public static bool AppliesTo(string path) =>
        path.StartsWith("src/", StringComparison.Ordinal)
        && (path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".cshtml", StringComparison.Ordinal));

    public static IReadOnlyList<string> Check(string path, string text)
    {
        List<string> violations = [];
        string[] lines = text.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            string previous = index > 0 ? lines[index - 1] : string.Empty;
            string location = $"{path}:{index + 1}";

            if (ClockPattern().IsMatch(line))
            {
                violations.Add($"{location}: čas se bere z TimeProvider (konvence A2).");
            }

            if (line.Contains("Reservanto.ApiClient", StringComparison.Ordinal)
                && (path.StartsWith("src/AvailabilityView.Web/", StringComparison.Ordinal)
                    || path.StartsWith("src/AvailabilityView.Application/", StringComparison.Ordinal)))
            {
                violations.Add($"{location}: klient API se používá jen z Infrastructure (konvence A1).");
            }

            // .editorconfig to nastavuje, ale build porušení nehlásí, ověřeno podstrčeným this.View(...).
            if (ThisPattern().IsMatch(line))
            {
                violations.Add($"{location}: this. se nepíše (konvence B2).");
            }

            if (line.Contains("Html.Raw", StringComparison.Ordinal) && !$"{previous}{line}".Contains("SAFE:", StringComparison.Ordinal))
            {
                violations.Add($"{location}: Html.Raw jen s komentářem // SAFE: <důvod> (konvence D).");
            }

            // Nový druh zaměření je nová třída, ne další podmínka (pravidlo A3). Typy z API znají jen čtecí třídy.
            bool isSource = path.StartsWith(AvailabilitySources, StringComparison.Ordinal) && path.EndsWith("AvailabilitySource.cs", StringComparison.Ordinal);
            if (!isSource && SegmentTypePattern().IsMatch(line))
            {
                violations.Add($"{location}: větvení podle typu zaměření patří do čtecí třídy (konvence A3).");
            }

            // Klient smí volat jen endpointy z katalogu, který hlídají testy čtení a práv.
            if (path != EndpointCatalog && line.Contains("\"/v1/", StringComparison.Ordinal))
            {
                violations.Add($"{location}: cesta API patří jen do ApiEndpoints.cs.");
            }
        }

        return violations;
    }

    [GeneratedRegex(@"\bDateTime(Offset)?\.(Now|UtcNow|Today)\b")]
    private static partial Regex ClockPattern();

    // Názvy typů z API a porovnání nebo switch nad typem, ať se proměnná jmenuje jakkoli. Platí i pro views.
    [GeneratedRegex("""
        "(Classes|OneToOne|AutoClasses|EmsLike)"|(\.|\b)(SegmentType|Type)\s*(==|!=|\bis\b|switch\b)|\.(SegmentType|Type)\.Equals\(|switch\s*\([^)]*\.(SegmentType|Type)\s*\)
        """)]
    private static partial Regex SegmentTypePattern();

    [GeneratedRegex(@"\bthis\.\w")]
    private static partial Regex ThisPattern();
}
