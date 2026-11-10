using System.Xml.Linq;

namespace AvailabilityView.Architecture.Tests;

/// <summary>Co víme o projektu z jeho csproj.</summary>
internal sealed record ProjectInfo(
    string Name,
    string Sdk,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> FrameworkReferences,
    IReadOnlyList<string> PackageReferences)
{
    public static ProjectInfo Load(string csprojPath)
    {
        XElement root = XDocument.Load(csprojPath).Root!;
        string[] references = [.. root.Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(((string)reference.Attribute("Include")!).Replace('\\', '/')))];
        string[] frameworks = [.. root.Descendants("FrameworkReference").Select(reference => (string)reference.Attribute("Include")!)];
        string[] packages = [.. root.Descendants("PackageReference").Select(reference => (string)reference.Attribute("Include")!)];
        return new ProjectInfo(Path.GetFileNameWithoutExtension(csprojPath), (string)root.Attribute("Sdk")!, references, frameworks, packages);
    }
}

/// <summary>Povolený směr závislostí mezi projekty. Vrací popis porušení, prázdný seznam = v pořádku.</summary>
internal static class ProjectRules
{
    public const string Application = "AvailabilityView.Application";
    public const string Infrastructure = "AvailabilityView.Infrastructure";
    public const string Web = "AvailabilityView.Web";
    public const string ApiClient = "Reservanto.ApiClient";

    private const string WebSdk = "Microsoft.NET.Sdk.Web";
    private const string WebPackagePrefix = "Microsoft.AspNetCore.";

    private static readonly Dictionary<string, string[]> _allowedReferences = new(StringComparer.OrdinalIgnoreCase)
    {
        [Application] = [],
        [ApiClient] = [],
        [Infrastructure] = [Application, ApiClient],
        [Web] = [Application, Infrastructure],
    };

    public static IEnumerable<string> KnownProjects => _allowedReferences.Keys;

    public static IReadOnlyList<string> Check(IEnumerable<ProjectInfo> projects)
    {
        List<string> violations = [];
        foreach (ProjectInfo project in projects)
        {
            if (!_allowedReferences.TryGetValue(project.Name, out string[]? allowed))
            {
                violations.Add($"{project.Name}: projekt není v povoleném směru závislostí, doplň ho do ProjectRules.");
                continue;
            }

            violations.AddRange(project.ProjectReferences
                .Where(reference => !allowed.Contains(reference, StringComparer.OrdinalIgnoreCase))
                .Select(reference => $"{project.Name} nesmí záviset na {reference}."));

            bool isWeb = string.Equals(project.Name, Web, StringComparison.OrdinalIgnoreCase);
            bool usesWebFramework = string.Equals(project.Sdk, WebSdk, StringComparison.OrdinalIgnoreCase)
                || project.FrameworkReferences.Count > 0
                || project.PackageReferences.Any(package => package.StartsWith(WebPackagePrefix, StringComparison.OrdinalIgnoreCase));
            if (!isWeb && usesWebFramework)
            {
                violations.Add($"{project.Name} nesmí používat webový framework.");
            }
        }

        return violations;
    }
}
