using AvailabilityView.Application.Availability;
using Microsoft.Extensions.Options;

namespace AvailabilityView.Infrastructure.Availability;

/// <summary>Konfigurace zobrazení, sekce <c>AvailabilityView</c> (<c>docs/SPEC.md</c> sekce 5).</summary>
public sealed class AvailabilityViewOptions
{
    /// <summary>Název konfigurační sekce.</summary>
    public const string SectionName = "AvailabilityView";

    /// <summary>Časové pásmo merchanta, API ho nevrací.</summary>
    public string TimeZone { get; set; } = "Europe/Prague";

    /// <summary>Nejdelší období, které jde zobrazit.</summary>
    public int MaxDays { get; set; } = 60;

    /// <summary>Jazyk stránky, když ho adresa neurčí.</summary>
    public string DefaultLanguage { get; set; } = "cs";
}

/// <summary>Konfigurace, která nemůže fungovat, nedovolí start aplikace.</summary>
internal sealed class AvailabilityViewOptionsValidator : IValidateOptions<AvailabilityViewOptions>
{
    public ValidateOptionsResult Validate(string? name, AvailabilityViewOptions options)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(options.TimeZone, out _))
        {
            return ValidateOptionsResult.Fail($"{AvailabilityViewOptions.SectionName}:TimeZone není známé časové pásmo.");
        }

        if (!AvailabilitySettings.Languages.Contains(options.DefaultLanguage, StringComparer.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                $"{AvailabilityViewOptions.SectionName}:DefaultLanguage musí být jedna z hodnot {string.Join(", ", AvailabilitySettings.Languages)}.");
        }

        return options.MaxDays >= 1
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{AvailabilityViewOptions.SectionName}:MaxDays musí být aspoň 1.");
    }
}
