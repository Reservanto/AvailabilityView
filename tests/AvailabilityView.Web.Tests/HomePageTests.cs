using System.Text.Json;
using AvailabilityView.Tests.Shared;

namespace AvailabilityView.Web.Tests;

/// <summary>Vývojová adresa aplikace (<c>docs/SPEC.md</c> sekce 6). Hlavní stránku testuje <see cref="VisitorPageTests"/>.</summary>
public sealed class HomePageTests
{
    [Fact]
    public void DevelopmentAddress_MatchesRegisteredRedirectUrl()
    {
        // Testovací ClientId je registrovaný na https://localhost:5001/Auth/Callback.
        string file = Path.Combine(RepositoryRoot.Path, "src", "AvailabilityView.Web", "Properties", "launchSettings.json");
        using var settings = JsonDocument.Parse(File.ReadAllText(file));

        string url = settings.RootElement.GetProperty("profiles").GetProperty("AvailabilityView.Web").GetProperty("applicationUrl").GetString()!;

        Assert.Equal("https://localhost:5001", url);
    }
}
