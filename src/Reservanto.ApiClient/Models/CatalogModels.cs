namespace Reservanto.ApiClient.Models;

// Modely katalogu merchanta. Názvy typů a vlastností 1:1 podle OpenAPI (components/schemas) — dohledatelnost
// proti dokumentaci API je důležitější než „hezčí“ jména. Časy (UnixTimeStamp) jsou DateTimeOffset v UTC.

/// <summary>Viditelnost objektu.</summary>
public enum ObjectState
{
    /// <summary>Soukromý — viditelný pouze pro obchodníka.</summary>
    Private,

    /// <summary>Veřejný — viditelný pro zákazníky i obchodníka.</summary>
    Public,

    /// <summary>Smazaný.</summary>
    Deleted,
}

/// <summary>Adresa.</summary>
public sealed class AddressApiModel
{
    /// <summary>Město.</summary>
    public string? City { get; init; }

    /// <summary>Stát.</summary>
    public string? Country { get; init; }

    /// <summary>Zeměpisná šířka.</summary>
    public double? Latitude { get; init; }

    /// <summary>Zeměpisná délka.</summary>
    public double? Longitude { get; init; }

    /// <summary>Název.</summary>
    public string? Name { get; init; }

    /// <summary>Ulice.</summary>
    public string? Street { get; init; }

    /// <summary>PSČ.</summary>
    public string? ZipCode { get; init; }
}

/// <summary>Zaměření.</summary>
public sealed class SegmentApiModel
{
    /// <summary>Vnitřní Id.</summary>
    public int Id { get; init; }

    /// <summary>Vlastní pojmenování zaměření obchodníkem.</summary>
    public string? CustomizedName { get; init; }

    /// <summary>Interní název.</summary>
    public string? InternalName { get; init; }

    /// <summary>Lokalizovaný název.</summary>
    public string? LocalizedName { get; init; }

    /// <summary>Typ zaměření jako text z API (např. <c>OneToOne</c>, <c>Classes</c>). Převod na typ zobrazení dělá konzument.</summary>
    public string? SegmentType { get; init; }

    /// <summary>Viditelnost.</summary>
    public ObjectState State { get; init; }
}

/// <summary>Provozovna.</summary>
public sealed class LocationApiModel
{
    /// <summary>Vnitřní Id.</summary>
    public int Id { get; init; }

    /// <summary>Adresa.</summary>
    public AddressApiModel? Address { get; init; }

    /// <summary>Vytvořeno.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Popisek.</summary>
    public string? Description { get; init; }

    /// <summary>Kontaktní e-mail.</summary>
    public string? Email { get; init; }

    /// <summary>Naposledy změněno.</summary>
    public DateTimeOffset ModifiedAt { get; init; }

    /// <summary>Název.</summary>
    public string? Name { get; init; }

    /// <summary>Telefon.</summary>
    public string? Phone { get; init; }

    /// <summary>Viditelnost.</summary>
    public ObjectState State { get; init; }

    /// <summary>Pracovní hodiny.</summary>
    public IReadOnlyList<WorkingHourApiModel>? WorkingHours { get; init; }
}

/// <summary>Pracovní hodina.</summary>
public sealed class WorkingHourApiModel
{
    /// <summary>Den v týdnu 0–6, 0 = neděle (číslo jako v API; odpovídá <see cref="System.DayOfWeek"/>).</summary>
    public int DayOfWeek { get; init; }

    /// <summary>Konec.</summary>
    public DateTimeOffset End { get; init; }

    /// <summary>Začátek.</summary>
    public DateTimeOffset Start { get; init; }
}

/// <summary>Služba.</summary>
public sealed class BookingServiceApiModel
{
    /// <summary>Vnitřní Id.</summary>
    public int Id { get; init; }

    /// <summary>Zdroje poskytující službu.</summary>
    public IReadOnlyList<int>? BookingResourceIds { get; init; }

    /// <summary>Kategorie služeb.</summary>
    public int? CategoryId { get; init; }

    /// <summary>Zaměření, ve kterém je služba poskytovaná.</summary>
    public int SegmentId { get; init; }

    /// <summary>Ceny podle cenových hladin.</summary>
    public IReadOnlyList<ItemPriceLevelApiModel>? AvailablePriceLevels { get; init; }

    /// <summary>Název kategorie.</summary>
    public string? CategoryName { get; init; }

    /// <summary>Popisek.</summary>
    public string? Description { get; init; }

