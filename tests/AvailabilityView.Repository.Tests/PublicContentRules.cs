using System.Text.RegularExpressions;

namespace AvailabilityView.Repository.Tests;

/// <summary>
/// Repozitář je veřejný, takže v něm nesmí být místní cesty, cizí e-maily, soukromé výrazy ani odkazy na milníky,
/// které v repozitáři nejsou. Soukromé výrazy (jména počítačů a účtů, názvy jiných projektů) by samy byly únikem,
/// proto je nese jen místní soubor mimo git (<see cref="PrivateList"/>). Vrací popis porušení bez soukromého výrazu.
/// </summary>
internal static partial class PublicContentRules
{
    private const string VerbatimApiDescription = "tests/Reservanto.ApiClient.Tests/OpenApi/";

    // Systémové cesty z návodu pro IIS a výstupu MSBuildu a vymyšlený kořen repozitáře v testech hooku.
    private static readonly string[] _allowedPathPrefixes = [@"C:\inetpub\", @"C:\Windows\", @"C:\Program Files\", @"C:\repo\"];

    // Vymyšlené domény v testech a adresa podpory z popisu API.
    private static readonly HashSet<string> _allowedEmailDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "example.cz",
        "example.com",
        "example.invalid",
        "vas-web.cz",
        "www.vas-web.cz",
    };

    // Veřejné adresy poskytovatele API: podpora z popisu API, obchod přiděluje ClientId.
    private static readonly HashSet<string> _allowedEmails = new(StringComparer.OrdinalIgnoreCase)
    {
        "podpora@reservanto.cz",
        "obchod@reservanto.cz",
    };

    private static readonly string[] _codeRoots = ["src/", "tests/", "eng/"];

    public static IReadOnlyList<string> Check(string path, string text, PrivateList list, IReadOnlySet<string> milestones)
    {
        List<string> violations = [];

        // Adresa file:/// nese místní cestu. Mezera místo druhého lomítka ji ukáže vzorům cest, délka textu se nemění.
        string paths = text.Replace("file:///", "file:/ /", StringComparison.OrdinalIgnoreCase);
        foreach (Match match in WindowsPathPattern().Matches(paths))
        {
            // V řetězcích JavaScriptu a C# bývá lomítko zdvojené. Povolená cesta může mít mezeru (Program Files),
            // proto se porovnává text za nálezem, ne jen nález.
            string found = paths.Substring(match.Index, Math.Min(64, paths.Length - match.Index))
                .Replace(@"\\", @"\", StringComparison.Ordinal)
                .Replace('/', '\\');
            if (!_allowedPathPrefixes.Any(prefix => found.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                violations.Add($"{path}:{LineOf(text, match.Index)}: místní cesta na disku.");
            }
        }

        foreach (Match match in PosixPathPattern().Matches(paths).Concat(NetworkPathPattern().Matches(text)))
        {
            violations.Add($"{path}:{LineOf(text, match.Index)}: místní cesta na disku.");
        }

        foreach (Match match in EmailPattern().Matches(text))
        {
            if (!_allowedEmails.Contains(match.Value) && !_allowedEmailDomains.Contains(match.Groups["domain"].Value))
            {
                violations.Add($"{path}:{LineOf(text, match.Index)}: e-mail, který není vymyšlený.");
            }
        }

        // Stažený popis API je dokument poskytovatele, výraz v něm nic neprozrazuje a upravit ho nejde.
        bool isVerbatim = path.StartsWith(VerbatimApiDescription, StringComparison.Ordinal);
        for (int index = 0; index < list.Terms.Count && !isVerbatim; index++)
        {
            Match match = TermPattern(list.Terms[index]).Match(text);
            if (match.Success)
            {
                violations.Add($"{path}:{LineOf(text, match.Index)}: soukromý výraz č. {index + 1} z {PrivateList.FileName}.");
            }
        }

        bool isCode = _codeRoots.Any(root => path.StartsWith(root, StringComparison.Ordinal));
        foreach (Match match in MilestonePattern().Matches(text))
        {
            if (isCode)
            {
                violations.Add($"{path}:{LineOf(text, match.Index)}: odkaz na milník v kódu, komentář má nést důvod sám.");
            }
            else if (!milestones.Contains(match.Value))
            {
                violations.Add($"{path}:{LineOf(text, match.Index)}: odkaz na milník {match.Value}, který v repozitáři není.");
            }
        }

        return violations;
    }

    /// <summary>Milníky, které v repozitáři jsou, podle složek <c>docs/milestones/&lt;Mx&gt;/</c>.</summary>
    public static IReadOnlySet<string> Milestones(IEnumerable<string> files) =>
        files.Select(path => MilestoneFolderPattern().Match(path))
            .Where(match => match.Success)
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Výraz je celé slovo bez ohledu na velikost písmen. Hvězdička na konci dovolí pokračování slova, kvůli českým
    /// tvarům (<c>Novák*</c> chytí i Nováka a Novákovi).
    /// </summary>
    public static Regex TermPattern(string term)
    {
        bool isPrefix = term.EndsWith('*');
        string word = Regex.Escape(isPrefix ? term[..^1] : term);
        return new Regex(isPrefix ? $@"(?<!\w){word}" : $@"(?<!\w){word}(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    // Písmeno disku samotné: Cert:\ a IIS:\ nejsou cesty na disku vývojáře.
    [GeneratedRegex(@"(?<![A-Za-z\\])[A-Za-z]:[\\/][^\s""'`<>|*?)\]]+")]
    private static partial Regex WindowsPathPattern();

    // Git Bash (/c/..., /C/...), WSL (/mnt/c/...) a domovské složky na Linuxu a macOS.
    [GeneratedRegex(@"(?<![\w.:/-])/(?:[A-Za-z]/|mnt/[a-z]/|Users/|home/)\w")]
    private static partial Regex PosixPathPattern();

    // Síťová cesta nese jméno počítače: dvě zpětná lomítka a jméno, nebo file:// a jméno. Před zpětnými lomítky nesmí
    // být písmeno, dvojtečka, lomítko ani uvozovka, zdvojené lomítko v řetězci kódu síťová cesta není.
    [GeneratedRegex(@"(?<![\w:\\""@])\\\\[A-Za-z0-9.-]+\\[^\s""'`<>|]+|file://(?!/)[A-Za-z0-9.-]+/", RegexOptions.IgnoreCase)]
    private static partial Regex NetworkPathPattern();

    // Koncovka domény celá malými, nebo celá velkými písmeny: Razor @field.Name ani @Html.Raw nejsou e-maily.
    // Doména smí mít diakritiku (büro.de).
    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@(?<domain>[\p{L}\p{N}-]+(?:\.[\p{L}\p{N}-]+)*\.(?:[a-z]{2,}|[A-Z]{2,}))\b")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\bM\d+\b")]
    private static partial Regex MilestonePattern();

    [GeneratedRegex(@"^docs/milestones/(?<name>M\d+)/")]
    private static partial Regex MilestoneFolderPattern();
}
