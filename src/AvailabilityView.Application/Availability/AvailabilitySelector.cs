namespace AvailabilityView.Application.Availability;

/// <summary>
/// Doplní chybějící volby podle <c>docs/SPEC.md</c> sekce 4 a nabídne možnosti filtrů. Obojí stojí na stejném
/// <see cref="Resolve"/>, takže nabídka nemůže ukázat možnost, kterou by doplnění odmítlo, ani naopak. Pracuje jen s veřejným katalogem, takže cizí Id se k API vůbec nedostane. Za možnost se bere jen to, co
/// k zadaným hodnotám patří a má aspoň jeden veřejný zdroj.
/// </summary>
internal sealed class AvailabilitySelector(PublicCatalog catalog, IReadOnlyDictionary<string, ISegmentAvailabilitySource> sources)
{
    private static readonly AvailabilityFilter[] _order = Enum.GetValues<AvailabilityFilter>();

    public (AvailabilityOutcome Outcome, AvailabilitySelection? Selection, ISegmentAvailabilitySource? Source) Select(AvailabilityQuery query)
    {
        var given = Choice.Of(query);
        if (_order.Any(filter => given[filter] is { } id && !Exists(filter, id)))
        {
            return (AvailabilityOutcome.ViewNotAvailable, null, null);
        }

        if ((given.Segment is { } segmentId && !sources.ContainsKey(catalog.Segments.First(item => item.Id == segmentId).Type))
            || !catalog.Segments.Any(item => sources.ContainsKey(item.Type)))
        {
            return (AvailabilityOutcome.UnsupportedSegment, null, null);
        }

        // Pevné hodnoty musí k sobě patřit. Hodnoty filtrů návštěvníka se přidávají shora a ta, která k vyšším
        // nepatří, se zahodí: návštěvník bez JavaScriptu mohl změnit zaměření i provozovnu najednou.
        Choice kept = given.Fixed(query.Pickers);
        if (Resolve(kept) is null)
        {
            return (AvailabilityOutcome.ViewNotAvailable, null, null);
        }

        foreach (AvailabilityFilter filter in _order.Where(query.Pickers.Contains))
        {
            Choice candidate = kept.With(filter, given[filter]);
            if (given[filter] is not null && Resolve(candidate) is not null)
            {
                kept = candidate;
            }
        }

        (AvailabilitySelection selection, ISegmentAvailabilitySource source) = Resolve(kept)!.Value;
        return (AvailabilityOutcome.Available, selection, source);
    }

    /// <summary>
    /// Možnosti filtrů návštěvníka. Vyšší filtry se berou z výsledné volby, nižší jen pevné z adresy, protože hodnotu
    /// nižšího filtru změna vyššího zahodí.
    /// </summary>
    public IReadOnlyList<FilterChoices> Options(AvailabilityQuery query, AvailabilitySelection selection)
    {
        var chosen = Choice.Of(selection);
        Choice fixedValues = Choice.Of(query).Fixed(query.Pickers);
        List<FilterChoices> filters = [];
        foreach (AvailabilityFilter filter in _order.Where(query.Pickers.Contains))
        {
            Choice basis = chosen;
            foreach (AvailabilityFilter lower in _order.Where(item => item > filter))
            {
                basis = basis.With(lower, fixedValues[lower]);
            }

            FilterOption[] options = [.. Candidates(filter).Where(option => Resolve(basis.With(filter, option.Id)) is not null)];

            bool allowsAll = filter is AvailabilityFilter.Service or AvailabilityFilter.Resource
                && Resolve(basis.With(filter, null)) is not null;
            filters.Add(new FilterChoices(filter, chosen[filter], allowsAll, options));
        }

        return filters;
    }

    private bool Exists(AvailabilityFilter filter, int id) => Candidates(filter).Any(option => option.Id == id);

    // Všechny veřejné objekty. Nepodporované zaměření a cokoli, co k volbám nepatří, vyřadí až Resolve.
    private IEnumerable<FilterOption> Candidates(AvailabilityFilter filter) => filter switch
    {
        AvailabilityFilter.Segment => catalog.Segments.Select(item => new FilterOption(item.Id, item.Name)),
        AvailabilityFilter.Location => catalog.Locations.Select(item => new FilterOption(item.Id, item.Name)),
        AvailabilityFilter.Service => catalog.Services.Select(item => new FilterOption(item.Id, item.Name)),
        AvailabilityFilter.Resource => catalog.Resources.Select(item => new FilterOption(item.Id, item.Name)),
        _ => throw new InvalidOperationException("Neznámý filtr."),
    };

