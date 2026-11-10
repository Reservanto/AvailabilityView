using AvailabilityView.Tests.Shared;

namespace AvailabilityView.Repository.Tests;

public sealed class PublicContentRulesTests
{
    // Porušení se skládají za běhu: literál v tomto souboru by shodil kontrolu celého repozitáře.
    private const string Doc = "docs/navod.md";
    private const string Code = "src/AvailabilityView.Web/Soubor.cs";

    private static readonly IReadOnlySet<string> _noMilestones = new HashSet<string>();

    [Fact]
    public void Repository_ContainsOnlyPublicContent()
    {
        var list = PrivateList.Load(RepositoryRoot.Path);
        string[] files = PublicFiles(list);
        IReadOnlySet<string> milestones = PublicContentRules.Milestones(files);

        AssertNone(TextFiles(files).SelectMany(path => PublicContentRules.Check(path, RepositoryFiles.Current.Read(path), list, milestones)));
    }

    [Fact]
    public void Repository_DocLinksLeadToPublicFiles()
    {
        string[] files = PublicFiles(PrivateList.Load(RepositoryRoot.Path));

        AssertNone(TextFiles(files).Where(DocLinkRules.AppliesTo).SelectMany(path => DocLinkRules.Check(path, RepositoryFiles.Current.Read(path), files)));
    }

    // Výpis všech porušení, Assert.Empty by ukázal jen několik prvních.
    private static void AssertNone(IEnumerable<string> violations)
    {
        string[] all = [.. violations];
        Assert.True(all.Length == 0, $"Porušení: {all.Length}{Environment.NewLine}{string.Join(Environment.NewLine, all)}");
    }

    [Theory]
    [InlineData(@":\Users\nekdo\projekt")]
    [InlineData(@":\Projekty\AvailabilityView")]
    [InlineData(":/Projekty/AvailabilityView")]
    public void Rule_CatchesWindowsPath(string rest)
    {
        Assert.NotEmpty(Check(Doc, $"Repozitář je v D{rest}."));
    }

    [Theory]
    [InlineData(@"Logy jsou v C:\inetpub\logs\LogFiles.")]
    [InlineData(@"Certifikát v Cert:\LocalMachine\My.")]
    [InlineData(@"Web v IIS:\Sites\AvailabilityView.")]
    [InlineData("""/^BLOCKED:/m.test(text)""")]
    public void Rule_AllowsIisAndProviderPaths(string text)
    {
        Assert.Empty(Check(Doc, text));
    }

    [Theory]
    [InlineData("/c/")]
    [InlineData("/home/")]
    [InlineData("/Users/")]
    public void Rule_CatchesPosixHomePath(string prefix)
    {
        Assert.NotEmpty(Check(Doc, $"cd {prefix}nekdo/projekt"));
    }

    [Theory]
    [InlineData("C:/", "Users/nekdo/web.css")]
    [InlineData("", "Users/nekdo/web.css")]
    [InlineData("", "home/nekdo/web.css")]
    public void Rule_CatchesLocalPathInFileUrl(string drive, string rest)
    {
        Assert.NotEmpty(Check(Doc, "Styl je v file://" + "/" + drive + rest));
    }

    [Theory]
    [InlineData(@"\\", @"PC-NEKDO\Users\nekdo\repo")]
    [InlineData("file://", "PC-NEKDO/Users/nekdo/repo/readme.md")]
    public void Rule_CatchesNetworkPath(string prefix, string rest)
    {
        Assert.NotEmpty(Check(Doc, $"Repozitář je na {prefix}{rest}."));
    }

    [Theory]
    [InlineData("""const file = "C:\\repo\\src\\Program.cs";""")]
    [InlineData("""[GeneratedRegex(@"[\\/]")] a .Replace(@"\\", @"\")""")]
    public void Rule_AllowsEscapedBackslashesInCode(string text)
    {
        Assert.Empty(Check(".claude/hooks/hook.test.mjs", text));
    }

    [Theory]
    [InlineData("/mnt/", "c/Users/nekdo/projekt")]
    [InlineData("/", "C/Projekty/AvailabilityView")]
    public void Rule_CatchesWslAndGitBashPaths(string prefix, string rest)
    {
        Assert.NotEmpty(Check(Doc, $"cd {prefix}{rest}"));
    }

    [Theory]
    [InlineData("https://api.reservanto.cz/c/x")]
    [InlineData("src/AvailabilityView.Web/Program.cs")]
    [InlineData("./c/d a ../c/d")]
    [InlineData("file:///etc/web.css")]
    public void Rule_AllowsUrlsAndRelativePaths(string text)
    {
        Assert.Empty(Check(Doc, text));
    }

