namespace AvailabilityView.Application.Connection;

/// <summary>Připojení účtu merchanta v průvodci (<c>docs/SPEC.md</c> sekce 6).</summary>
public interface IConnectionSetup
{
    /// <summary>ClientId z konfigurace pro předvyplnění pole, jinak <c>null</c>.</summary>
    string? PrefilledClientId { get; }

    /// <summary>Práva, o která se žádá vždy, kódy z API.</summary>
    IReadOnlyList<string> BaseRights { get; }

    /// <summary>Práva navíc, když správce chce ukazovat obsazenost z rezervací.</summary>
    IReadOnlyList<string> OccupancyRights { get; }

    /// <summary>Adresa souhlasu, na kterou se správce přesměruje.</summary>
    /// <param name="clientId">Id aplikace přidělené Reservantem.</param>
    /// <param name="redirectUrl">Návratová adresa této instance.</param>
    /// <param name="occupancy">Správce chce ukazovat obsazenost z rezervací.</param>
    Uri BuildConsentUrl(string clientId, Uri redirectUrl, bool occupancy);

    /// <summary>Přečte výsledek souhlasu z parametrů návratové adresy, které může podstrčit kdokoli.</summary>
    /// <param name="accepted">Hodnota parametru <c>accepted</c>.</param>
    /// <param name="token">Hodnota parametru <c>token</c>.</param>
    ConsentResult ReadConsent(string? accepted, string? token);
}

/// <summary>Jak dopadl souhlas v Reservantu.</summary>
public enum ConsentOutcome
{
    /// <summary>Správce přístup povolil a token má očekávaný tvar.</summary>
    Accepted,

    /// <summary>Správce přístup nepovolil.</summary>
    Denied,

    /// <summary>Přístup je povolený, ale token chybí nebo má nečekaný tvar.</summary>
    Invalid,
}

/// <summary>Výsledek souhlasu. Záměrně ne record: vygenerovaný <c>ToString()</c> by vypsal token.</summary>
/// <param name="outcome">Jak souhlas dopadl.</param>
/// <param name="token">Token, jen u <see cref="ConsentOutcome.Accepted"/>.</param>
public sealed class ConsentResult(ConsentOutcome outcome, string? token = null)
{
    /// <summary>Jak souhlas dopadl.</summary>
    public ConsentOutcome Outcome { get; } = outcome;

    /// <summary>Token, jen u <see cref="ConsentOutcome.Accepted"/>.</summary>
    public string? Token { get; } = token;
}
