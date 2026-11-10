using System.Diagnostics.CodeAnalysis;

namespace Reservanto.ApiClient;

/// <summary>Konfigurace klienta, sekce <c>Reservanto</c> (<c>docs/SPEC.md</c> sekce 5).</summary>
public sealed class ReservantoApiOptions
{
    /// <summary>Název konfigurační sekce.</summary>
    public const string SectionName = "Reservanto";

    /// <summary>
    /// Adresa API. Text, ne <see cref="Uri"/>: konfigurace se načítá i za běhu a adresu, kterou nejde přečíst, by
    /// převod hodil jako výjimku na každé stránce. Používá se přes <see cref="ApiAddress"/>.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.reservanto.cz/";

    /// <summary>Nepovinné, jen předvyplní pole v průvodci připojením. Za běhu ho aplikace nepotřebuje.</summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Přístup k účtu merchanta. <b>Tajný údaj:</b> jen user-secrets (vývoj) nebo <c>appsettings.Production.json</c>
    /// na serveru, nikdy v repozitáři. Prázdná hodnota = instance není připojená, aplikace přesto nastartuje.
    /// </summary>
    public string? LongTimeToken { get; set; }

    // Hodnoty níže nejsou součást konfigurace pro merchanty (SPEC sekce 5), proto je z konfigurace nastavit nejde.

    /// <summary>Timeout jednoho HTTP požadavku.</summary>
    public TimeSpan Timeout { get; internal set; } = TimeSpan.FromSeconds(10);

    /// <summary>Životnost krátkodobého tokenu podle API (30 minut).</summary>
    public TimeSpan ShortTimeTokenLifetime { get; internal set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Přísné čtení odpovědí: neznámé pole = chyba. Jen pro testy, v provozu vypnuté: nové pole v API nesmí shodit
    /// stránku. Záměrně internal, z konfigurace aplikace se nastavit nedá.
    /// </summary>
    internal bool StrictDeserialization { get; set; }

    /// <summary>Ochrana API při výpadku.</summary>
    public DegradationOptions Degradation { get; } = new();

    /// <summary>Adresa API. Jen absolutní https: LongTimeToken nesmí odejít nešifrovaně.</summary>
    /// <exception cref="InvalidOperationException">Adresa není absolutní https adresa.</exception>
    public Uri ApiAddress() =>
        TryGetApiAddress(BaseUrl, out Uri? address)
            ? address
            : throw new InvalidOperationException($"{SectionName}:BaseUrl musí být absolutní https adresa.");

    internal static bool TryGetApiAddress(string? value, [NotNullWhen(true)] out Uri? address) =>
        Uri.TryCreate(value, UriKind.Absolute, out address) && address.Scheme == Uri.UriSchemeHttps;
}

/// <summary>Prahy stavu „API degraded“.</summary>
public sealed class DegradationOptions
{
    /// <summary>Počet technických selhání v okně, který přepne do degradovaného stavu.</summary>
    public int FailureThreshold { get; internal set; } = 5;

    /// <summary>Okno, ve kterém se selhání sčítají.</summary>
    public TimeSpan FailureWindow { get; internal set; } = TimeSpan.FromSeconds(30);

    /// <summary>Jak dlouho degradovaný stav trvá.</summary>
    public TimeSpan DegradedDuration { get; internal set; } = TimeSpan.FromSeconds(30);
}
