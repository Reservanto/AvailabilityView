using System.Globalization;

namespace AvailabilityView.Web.Availability;

/// <summary>Čas od–do v jazyce stránky, stejně ve všech zobrazeních.</summary>
internal static class TimeText
{
    private const int MinutesPerDay = 24 * 60;

    /// <summary>
    /// Úsek, který končí půlnocí dalšího dne, končí ve 24:00. Volno se dělí po dnech (rozhodnutí uživatele 2026-11-09)
    /// a „0:00–0:00“ by vypadalo jako prázdný úsek.
    /// </summary>
    public static string Range(DateTimeOffset start, DateTimeOffset end, CultureInfo culture) =>
        $"{start.ToString("t", culture)}–{(end.TimeOfDay == TimeSpan.Zero && end.Date > start.Date ? "24:00" : end.ToString("t", culture))}";

    /// <summary>Část dne v minutách od půlnoci podle místních hodin, konec dne je 24:00.</summary>
    public static string Range(int fromMinute, int toMinute, CultureInfo culture) =>
        $"{Clock(fromMinute, culture)}–{(toMinute == MinutesPerDay ? "24:00" : Clock(toMinute, culture))}";

    private static string Clock(int minute, CultureInfo culture) => new TimeOnly(minute / 60, minute % 60).ToString("t", culture);
}
