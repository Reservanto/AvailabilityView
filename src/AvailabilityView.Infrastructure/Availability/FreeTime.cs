using AvailabilityView.Application.Availability;

namespace AvailabilityView.Infrastructure.Availability;

/// <summary>Sjednocení volného času zdrojů.</summary>
internal static class FreeTime
{
    /// <summary>
    /// Úseky stejného zdroje, které se překrývají nebo na sebe navazují, spojí do jednoho. Výsledek je podle začátku
    /// a zdroje. Úseky nesou jen zdroj a čas.
    /// </summary>
    /// <param name="slots">Volné úseky, v libovolném pořadí.</param>
    public static IReadOnlyList<SourceSlot> Merge(IEnumerable<SourceSlot> slots)
    {
        List<SourceSlot> merged = [];
        foreach (IGrouping<int, SourceSlot> resource in slots.GroupBy(slot => slot.ResourceId))
        {
            SourceSlot? current = null;
            foreach (SourceSlot slot in resource.OrderBy(slot => slot.Start))
            {
                if (current is not null && slot.Start <= current.End)
                {
                    current = current with { End = slot.End > current.End ? slot.End : current.End };
                    continue;
                }

                if (current is not null)
                {
                    merged.Add(current);
                }

                current = new SourceSlot(slot.Start, slot.End, null, slot.ResourceId, null, false);
            }

            if (current is not null)
            {
                merged.Add(current);
            }
        }

        return [.. merged.OrderBy(slot => slot.Start).ThenBy(slot => slot.ResourceId)];
    }
}
