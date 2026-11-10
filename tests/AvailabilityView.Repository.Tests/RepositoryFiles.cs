using System.Diagnostics;
using System.Text;
using AvailabilityView.Tests.Shared;

namespace AvailabilityView.Repository.Tests;

/// <summary>Soubory v repozitáři: sledované, nové, které ještě nejsou v gitu, a obsah připravený ke commitu.</summary>
internal sealed class RepositoryFiles(string root)
{
    private static readonly HashSet<string> _binaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".ico", ".pdf", ".dll", ".exe", ".woff", ".woff2",
    };

    public static RepositoryFiles Current { get; } = new(RepositoryRoot.Path);

    /// <summary>Relativní cesty s lomítky. Smazané soubory, které git ještě eviduje, se přeskočí.</summary>
    public IReadOnlyList<string> All() =>
        [.. Git("ls-files", "-z", "--cached", "--others", "--exclude-standard")
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .Where(path => File.Exists(FullPath(path)))];

    public IEnumerable<string> Text() => All().Where(IsText);

    public string Read(string path) => File.ReadAllText(FullPath(path), Encoding.UTF8);

    /// <summary>Obsah připravený ke commitu. Může se lišit od pracovní kopie, když se soubor po <c>git add</c> upravil.</summary>
    public IEnumerable<(string Path, string Content)> Staged() =>
        StagedPaths().Where(IsText).Select(path => (path, Git("show", $":{path}")));

    /// <summary>Cesty připravené ke commitu, i když soubor v pracovní kopii už není.</summary>
    public IReadOnlyList<string> StagedPaths() =>
        Git("diff", "--cached", "--name-only", "-z", "--diff-filter=ACMR").Split('\0', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Ignoruje git danou cestu?</summary>
    public bool IsIgnored(string path)
    {
        using Process process = StartGit("check-ignore", "-q", path);
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    private static bool IsText(string path) => !_binaryExtensions.Contains(Path.GetExtension(path));

    private string FullPath(string path) => Path.Combine(root, path);

    private string Git(params string[] arguments)
    {
        using Process process = StartGit(arguments);
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0
            ? output
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} skončil kódem {process.ExitCode}.");
    }

    private Process StartGit(params string[] arguments)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info)!;
    }
}
