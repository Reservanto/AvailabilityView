namespace AvailabilityView.Repository.Tests;

public sealed class CodeRulesTests
{
    private const string SourceFile = "src/AvailabilityView.Web/Soubor.cs";

    [Fact]
    public void Sources_FollowCodeRules()
    {
        string[] files = [.. RepositoryFiles.Current.Text().Where(CodeRules.AppliesTo)];

        Assert.NotEmpty(files);
        Assert.Empty(files.SelectMany(path => CodeRules.Check(path, RepositoryFiles.Current.Read(path))));
    }

    [Theory]
    [InlineData("src/AvailabilityView.Web/Home/HomeController.cs")]
    [InlineData("src/AvailabilityView.Web/Views/Home/Index.cshtml")]
    [InlineData("src/AvailabilityView.Application/Connection/Soubor.cs")]
    public void Rule_CatchesApiClientOutsideInfrastructure(string path)
    {
        // ArchUnitNET nevidí catch (X) ani konstanty vložené překladačem, ověřeno na podstrčeném kódu.
        Assert.NotEmpty(CodeRules.Check(path, "using Reservanto.ApiClient.Core;"));
    }

    [Theory]
    [InlineData("""if (segment.Type == "Classes")""")]
    [InlineData("if (segment.Type is SupportedType)")]
    [InlineData("return model.SegmentType != other;")]
    [InlineData("switch (selection.Segment.Type)")]
    [InlineData("""string type = "EmsLike";""")]
    [InlineData("if (candidate.Type == IndividualType)")]
    [InlineData("switch (candidate.Type)")]
    [InlineData("return item.Type.Equals(other, StringComparison.Ordinal);")]
    [InlineData("return selection.Segment.Type switch")]
    public void Rule_CatchesBranchingBySegmentType(string line)
    {
        Assert.NotEmpty(CodeRules.Check("src/AvailabilityView.Application/Availability/Soubor.cs", line));
    }

    [Theory]
    [InlineData("""@if (Model.Selection.Segment.Type == "Classes")""")]
    [InlineData("""@if (Model.Selection.Segment.Type is "OneToOne") { }""")]
    public void Rule_CatchesBranchingBySegmentType_InViews(string line)
    {
        Assert.NotEmpty(CodeRules.Check("src/AvailabilityView.Web/Views/Availability/Index.cshtml", line));
    }

    [Fact]
    public void Rule_AllowsSegmentTypesInSources()
    {
        const string Line = """public IReadOnlyList<string> SegmentTypes { get; } = ["Classes"];""";

        Assert.Empty(CodeRules.Check("src/AvailabilityView.Infrastructure/Availability/ClassesAvailabilitySource.cs", Line));
    }

    [Fact]
    public void Rule_AllowsLookupBySegmentType()
    {
        Assert.Empty(CodeRules.Check("src/AvailabilityView.Application/Availability/Soubor.cs", "sources.ContainsKey(segment.Type)"));
    }

    [Fact]
    public void Rule_AllowsApiClientInInfrastructure()
    {
        Assert.Empty(CodeRules.Check("src/AvailabilityView.Infrastructure/Soubor.cs", "using Reservanto.ApiClient;"));
    }

    [Theory]
    [InlineData("var now = DateTime.Now;")]
    [InlineData("var now = DateTime.UtcNow;")]
    [InlineData("var now = DateTimeOffset.UtcNow;")]
    [InlineData("var today = DateTime.Today;")]
    [InlineData("@Html.Raw(Model.Text)")]
    [InlineData("return this.View();")]
    [InlineData("new ApiEndpoint(\"/v1/Classes/CreateBooking\", \"FreeSpace_r\")")]
    public void Rule_CatchesViolation(string line)
    {
        Assert.NotEmpty(CodeRules.Check(SourceFile, line));
    }

    [Theory]
    [InlineData("timeProvider.GetUtcNow()")]
    [InlineData("// SAFE: obsah je z resx\n@Html.Raw(Model.Text)")]
    [InlineData("/// <summary><c>/v1/Segment/*</c></summary>")]
    public void Rule_AllowsCorrectCode(string text)
    {
        Assert.Empty(CodeRules.Check(SourceFile, text));
    }

    [Fact]
    public void Rule_AllowsApiPathsInEndpointCatalog()
    {
        Assert.Empty(CodeRules.Check("src/Reservanto.ApiClient/Core/ApiEndpoints.cs", "new(\"/v1/Segment/GetList\", \"Segment_r\")"));
    }

    [Theory]
    [InlineData("src/AvailabilityView.Web/Program.cs", true)]
    [InlineData("src/AvailabilityView.Web/Views/Home/Index.cshtml", true)]
    [InlineData("tests/AvailabilityView.Repository.Tests/CodeRules.cs", false)]
    [InlineData("docs/SPEC.md", false)]
    public void Rule_AppliesOnlyToSources(string path, bool expected)
    {
        Assert.Equal(expected, CodeRules.AppliesTo(path));
    }
}