    private (AvailabilitySelection Selection, ISegmentAvailabilitySource Source)? Resolve(Choice choice)
    {
        PublicSegment? segment = Find(catalog.Segments, choice.Segment, item => item.Id);
        PublicLocation? location = Find(catalog.Locations, choice.Location, item => item.Id);
        PublicService? service = Find(catalog.Services, choice.Service, item => item.Id);
        PublicResource? resource = Find(catalog.Resources, choice.Resource, item => item.Id);
        if (IsMissing(choice.Segment, segment) || IsMissing(choice.Location, location)
            || IsMissing(choice.Service, service) || IsMissing(choice.Resource, resource))
        {
            return null;
        }

        PublicSegment[] candidates = segment is not null
            ? [segment]
            : [.. catalog.Segments.Where(item => sources.ContainsKey(item.Type))];
        foreach (PublicSegment candidate in candidates)
        {
            if (sources.TryGetValue(candidate.Type, out ISegmentAvailabilitySource? source)
                && Complete(candidate, location, service, resource) is { } selection)
            {
                return (selection, source);
            }
        }

        return null;
    }

    private static T? Find<T>(IReadOnlyList<T> items, int? id, Func<T, int> idOf)
        where T : class =>
        id is null ? null : items.FirstOrDefault(item => idOf(item) == id);

    private static bool IsMissing<T>(int? id, T? item)
        where T : class =>
        id is not null && item is null;

    private AvailabilitySelection? Complete(
        PublicSegment segment,
        PublicLocation? location,
        PublicService? service,
        PublicResource? resource)
    {
        if (service is not null && service.SegmentId != segment.Id)
        {
            return null;
        }

        PublicResource[] resources = [.. catalog.Resources.Where(item =>
            item.SegmentIds.Contains(segment.Id)
            && (resource is null || item.Id == resource.Id)
            && (location is null || item.LocationId == location.Id)
            && (service is null || service.ResourceIds.Contains(item.Id)))];

        foreach (PublicLocation candidate in catalog.Locations)
        {
            // Provozovna bez veřejné služby zaměření by nikdy nic neukázala. Zvolenou službu zdroje výše poskytují.
            PublicResource[] here = [.. resources.Where(item => item.LocationId == candidate.Id)];
            if (here.Length == 0
                || !catalog.Services.Any(item => item.SegmentId == segment.Id && here.Any(own => item.ResourceIds.Contains(own.Id))))
            {
                continue;
            }

            // Chybějící služba se nedoplňuje, ani u individuálních typů (SPEC sekce 4).
            return new AvailabilitySelection(segment, candidate, service, resource);
        }

        return null;
    }

    /// <summary>
    /// Veřejné služby pohledu: zvolená služba, nebo služby zaměření, které poskytuje některý veřejný zdroj pohledu.
    /// Volno individuálních typů se z nich sjednotí (SPEC sekce 3).
    /// </summary>
    public IReadOnlyList<PublicService> ServicesOf(AvailabilitySelection selection)
    {
        if (selection.Service is { } service)
        {
            return [service];
        }

        int[] resources = [.. ResourcesOf(selection).Select(item => item.Id)];
        return [.. catalog.Services.Where(item => item.SegmentId == selection.Segment.Id && item.ResourceIds.Any(resources.Contains))];
    }

    /// <summary>
    /// Veřejné zdroje pohledu: zvolený zdroj, nebo všechny zdroje zaměření v provozovně. Se zvolenou službou jen ty,
    /// které ji poskytují (SPEC sekce 4).
    /// </summary>
    public IReadOnlyList<PublicResource> ResourcesOf(AvailabilitySelection selection) =>
        [.. catalog.Resources.Where(item => item.LocationId == selection.Location.Id
            && item.SegmentIds.Contains(selection.Segment.Id)
            && (selection.Resource is null || item.Id == selection.Resource.Id)
            && (selection.Service is null || selection.Service.ResourceIds.Contains(item.Id)))];

    /// <summary>Hodnoty voleb podle filtrů, <c>null</c> = bez volby.</summary>
    private sealed record Choice(int? Segment, int? Location, int? Service, int? Resource)
    {
        public int? this[AvailabilityFilter filter] => filter switch
        {
            AvailabilityFilter.Segment => Segment,
            AvailabilityFilter.Location => Location,
            AvailabilityFilter.Service => Service,
            AvailabilityFilter.Resource => Resource,
            _ => throw new InvalidOperationException("Neznámý filtr."),
        };

        public static Choice Of(AvailabilityQuery query) => new(query.SegmentId, query.LocationId, query.ServiceId, query.ResourceId);

        public static Choice Of(AvailabilitySelection selection) =>
            new(selection.Segment.Id, selection.Location.Id, selection.Service?.Id, selection.Resource?.Id);

        public Choice With(AvailabilityFilter filter, int? id) => filter switch
        {
            AvailabilityFilter.Segment => this with { Segment = id },
            AvailabilityFilter.Location => this with { Location = id },
            AvailabilityFilter.Service => this with { Service = id },
            AvailabilityFilter.Resource => this with { Resource = id },
            _ => throw new InvalidOperationException("Neznámý filtr."),
        };

        /// <summary>Jen pevné hodnoty, filtry návštěvníka bez volby.</summary>
        public Choice Fixed(IReadOnlySet<AvailabilityFilter> pickers) =>
            _order.Where(pickers.Contains).Aggregate(this, (choice, filter) => choice.With(filter, null));
    }
}
