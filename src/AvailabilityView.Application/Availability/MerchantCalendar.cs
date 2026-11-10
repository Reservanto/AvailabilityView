namespace AvailabilityView.Application.Availability;

/// <summary>Místní dny merchanta. Den nemusí mít 24 hodin, proto se období počítá po dnech, ne přičítáním hodin.</summary>
/// <param name="timeZone">Časové pásmo merchanta.</param>
internal sealed class MerchantCalendar(TimeZoneInfo timeZone)
{
    public DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(ToLocal(now).DateTime);

    public DateTimeOffset ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, timeZone);

    /// <summary>Okamžik, kdy místní den začíná. V některých pásmech půlnoc o přechodu času neexistuje nebo je dvakrát.</summary>
    public DateTimeOffset StartOf(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        // Dvojitý čas: den začíná prvním výskytem, to je ten s větším posunem.
        TimeSpan offset = timeZone.IsAmbiguousTime(local) ? timeZone.GetAmbiguousTimeOffsets(local).Max() : timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
