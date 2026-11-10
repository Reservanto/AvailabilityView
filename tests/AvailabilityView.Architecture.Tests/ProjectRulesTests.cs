using AvailabilityView.Tests.Shared;

namespace AvailabilityView.Architecture.Tests;

public sealed class ProjectRulesTests
{
    private const string PlainSdk = "Microsoft.NET.Sdk";

    private static ProjectInfo[] SolutionProjects() =>
        [.. Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "src"), "*.csproj", SearchOption.AllDirectories).Select(ProjectInfo.Load)];

    [Fact]
    public void Solution_FollowsAllowedDirection()
    {
        Assert.Empty(ProjectRules.Check(SolutionProjects()));
    }

    [Fact]
    public void Rules_SeeEveryProjectInSource()
    {
        // Pojistka proti běhu naprázdno: pravidlo se špatnou cestou by nevidělo nic a prošlo.
        Assert.Equal(ProjectRules.KnownProjects.Order(), SolutionProjects().Select(project => project.Name).Order());
    }

    [Theory]
    [InlineData(ProjectRules.Application, ProjectRules.ApiClient)]
    [InlineData(ProjectRules.Application, ProjectRules.Infrastructure)]
    [InlineData(ProjectRules.ApiClient, ProjectRules.Application)]
    [InlineData(ProjectRules.Infrastructure, ProjectRules.Web)]
    [InlineData(ProjectRules.Web, ProjectRules.ApiClient)]
    public void Rule_CatchesForbiddenReference(string project, string reference)
    {
        ProjectInfo[] projects = [new ProjectInfo(project, PlainSdk, [reference], [], [])];

        Assert.Contains(ProjectRules.Check(projects), violation => violation.Contains(reference, StringComparison.Ordinal));
    }

    [Fact]
    public void Rule_CatchesUnknownProject()
    {
        Assert.NotEmpty(ProjectRules.Check([new ProjectInfo("Novy.Projekt", PlainSdk, [], [], [])]));
    }

    [Fact]
    public void Rule_CatchesWebFrameworkOutsideWeb()
    {
        Assert.NotEmpty(ProjectRules.Check([new ProjectInfo(ProjectRules.Application, PlainSdk, [], ["Microsoft.AspNetCore.App"], [])]));
        Assert.NotEmpty(ProjectRules.Check([new ProjectInfo(ProjectRules.Application, "Microsoft.NET.Sdk.Web", [], [], [])]));
        Assert.NotEmpty(ProjectRules.Check([new ProjectInfo(ProjectRules.Application, PlainSdk, [], [], ["Microsoft.AspNetCore.Http.Abstractions"])]));
    }
}
