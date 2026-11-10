namespace AvailabilityView.Repository.Tests;

/// <summary>
/// Místní seznam mimo git: soukromé výrazy, které se v repozitáři nesmí objevit, a soubory, které do veřejného
/// repozitáře nepatří a kontrola veřejného obsahu je přeskočí. V čistém klonu soubor není a seznam je prázdný.
/// Formát: výraz na řádek (celé slovo, s <c>*</c> na konci i jeho tvary), <c>exclude: &lt;cesta nebo začátek cesty&gt;</c>,
/// <c>#</c> uvozuje poznámku.
/// </summary>
internal sealed record PrivateList(IReadOnlyList<string> Terms, IReadOnlyList<string> Excludes)
{
    public const string FileName = ".private/public-check.txt";

    private const string ExcludePrefix = "exclude:";

    public static PrivateList Empty { get; } = new([], []);

    public static PrivateList Load(string root)
    {
        string path = Path.Combine(root, FileName);
        return File.Exists(path) ? Parse(File.ReadAllLines(path)) : Empty;
    }

    public static PrivateList Parse(IEnumerable<string> lines)
    {
        List<string> terms = [];
        List<string> excludes = [];
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith(ExcludePrefix, StringComparison.OrdinalIgnoreCase))
            {
                excludes.Add(line[ExcludePrefix.Length..].Trim());
            }
            else
            {
                terms.Add(line);
            }
        }

        return new PrivateList(terms, excludes);
    }

    public bool IsExcluded(string path) => Excludes.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal));
}
