namespace AvailabilityView.Application.Availability;

/// <summary>
/// Úseky hodinové osy z volna, pracovní doby a obsazenosti zdrojů (<c>docs/SPEC.md</c> sekce 3). V každém okamžiku
/// platí první pravidlo, které sedí: volno, když má volno aspoň jeden zdroj pohledu. Obsazeno, když má aspoň jeden
/// zdroj v pracovní době rezervaci. Nedostupné, když aspoň jeden zdroj pracuje. Jinak se nekreslí nic. Volno má
/// přednost, protože zdroj s kapacitou pro víc lidí může mít volno i během rezervace.
/// </summary>
internal static class TimelineBands
{
    /// <param name="free">Volné úseky zdrojů pohledu, už po kontrole veřejnosti.</param>
    /// <param name="timeline">Pracovní doba a obsazenost ze čtení, i cizích zdrojů.</param>
    /// <param name="resources">Veřejné zdroje pohledu. Pracovní doba a rezervace jiných zdrojů se nepočítají.</param>
    /// <param name="from">Začátek nabízené části týdne.</param>
    /// <param name="to">Konec nabízené části týdne.</param>
    public static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End, TimeBandState State)> Build(
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> free,
        SourceTimeline timeline,
        IReadOnlySet<int> resources,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        ResourceInterval[] working = [.. timeline.Working.Where(item => resources.Contains(item.ResourceId))];
        ResourceInterval[] busy = [.. timeline.Busy.Where(item => resources.Contains(item.ResourceId))];

        DateTimeOffset[] points = [.. free.SelectMany(item => new[] { item.Start, item.End })
            .Concat(working.SelectMany(item => new[] { item.Start, item.End }))
            .Concat(busy.SelectMany(item => new[] { item.Start, item.End }))
            .Append(from)
            .Append(to)
            .Where(point => point >= from && point <= to)
            .Distinct()
            .Order()];

        List<(DateTimeOffset Start, DateTimeOffset End, TimeBandState State)> bands = [];
        foreach ((DateTimeOffset start, DateTimeOffset end) in points.Zip(points.Skip(1)))
        {
            if (StateAt(start, free, working, busy) is not { } state)
            {
                continue;
            }

            if (bands.Count > 0 && bands[^1].End == start && bands[^1].State == state)
            {
                bands[^1] = bands[^1] with { End = end };
            }
            else
            {
                bands.Add((start, end, state));
            }
        }

        return bands;
    }

    // Mezi dvěma sousedními hranicemi se stav nemění, stačí ho určit na začátku.
    private static TimeBandState? StateAt(
        DateTimeOffset instant,
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> free,
        ResourceInterval[] working,
        ResourceInterval[] busy)
    {
        if (free.Any(item => Covers(item.Start, item.End, instant)))
        {
            return TimeBandState.Free;
        }

        int[] atWork = [.. working.Where(item => Covers(item.Start, item.End, instant)).Select(item => item.ResourceId).Distinct()];
        if (atWork.Length == 0)
        {
            return null;
        }

        return busy.Any(item => atWork.Contains(item.ResourceId) && Covers(item.Start, item.End, instant))
            ? TimeBandState.Busy
            : TimeBandState.Unavailable;
    }

    private static bool Covers(DateTimeOffset start, DateTimeOffset end, DateTimeOffset instant) => start <= instant && instant < end;
}
