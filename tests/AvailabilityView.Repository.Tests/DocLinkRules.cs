using System.Text.RegularExpressions;

namespace AvailabilityView.Repository.Tests;

/// <summary>
/// Odkazy na soubory v dokumentech musí vést na soubor nebo složku v repozitáři. Hlídá hlavně AI setup: odkaz na
/// dokument, který ve veřejném repozitáři není, by AI poslal hledat něco, co neexistuje.
/// </summary>
internal static partial class DocLinkRules
{
    private static readonly string[] _roots = ["docs/", "src/", "tests/", "eng/", ".claude/"];

    public static bool AppliesTo(string path) => path.EndsWith(".md", StringComparison.Ordinal);

    public static IReadOnlyList<string> Check(string path, string text, IReadOnlyCollection<string> files)
    {
        List<string> violations = [];
        string directory = path.Contains('/') ? path[..(path.LastIndexOf('/') + 1)] : string.Empty;

        foreach (Match match in CodeSpanPattern().Matches(text))
        {
            string target = Clean(match.Groups["path"].Value);
            if (_roots.Any(root => target.StartsWith(root, StringComparison.Ordinal)) && IsPlainPath(target) && !Exists(target, files))
            {
                violations.Add($"{path}:{LineOf(text, match.Index)}: odkaz na {target}, který v repozitáři není.");
            }
        }

        foreach (Match match in LinkPattern().Matches(text))
        {
            string raw = Clean(match.Groups["path"].Value);
            if (raw.Length == 0 || raw.Contains("://", StringComparison.Ordinal) || raw.StartsWith("mailto:", StringComparison.Ordinal) || !IsPlainPath(raw))
            {
                continue;
            }

            string target = Normalize(directory + raw);
            if (!Exists(target, files))
            {
                violations.Add($"{path}:{LineOf(text, match.Index)}: odkaz na {raw}, který v repozitáři není.");
            }
        }

        foreach (Match match in ImportPattern().Matches(text))
        {
            string target = Clean(match.Groups["path"].Value);
            if (!Exists(target, files))
            {
                violations.Add($"{path}:{LineOf(text, match.Index)}: import {target}, který v repozitáři není.");
            }
        }

        return violations;
    }

    private static bool Exists(string target, IReadOnlyCollection<string> files)
    {
        string folder = target.TrimEnd('/') + "/";
        return files.Contains(target) || files.Any(file => file.StartsWith(folder, StringComparison.Ordinal));
    }

    // Vzory a zástupné názvy (<Mx>, *.cs, {soubor}) nejsou odkazy na konkrétní soubor.
    private static bool IsPlainPath(string target) => target.IndexOfAny(['<', '>', '*', '{', '}', '…', ' ']) < 0;

    // Číslo řádku (soubor.cs:42) a kotva (#sekce) nejsou součástí cesty.
    private static string Clean(string target)
    {
        int anchor = target.IndexOf('#', StringComparison.Ordinal);
        string path = anchor >= 0 ? target[..anchor] : target;
        return LineSuffixPattern().Replace(path, string.Empty);
    }

    private static string Normalize(string path)
    {
        List<string> parts = [];
        foreach (string part in path.Split('/'))
        {
            if (part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
            }
            else if (part != "." && part.Length > 0)
            {
                parts.Add(part);
            }
        }

        return string.Join('/', parts) + (path.EndsWith('/') ? "/" : string.Empty);
    }

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    [GeneratedRegex(@"`(?<path>[^`\s]+)`")]
    private static partial Regex CodeSpanPattern();

    // Odkaz může mít za cestou titulek: "Titulek", 'Titulek' nebo (Titulek).
    [GeneratedRegex(@"\]\((?<path>[^)\s]+)(?:\s+(?:""[^""]*""|'[^']*'|\([^)]*\)))?\)")]
    private static partial Regex LinkPattern();

    // Import v CLAUDE.md: řádek @docs/soubor.md.
    [GeneratedRegex(@"^@(?<path>\S+)", RegexOptions.Multiline)]
    private static partial Regex ImportPattern();

    [GeneratedRegex(@":\d+(-\d+)?$")]
    private static partial Regex LineSuffixPattern();
}
