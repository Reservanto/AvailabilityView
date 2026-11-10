using AvailabilityView.Application.Availability;

namespace AvailabilityView.Web.Availability;

/// <summary>
/// Části tříd pro stavy dne a úseku osy (<c>av-month__day--free</c>, <c>av-legend__item--busy</c>). Třídy jsou veřejné
/// rozhraní (<c>docs/vzhled.md</c>) a merchanti na nich staví, proto jsou tu vypsané a neodvozují se z názvů výčtů.
/// Přejmenování stavu v kódu je tak nezmění a nový stav bez třídy shodí test.
/// </summary>
public static class StateClass
{
    /// <summary>Část třídy stavu dne měsíce.</summary>
    public static string Of(DayState state) => state switch
    {
        DayState.Free => "free",
        DayState.Full => "full",
        DayState.Closed => "closed",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Stav dne nemá třídu."),
    };

    /// <summary>Část třídy stavu úseku hodinové osy.</summary>
    public static string Of(TimeBandState state) => state switch
    {
        TimeBandState.Free => "free",
        TimeBandState.Busy => "busy",
        TimeBandState.Unavailable => "unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Stav úseku nemá třídu."),
    };
}
