using System.Diagnostics;
using System.Globalization;

namespace AvailabilityView.Repository.Tests;

public sealed class SecretRulesTests
{
    // GUID se skládá za běhu: literál v tomto souboru by shodil kontrolu celého repozitáře.
    private static readonly string _guid = Guid.NewGuid().ToString();

    [Fact]
    public void Repository_ContainsNoSecrets()
    {
        RepositoryFiles repository = RepositoryFiles.Current;
        string[] violations =
        [
            .. repository.All().Concat(repository.StagedPaths()).SelectMany(SecretRules.CheckPath),
            .. repository.Text().SelectMany(path => SecretRules.Check(path, repository.Read(path))),
            .. repository.Staged().SelectMany(file => SecretRules.Check(file.Path, file.Content)),
        ];

        Assert.Empty(violations);
    }

    [Fact]
    public void Scan_SeesTrackedSourcesAndDocs()
    {
        // Pojistka proti běhu naprázdno.
        IReadOnlyList<string> files = RepositoryFiles.Current.All();

        Assert.Contains("docs/SPEC.md", files);
        Assert.Contains("src/AvailabilityView.Web/Program.cs", files);
    }

    [Fact]
    public void Scan_SeesFileNotYetInGit_AndStagedContentCleanedOnlyInWorkingCopy()
    {
        string root = Directory.CreateTempSubdirectory("availabilityview-").FullName;
        try
        {
            RunGit(root, "init", "-q");
            File.WriteAllText(Path.Combine(root, "novy.txt"), "obsah");
            Assert.Contains("novy.txt", new RepositoryFiles(root).All());

            string staged = Path.Combine(root, "appsettings.json");
            File.WriteAllText(staged, _guid);
            RunGit(root, "add", "appsettings.json");
            File.WriteAllText(staged, "vycisteno");

            (string path, string content) = Assert.Single(new RepositoryFiles(root).Staged());
            Assert.Equal("appsettings.json", path);
            Assert.NotEmpty(SecretRules.Check(path, content));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Scan_SeesStagedArchiveDeletedFromWorkingCopy()
    {
        string root = Directory.CreateTempSubdirectory("availabilityview-").FullName;
        try
        {
            RunGit(root, "init", "-q");
            string archive = Path.Combine(root, "zaloha.gz");
            File.WriteAllText(archive, "obsah");
            RunGit(root, "add", "zaloha.gz");
            File.Delete(archive);

            var repository = new RepositoryFiles(root);

            Assert.DoesNotContain("zaloha.gz", repository.All());
            Assert.NotEmpty(SecretRules.CheckPath(Assert.Single(repository.StagedPaths())));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("src/AvailabilityView.Web/appsettings.Production.json")]
    [InlineData("src/AvailabilityView.Web/appsettings.Development.local.json")]
    public void ProductionConfiguration_IsIgnoredByGit(string path)
    {
        Assert.True(RepositoryFiles.Current.IsIgnored(path));
    }

    [Fact]
    public void GitIgnoreCheck_RecognizesNormalFile()
    {
        Assert.False(RepositoryFiles.Current.IsIgnored("src/AvailabilityView.Web/appsettings.json"));
    }

    [Theory]
    [InlineData("{0}")]
    [InlineData("\"ClientId\": \"{0}\"")]
    [InlineData("https://api.reservanto.cz/v1/Authorize/GetLongTimeToken?clientId={0}&rights=Segment_r")]
    [InlineData("https%3A%2F%2Flocalhost%2FAuth%2FCallback%3Faccepted%3D1%26token%3D{0}%26x%3D1")]
    [InlineData("token_{0}")]
    [InlineData("ltt{0}")]
    [InlineData("radek\\n{0}")]
    public void Rule_CatchesGuid(string template)
    {
        Assert.NotEmpty(SecretRules.Check("soubor.txt", string.Format(CultureInfo.InvariantCulture, template, _guid)));
    }

    [Fact]
    public void Rule_CatchesUppercaseGuid()
    {
        Assert.NotEmpty(SecretRules.Check("soubor.txt", _guid.ToUpperInvariant()));
    }

    [Theory]
    [InlineData("\"LongTimeToken\": \"neco-tajneho\"")]
    [InlineData("LongTimeToken = \"neco-tajneho\"")]
    [InlineData("[\"Reservanto:LongTimeToken\"] = \"neco-tajneho\"")]
    [InlineData("dotnet user-secrets set \"Reservanto:LongTimeToken\" \"neco-tajneho\"")]
    [InlineData("\"ClientId\": \"neco\"")]
    public void Rule_CatchesAssignedValue(string text)
    {
        Assert.NotEmpty(SecretRules.Check("soubor.txt", text));
    }

    [Theory]
    [InlineData("\"LongTimeToken\": \"\"")]
    [InlineData("[\"Reservanto:LongTimeToken\"] = \"ltt-testu\"")]
    [InlineData("dotnet user-secrets set \"Reservanto:LongTimeToken\" \"<token>\"")]
    [InlineData("LongTimeToken je tajný údaj")]
    [InlineData("{\"ErrorParameter\":\"LongTimeToken\",\"ErrorParameters\":[\"LongTimeToken\"]}\"")]
    [InlineData("{\"ErrorParameter\":\"LongTimeToken\"}\"")]
    public void Rule_AllowsPlaceholders(string text)
    {
        Assert.Empty(SecretRules.Check("soubor.txt", text));
    }

    [Theory]
    [InlineData("docs/konfigurace.zip")]
    [InlineData("src/AvailabilityView.Web/appsettings.json.bak")]
    public void Rule_CatchesArchiveOrBackup(string path)
    {
        Assert.NotEmpty(SecretRules.CheckPath(path));
    }

    [Fact]
    public void Rule_AllowsNormalFile()
    {
        Assert.Empty(SecretRules.CheckPath("docs/SPEC.md"));
    }

    [Fact]
    public void Violation_DoesNotRevealValue()
    {
        string violation = Assert.Single(SecretRules.Check("soubor.txt", "\"LongTimeToken\": \"neco-tajneho\""));

        Assert.DoesNotContain("neco-tajneho", violation, StringComparison.Ordinal);
    }

    private static void RunGit(string root, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    // Git zapisuje objekty jen pro čtení a Windows takové soubory smazat odmítne.
    private static void DeleteDirectory(string root)
    {
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(root, recursive: true);
    }
}
