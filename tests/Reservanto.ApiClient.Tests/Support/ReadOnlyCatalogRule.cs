using Reservanto.ApiClient.Core;

namespace Reservanto.ApiClient.Tests.Support;

/// <summary>Pravidlo „projekt v Reservantu jen čte“. Vrací popis porušení, prázdný seznam = v pořádku.</summary>
internal static class ReadOnlyCatalogRule
{
    public static IReadOnlyList<string> Check(IEnumerable<ApiEndpoint> endpoints, IReadOnlySet<string> allowedRights)
    {
        List<string> violations = [];
        foreach (ApiEndpoint endpoint in endpoints)
        {
            string action = endpoint.Path[(endpoint.Path.LastIndexOf('/') + 1)..];
            if (!action.StartsWith("Get", StringComparison.Ordinal))
            {
                violations.Add($"{endpoint.Path}: akce nezačíná na Get, projekt smí jen číst.");
            }

            violations.AddRange(endpoint.Rights
                .Where(right => !allowedRights.Contains(right))
                .Select(right => $"{endpoint.Path}: právo {right} není ve SPEC sekce 6."));
        }

        return violations;
    }
}
