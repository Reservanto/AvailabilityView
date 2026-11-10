using System.Reflection;
using ArchUnitNET.Loader;
using Reservanto.ApiClient;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using ArchitectureModel = ArchUnitNET.Domain.Architecture;

namespace AvailabilityView.Architecture.Tests;

/// <summary>
/// Závislosti na úrovni typů. Web vidí <c>Reservanto.ApiClient</c> přes tranzitivní referenci, kterou pravidla nad
/// csproj nezachytí, proto je potřeba hlídat skutečné použití typů.
/// </summary>
public sealed class TypeRulesTests
{
    private static readonly string[] _solutionAssemblies = [ProjectRules.Web, ProjectRules.Application, ProjectRules.Infrastructure, ProjectRules.ApiClient];
    private static readonly string _testAssembly = typeof(TypeRulesTests).Assembly.GetName().Name!;

    private static readonly ArchitectureModel _architecture = new ArchLoader()
        .LoadAssemblies([.. _solutionAssemblies.Select(Assembly.Load), typeof(TypeRulesTests).Assembly])
        .Build();

    [Theory]
    [InlineData(ProjectRules.Web, ProjectRules.ApiClient)]
    [InlineData(ProjectRules.Application, ProjectRules.Web)]
    [InlineData(ProjectRules.Application, ProjectRules.Infrastructure)]
    [InlineData(ProjectRules.Application, ProjectRules.ApiClient)]
    [InlineData(ProjectRules.ApiClient, ProjectRules.Application)]
    [InlineData(ProjectRules.ApiClient, ProjectRules.Web)]
    [InlineData(ProjectRules.ApiClient, ProjectRules.Infrastructure)]
    public void Solution_DoesNotUseForbiddenAssembly(string source, string target)
    {
        Assert.False(Uses(source, target), $"{source} nesmí používat typy z {target}.");
    }

    [Fact]
    public void Web_UsesInfrastructureOnlyInProgram()
    {
        Assert.True(Types().That().ResideInAssemblyMatching(Exactly(ProjectRules.Web))
            .And().DoNotHaveFullName("Program")
            .Should().NotDependOnAnyTypesThat().ResideInAssemblyMatching(Exactly(ProjectRules.Infrastructure))
            .HasNoViolations(_architecture));
    }

    [Theory]
    [InlineData(ProjectRules.Web)]
    [InlineData(ProjectRules.Application)]
    [InlineData(ProjectRules.Infrastructure)]
    [InlineData(ProjectRules.ApiClient)]
    public void Rules_SeeTypesOfEveryProject(string assembly)
    {
        // Pojistka proti běhu naprázdno.
        Assert.NotEmpty(Types().That().ResideInAssemblyMatching(Exactly(assembly)).GetObjects(_architecture));
    }

    [Fact]
    public void Rule_CatchesUseOfForbiddenType()
    {
        // Sestavení testů obsahuje UsesApiClient níže, pravidlo musí porušení vidět.
        Assert.True(Uses(_testAssembly, ProjectRules.ApiClient));
    }

    private static bool Uses(string source, string target) =>
        !Types().That().ResideInAssemblyMatching(Exactly(source))
            .Should().NotDependOnAnyTypesThat().ResideInAssemblyMatching(Exactly(target))
            .HasNoViolations(_architecture);

    private static string Exactly(string assembly) => $"^{assembly.Replace(".", "\\.", StringComparison.Ordinal)}(,.*)?$";

    /// <summary>Úmyslné porušení pro negativní test.</summary>
    internal sealed class UsesApiClient
    {
        public ReservantoApiOptions Options { get; } = new();
    }
}
