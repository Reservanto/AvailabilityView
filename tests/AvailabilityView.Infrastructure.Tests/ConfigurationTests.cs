using AvailabilityView.Application.Availability;
using AvailabilityView.Infrastructure.Availability;
using AvailabilityView.Infrastructure.Tests.Support;
using Microsoft.Extensions.Options;

namespace AvailabilityView.Infrastructure.Tests;

/// <summary>Konfigurace zobrazení (<c>docs/SPEC.md</c> sekce 5).</summary>
public sealed class ConfigurationTests
{
    [Fact]
    public void Defaults_MatchSpec()
    {
        using var harness = new Harness();

        AvailabilitySettings settings = harness.Get<AvailabilitySettings>();

        Assert.Equal(60, settings.MaxDays);
        Assert.Equal(TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague"), settings.TimeZone);
        Assert.Equal("cs", settings.DefaultLanguage);
    }

    [Fact]
    public void DefaultLanguageFromConfiguration_IsUsed()
    {
        using var harness = new Harness(settings => settings["AvailabilityView:DefaultLanguage"] = "en");

        Assert.Equal("en", harness.Get<AvailabilitySettings>().DefaultLanguage);
    }

    [Fact]
    public void TimeZoneFromConfiguration_IsUsed()
    {
        using var harness = new Harness(settings => settings["AvailabilityView:TimeZone"] = "Europe/London");

        Assert.Equal(TimeZoneInfo.FindSystemTimeZoneById("Europe/London"), harness.Get<AvailabilitySettings>().TimeZone);
    }

    [Theory]
    [InlineData("AvailabilityView:TimeZone", "Mars/Olympus")]
    [InlineData("AvailabilityView:MaxDays", "0")]
    [InlineData("AvailabilityView:DefaultLanguage", "de")]
    [InlineData("AvailabilityView:DefaultLanguage", "EN")]
    [InlineData("AvailabilityView:DefaultLanguage", "")]
    public void InvalidValue_FailsValidation(string key, string value)
    {
        using var harness = new Harness(settings => settings[key] = value);

        Assert.Throws<OptionsValidationException>(() => harness.Get<IOptions<AvailabilityViewOptions>>().Value);
    }
}
