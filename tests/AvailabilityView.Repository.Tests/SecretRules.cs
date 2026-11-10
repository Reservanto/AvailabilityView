using System.Text.RegularExpressions;

namespace AvailabilityView.Repository.Tests;

/// <summary>
/// Tajné údaje v souborech. ClientId i LongTimeToken jsou GUID, takže pravidlo hlídá každý GUID v repozitáři a
/// každou hodnotu přiřazenou klíči těchto údajů. Vrací popis porušení bez samotné hodnoty, aby se neobjevila ve výstupu.
/// </summary>
internal static partial class SecretRules
{
    // Vzorové hodnoty, které nejsou tajné.
    private static readonly HashSet<string> _allowedValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "ltt-testu",
        "REDACTED",
        "<LTT>",
        "<token>",
        "<ClientId>",
    };

    // Do obsahu těchto souborů pravidlo nevidí, záloha konfigurace v archivu by prošla.
    private static readonly HashSet<string> _forbiddenExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".bak", ".pfx", ".p12",
    };

    public static IReadOnlyList<string> CheckPath(string path) =>
        _forbiddenExtensions.Contains(Path.GetExtension(path))
            ? [$"{path}: archiv nebo záloha do repozitáře nepatří, kontrola do nich nevidí."]
            : [];

    public static IReadOnlyList<string> Check(string path, string text)
    {
        List<string> violations = [];
        foreach (Match match in GuidPattern().Matches(text))
        {
            violations.Add($"{path}:{LineOf(text, match.Index)}: GUID, může to být ClientId nebo LongTimeToken.");
        }

        foreach (Match match in AssignmentPattern().Matches(text))
        {
            if (!_allowedValues.Contains(match.Groups["value"].Value))
            {
                violations.Add($"{path}:{LineOf(text, match.Index)}: hodnota klíče {match.Groups["key"].Value}.");
            }
        }

        return violations;
    }

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    // Bez hranic slova: GUID bývá nalepený na text, třeba za procentním kódem v zakódované adrese (%3D).
    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    // Přiřazení v JSON a C# i příkaz pro user-secrets, kde hodnota následuje za klíčem v uvozovkách. Hodnota nezačíná
    // čárkou ani závorkou: "LongTimeToken","Jiny" a ["LongTimeToken"] jsou názvy, ne přiřazení.
    [GeneratedRegex("""(?<key>LongTimeToken|ClientId|clientId)["\]]*\s*[:=]?\s*"(?<value>[^"\s:,\]}][^"\r\n]*)"(?!\s*\+)""")]
    private static partial Regex AssignmentPattern();
}
