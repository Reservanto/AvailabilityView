namespace AvailabilityView.Web.Availability;

/// <summary>
/// Označuje stránku návštěvníka: mluví jazykem z parametru <c>lang</c>, jde vložit do iframe z povolených domén
/// a načte vlastní CSS merchanta. Ostatní stránky, třeba průvodce a generátor, nic z toho nemají. Jedno označení pro
/// všechno, ať se pravidla nemohou rozejít.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class VisitorPageAttribute : Attribute
{
    /// <summary>Požadavek míří na stránku návštěvníka. Platí až po výběru stránky (<c>UseRouting</c>).</summary>
    /// <param name="context">Požadavek.</param>
    public static bool Applies(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetEndpoint()?.Metadata.GetMetadata<VisitorPageAttribute>() is not null;
    }
}
