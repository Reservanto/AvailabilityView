namespace Reservanto.ApiClient.Models;

// Rezervace nesou údaje zákazníků. Model má jen pole, která aplikace potřebuje k obsazeným časům. Ostatní pole
// z OpenAPI (jméno a poznámka zákazníka, cena, vlastní pole…) vyjmenovává DiscardedMembers: JSON čtečka jejich
// hodnotu přeskočí, takže se do paměti aplikace nedostanou.

/// <summary>Stav rezervace.</summary>
public enum AppointmentStatus
{
    /// <summary>Rezervaci je nutno potvrdit.</summary>
    ConfirmationRequested,

    /// <summary>Rezervace byla zamítnuta.</summary>
    Rejected,

    /// <summary>Rezervace je ve stavu náhradníka.</summary>
    AlternateSubscribed,

    /// <summary>Náhradník, po uvolnění místa bude automaticky povýšen.</summary>
    AlternateAutoPromotion,

    /// <summary>Rezervace je stornována.</summary>
    Canceled,

    /// <summary>Rezervace je potvrzena.</summary>
    Confirmed,
}

/// <summary>Rezervace bez údajů zákazníka.</summary>
public sealed class EventApiModel
{
    /// <summary>Termín.</summary>
    public int AppointmentId { get; init; }

    /// <summary>Služba.</summary>
    public int? BookingServiceId { get; init; }

    /// <summary>Kalendář.</summary>
    public int CalendarId { get; init; }

    /// <summary>Kurz.</summary>
    public int? CourseId { get; init; }

    /// <summary>Interní zdroj.</summary>
    public int? InternalSourceId { get; init; }

    /// <summary>Provozovna.</summary>
    public int LocationId { get; init; }

    /// <summary>Zdroj.</summary>
    public int SourceId { get; init; }

    /// <summary>Počet osob.</summary>
    public int Count { get; init; }

    /// <summary>Konec.</summary>
    public DateTimeOffset EndDate { get; init; }

    /// <summary>Začátek.</summary>
    public DateTimeOffset StartDate { get; init; }

    /// <summary>Stav.</summary>
    public AppointmentStatus Status { get; init; }
}