    /// <summary>Trvání v minutách.</summary>
    public int Duration { get; init; }

    /// <summary>Název.</summary>
    public string? Name { get; init; }

    /// <summary>Cena včetně DPH.</summary>
    public decimal? Price { get; init; }

    /// <summary>Název zaměření.</summary>
    public string? SegmentName { get; init; }

    /// <summary>Viditelnost.</summary>
    public ObjectState State { get; init; }

    /// <summary>Sazba DPH; null u neplátce nebo služby bez ceny.</summary>
    public decimal? VatRateNullable { get; init; }
}

/// <summary>Cena pro cenovou hladinu.</summary>
public sealed class ItemPriceLevelApiModel
{
    /// <summary>Vnitřní Id cenové hladiny.</summary>
    public int Id { get; init; }

    /// <summary>Název cenové hladiny.</summary>
    public string? Name { get; init; }

    /// <summary>Cena po použití hladiny.</summary>
    public decimal Price { get; init; }
}

/// <summary>Zdroj.</summary>
public sealed class BookingResourceApiModel
{
    /// <summary>Vnitřní Id.</summary>
    public int Id { get; init; }

    /// <summary>Služby, které zdroj poskytuje.</summary>
    public IReadOnlyList<int>? BookingServiceIds { get; init; }

    /// <summary>Provozovna zdroje.</summary>
    public int LocationId { get; init; }

    /// <summary>Zaměření, ve kterých zdroj vystupuje.</summary>
    public IReadOnlyList<int>? SegmentIds { get; init; }

    /// <summary>Štítky.</summary>
    public IReadOnlyList<int>? TagIds { get; init; }

    /// <summary>Dostupný od.</summary>
    public DateTimeOffset? AvailableFrom { get; init; }

    /// <summary>Dostupný do.</summary>
    public DateTimeOffset? AvailableTo { get; init; }

    /// <summary>Kolik rezervací může mít v jeden okamžik.</summary>
    public int Capacity { get; init; }

    /// <summary>Popisek.</summary>
    public string? Description { get; init; }

    /// <summary>Kontaktní e-mail.</summary>
    public string? Email { get; init; }

    /// <summary>Má vlastní fotku.</summary>
    public bool HasCustomImage { get; init; }

    /// <summary>Odkaz na fotku.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>Nejmenší dílek kalendáře v minutách.</summary>
    public int MinTimeUnit { get; init; }

    /// <summary>Název.</summary>
    public string? Name { get; init; }

    /// <summary>Kontaktní telefon.</summary>
    public string? Phone { get; init; }

    /// <summary>Viditelnost.</summary>
    public ObjectState State { get; init; }
}

/// <summary>Vypsaný termín (událost) zaměření Classes.</summary>
public sealed class ExtendedAppointmentClassApiModel
{
    /// <summary>Vnitřní Id události.</summary>
    public int Id { get; init; }

    /// <summary>Zdroj.</summary>
    public int BookingResourceId { get; init; }

    /// <summary>Služba.</summary>
    public int BookingServiceId { get; init; }

    /// <summary>Kalendář.</summary>
    public int CalendarId { get; init; }

    /// <summary>Kurz, pokud je událost jeho součástí.</summary>
    public int? CourseId { get; init; }

    /// <summary>Název zdroje.</summary>
    public string? BookingResourceName { get; init; }

    /// <summary>Název služby.</summary>
    public string? BookingServiceName { get; init; }

    /// <summary>Odkaz na rezervaci události v Reservantu.</summary>
    public string? BookingUrl { get; init; }

    /// <summary>Název kalendáře.</summary>
    public string? CalendarName { get; init; }

    /// <summary>Kapacita.</summary>
    public int Capacity { get; init; }

    /// <summary>Název kurzu.</summary>
    public string? CourseName { get; init; }

    /// <summary>Konec.</summary>
    public DateTimeOffset EndDate { get; init; }

    /// <summary>Obsazenost ve formátu obchodníka (např. „2/10“ nebo „volno“).</summary>
    public string? FormattedAvailability { get; init; }

    /// <summary>Událost je dostupná (není obsazená).</summary>
    public bool IsAvailable { get; init; }

    /// <summary>Obsazená kapacita.</summary>
    public int OccupiedCapacity { get; init; }

    /// <summary>Začátek.</summary>
    public DateTimeOffset StartDate { get; init; }
}