    [Theory]
    [InlineData("jan", "@firma.cz")]
    [InlineData("Jan.Novak", "@Firma.cz")]
    [InlineData("jan", "@FIRMA.CZ")]
    [InlineData("kontakt", "@büro.de")]
    public void Rule_CatchesRealLookingEmail(string local, string domain)
    {
        Assert.NotEmpty(Check(Doc, $"Napište na {local}{domain}."));
    }

    [Theory]
    [InlineData("jan.novak@example.cz")]
    [InlineData("Jan.Novak@Example.cz")]
    [InlineData("kontakt@example.invalid")]
    [InlineData("jmeno@vas-web.cz")]
    [InlineData("podpora@reservanto.cz")]
    [InlineData("""<div class="av-filter-@field.Name">@Html.Raw(x)</div>""")]
    [InlineData("""<span class="av-to-@block.To av-lane-@block.Lane av-month__key--@key.Kind av-to-@grid.Rows">""")]
    public void Rule_AllowsInventedEmails_AndRazor(string text)
    {
        Assert.Empty(Check(Doc, text));
    }

    [Fact]
    public void Rule_CatchesOtherAddressOfApiProvider()
    {
        Assert.NotEmpty(Check(Doc, "jan" + "@reservanto.cz"));
    }

    [Fact]
    public void Rule_CatchesPrivateTerm_IgnoringCase_WithoutPrintingIt()
    {
        var list = PrivateList.Parse(["Tajny-Pocitac"]);

        string violation = Assert.Single(PublicContentRules.Check(Doc, "Spuštěno na TAJNY-POCITAC.", list, _noMilestones));
        Assert.DoesNotContain("pocitac", violation, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Novák", "Zapsal Novák.", true)]
    [InlineData("Novák", "Zapsal Nováka.", false)]
    [InlineData("Novák*", "Poslat Novákovi.", true)]
    [InlineData("Novák*", "Zapsal Sinovák.", false)]
    [InlineData("Interni.Kod", "using Interni.Kod.Data;", true)]
    [InlineData("Interni.Kod", "using InterniXKod;", false)]
    public void PrivateTerm_IsWholeWord_OrPrefixWithStar(string term, string text, bool caught)
    {
        IReadOnlyList<string> violations = PublicContentRules.Check(Doc, text, PrivateList.Parse([term]), _noMilestones);

        Assert.Equal(caught, violations.Count > 0);
    }

    [Fact]
    public void PrivateTerm_IsIgnoredInDownloadedApiDescription_OtherRulesAreNot()
    {
        const string ApiDescription = "tests/Reservanto.ApiClient.Tests/OpenApi/popis.yaml";
        var list = PrivateList.Parse(["Vyraz"]);

        Assert.Empty(PublicContentRules.Check(ApiDescription, "description: Vyraz", list, _noMilestones));
        Assert.NotEmpty(PublicContentRules.Check(ApiDescription, "jan" + "@firma.cz", list, _noMilestones));
    }

    [Theory]
    [InlineData(@"const file = ""C:\\repo\\src\\Program.cs"";", false)]
    [InlineData(@"const sdk = ""C:\\Program Files\\dotnet\\sdk"";", false)]
    [InlineData(@":\\projekty\\src\\Program.cs", true)]
    public void Rule_ReadsDoubledBackslashInStrings(string text, bool caught)
    {
        // Cizí cesta dostane písmeno disku až tady, jinak by ji kontrola našla v tomto souboru.
        string line = text.StartsWith(':') ? $"const file = \"D{text}\";" : text;

        Assert.Equal(caught, Check(".claude/hooks/hook.test.mjs", line).Count > 0);
    }

    [Theory]
    [InlineData("src/AvailabilityView.Web/Soubor.cs")]
    [InlineData("tests/AvailabilityView.Web.Tests/SouborTests.cs")]
    [InlineData("eng/check.ps1")]
    public void Rule_CatchesMilestoneInCode(string path)
    {
        Assert.NotEmpty(Check(path, "// Podle plánu M" + "6, návrh 2."));
    }

    [Fact]
    public void Rule_CatchesMilestoneInDocs_OnlyWhenItsFolderIsMissing()
    {
        IReadOnlySet<string> milestones = PublicContentRules.Milestones(["docs/milestones/M" + "6/plan.md", "docs/SPEC.md"]);

        Assert.Empty(PublicContentRules.Check(Doc, "Viz plán M" + "6.", PrivateList.Empty, milestones));
        Assert.NotEmpty(PublicContentRules.Check(Doc, "Viz plán M" + "4.", PrivateList.Empty, milestones));
    }

    [Fact]
    public void Rule_IgnoresWordsWithM()
    {
        Assert.Empty(Check(Code, "var M = MaxDays; // M3U8 a Mx nejsou milníky"));
    }

    [Fact]
    public void PrivateList_ReadsTermsAndExcludes_SkipsComments()
    {
        var list = PrivateList.Parse(["# poznámka", "", "  vyraz  ", "exclude: docs/stare/", "EXCLUDE:docs/x.md"]);

        Assert.Equal(["vyraz"], list.Terms);
        Assert.Equal(["docs/stare/", "docs/x.md"], list.Excludes);
        Assert.True(list.IsExcluded("docs/stare/plan.md"));
        Assert.False(list.IsExcluded("docs/SPEC.md"));
    }

    [Fact]
    public void PrivateList_IsEmpty_WhenFileIsMissing()
    {
        string root = Directory.CreateTempSubdirectory("availabilityview-").FullName;
        try
        {
            Assert.Same(PrivateList.Empty, PrivateList.Load(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PrivateList_IsIgnoredByGit()
    {
        Assert.True(RepositoryFiles.Current.IsIgnored(PrivateList.FileName));
    }

    [Fact]
    public void ExcludedMilestoneFolder_DoesNotCountAsPresent()
    {
        var list = PrivateList.Parse(["exclude: docs/milestones/M" + "4/"]);
        string[] files = ["docs/milestones/M" + "4/plan.md", "docs/SPEC.md"];

        IReadOnlySet<string> milestones = PublicContentRules.Milestones(files.Where(path => !list.IsExcluded(path)));

        Assert.NotEmpty(PublicContentRules.Check(Doc, "Viz M" + "4.", list, milestones));
    }

    [Fact]
    public void DocLinks_CatchMissingFile_InCodeSpan_Link_AndImport()
    {
        string[] files = ["docs/SPEC.md", "CLAUDE.md"];

        Assert.NotEmpty(DocLinkRules.Check("CLAUDE.md", "Čti `docs/chybi.md`.", files));
        Assert.NotEmpty(DocLinkRules.Check("docs/SPEC.md", "Viz [návod](chybi.md).", files));
        Assert.NotEmpty(DocLinkRules.Check("docs/SPEC.md", "Viz [návod](chybi.md \"Návod\").", files));
        Assert.NotEmpty(DocLinkRules.Check("docs/SPEC.md", "Viz [návod](chybi.md 'Návod').", files));
        Assert.NotEmpty(DocLinkRules.Check("docs/SPEC.md", "Viz [návod](chybi.md (Návod)).", files));
        Assert.NotEmpty(DocLinkRules.Check("CLAUDE.md", "@docs/chybi.md", files));
    }

    [Fact]
    public void DocLinks_AcceptExistingFilesAndFolders_WithAnchorsAndLines()
    {
        string[] files = ["docs/SPEC.md", "docs/instalace-iis.md", "src/AvailabilityView.Web/Program.cs", "CLAUDE.md"];
        string text = """
            Čti `docs/SPEC.md`, `src/AvailabilityView.Web/` a `src/AvailabilityView.Web/Program.cs:12`.
            Viz [návod](instalace-iis.md#log), [spec](./SPEC.md), [web](https://example.cz/x.md) a [kořen](../CLAUDE.md).
            @docs/SPEC.md
            """;

        Assert.Empty(DocLinkRules.Check("docs/SPEC.md", text, files));
    }

    [Theory]
    [InlineData("`docs/milestones/<Mx>/plan.md`")]
    [InlineData("`src/**/*.cs`")]
    [InlineData("`appsettings.json`")]
    public void DocLinks_SkipPatternsAndBareNames(string text)
    {
        Assert.Empty(DocLinkRules.Check("docs/SPEC.md", text, ["docs/SPEC.md"]));
    }

    private static IReadOnlyList<string> Check(string path, string text) =>
        PublicContentRules.Check(path, text, PrivateList.Empty, _noMilestones);

    private static string[] PublicFiles(PrivateList list) =>
        [.. RepositoryFiles.Current.All().Where(path => !list.IsExcluded(path))];

    private static IEnumerable<string> TextFiles(IEnumerable<string> files)
    {
        HashSet<string> text = [.. RepositoryFiles.Current.Text()];
        return files.Where(text.Contains);
    }
}
