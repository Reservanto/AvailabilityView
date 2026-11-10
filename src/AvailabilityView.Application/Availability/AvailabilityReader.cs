using AvailabilityView.Application.Connection;

namespace AvailabilityView.Application.Availability;

/// <inheritdoc />
public sealed class AvailabilityReader(
    IInstanceConnection connection,
    IPublicCatalog catalog,
    IEnumerable<ISegmentAvailabilitySource> sources,
    AvailabilitySettings settings,
    TimeProvider timeProvider) : IAvailabilityReader
{
    private readonly Dictionary<string, ISegmentAvailabilitySource> _sources = sources
        .SelectMany(source => source.SegmentTypes.Select(type => (type, source)))
        .ToDictionary(pair => pair.type, pair => pair.source, StringComparer.Ordinal);

    private readonly MerchantCalendar _calendar = new(settings.TimeZone);

    /// <inheritdoc />
    public async Task<AvailabilityOptions> OptionsAsync(AvailabilityQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        try
        {
            Selected selected = await SelectAsync(query, ct);
            return selected.Selection is null
                ? new AvailabilityOptions(selected.Outcome, null, [])
                : new AvailabilityOptions(AvailabilityOutcome.Available, selected.Selection, selected.Selector!.Options(query, selected.Selection));
        }
        catch (ReservationSystemException exception)
        {
            return new AvailabilityOptions(AvailabilityOutcome.SourceUnavailable, null, [], exception.Reason);
        }
    }

    /// <inheritdoc />
    public async Task<AvailabilityResult> ReadAsync(AvailabilityQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        try
        {
            Selected selected = await SelectAsync(query, ct);
            if (selected is not { Selection: { } selection, Source: { } source, Selector: { } selector })
            {
                return AvailabilityResult.Without(selected.Outcome);
            }

            // Půlnoc během čtení: období, „dnes“ a „zítra“ by patřily ke včerejšku. Druhé čtení už bere data z cache.
            return await ReadOnceAsync(query, selected.Catalog!, selection, source, selector, lastAttempt: false, ct)
                ?? (await ReadOnceAsync(query, selected.Catalog!, selection, source, selector, lastAttempt: true, ct))!;
        }
        catch (ReservationSystemException exception)
        {
            return new AvailabilityResult(AvailabilityOutcome.SourceUnavailable, null, [], exception.Reason);
        }
    }

    // Null, když mezitím začal další den a nejde o poslední pokus.
    private async Task<AvailabilityResult?> ReadOnceAsync(
        AvailabilityQuery query,
        PublicCatalog publicCatalog,
        AvailabilitySelection selection,
        ISegmentAvailabilitySource source,
        AvailabilitySelector selector,
        bool lastAttempt,
        CancellationToken ct)
    {
        // Vždy celé nejdelší období: počet dní a týden volí návštěvník a cache tak nemá záznam pro každou jeho volbu.
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateOnly today = _calendar.Today(now);
        var interval = new AvailabilityInterval(_calendar.StartOf(today), _calendar.StartOf(today.AddDays(settings.MaxDays)));
        AvailabilityPeriod period = Period(today, query);
        IReadOnlyList<PublicResource> resources = selector.ResourcesOf(selection);
        var request = new SourceRequest(selection, selector.ServicesOf(selection), resources, interval, now);

        // Pracovní doba a rezervace jen pro hodinovou osu a jen za zobrazený týden.
        Task<SourceTimeline?> timeline = query.Timeline && query.Range is { Unit: ShownRange.Week }
            ? source.ReadTimelineAsync(request, new AvailabilityInterval(_calendar.StartOf(period.ShownStart), _calendar.StartOf(period.ShownEnd)), ct)
            : Task.FromResult<SourceTimeline?>(null);

        // Měsíc potřebuje pracovní dobu, aby odlišil plný den od zavřeného. Rezervace ne.
        DateOnly firstOffered = Max(period.ShownStart, period.Today);
        DateOnly afterOffered = Min(period.ShownEnd, period.OfferedEnd);
        Task<IReadOnlyList<ResourceInterval>?> working = query.Range is { Unit: ShownRange.Month } && firstOffered < afterOffered
            ? source.ReadWorkingHoursAsync(request, new AvailabilityInterval(_calendar.StartOf(firstOffered), _calendar.StartOf(afterOffered)), ct)
            : Task.FromResult<IReadOnlyList<ResourceInterval>?>(null);
        IReadOnlyList<SourceSlot> found = await source.ReadAsync(request, ct);
        SourceTimeline? readTimeline = await timeline;
        IReadOnlyList<ResourceInterval>? readWorking = await working;

        // Čtení může chvíli trvat, lekce, která mezitím začala, už volná není. Volný úsek se podle toho zahodit
        // nesmí, platí jeho další začátky. Ty hlídá čtení volna, které začátky zná. Dny týdne
        // mimo nabízené období termíny nemají. Den se kontroluje až po všech čteních.
        now = timeProvider.GetUtcNow();
        if (!lastAttempt && _calendar.Today(now) != today)
        {
            return null;
        }

        DateTimeOffset notBefore = source.Kind is SlotKind.Lesson ? now : request.NotBefore;
        DateTimeOffset start = Max(now, _calendar.StartOf(period.ShownStart));
        DateTimeOffset end = _calendar.StartOf(Min(period.ShownEnd, period.OfferedEnd));
        AvailabilitySlot[] slots = [.. Publish(found, source.Kind, publicCatalog, selection, notBefore, _calendar.StartOf(period.ShownStart), end)];
        return new AvailabilityResult(slots.Length > 0 ? AvailabilityOutcome.Available : AvailabilityOutcome.NothingFree, selection, slots)
        {
            Kind = source.Kind,
            Period = period,
            Filters = selector.Options(query, selection),
            Bands = Bands(readTimeline, slots, resources, period, start),
            Days = query.Range is { Unit: ShownRange.Month }
                ? DayStates(source.Kind, slots, readWorking, resources, firstOffered, afterOffered)
                : [],
        };
    }

    // Nabídka filtrů i čtení stojí na stejném výběru, generátor tak nabídne totéž co stránka.
    private async Task<Selected> SelectAsync(AvailabilityQuery query, CancellationToken ct)
    {
        if (!connection.IsConnected)
        {
            return new Selected(AvailabilityOutcome.NotConnected);
        }

        if (query.IsMalformed || query.Days < 1)
        {
            return new Selected(AvailabilityOutcome.ViewNotAvailable);
        }

        PublicCatalog publicCatalog = await catalog.GetAsync(ct);
        var selector = new AvailabilitySelector(publicCatalog, _sources);
        (AvailabilityOutcome outcome, AvailabilitySelection? selection, ISegmentAvailabilitySource? source) = selector.Select(query);
        return new Selected(outcome, publicCatalog, selector, selection, source);
    }

    private sealed record Selected(
        AvailabilityOutcome Outcome,
        PublicCatalog? Catalog = null,
        AvailabilitySelector? Selector = null,
        AvailabilitySelection? Selection = null,
        ISegmentAvailabilitySource? Source = null);

    // Týden začíná pondělím v obou jazycích. Nabízí se rozsahy, které s obdobím mají společný aspoň
    // jeden den, den mimo období se posune na nejbližší z nich. Jeden výpočet pro každou jednotku rozsahu.
    private AvailabilityPeriod Period(DateOnly today, AvailabilityQuery query)
    {
        DateOnly offeredEnd = today.AddDays(Math.Min(query.Days, settings.MaxDays));
        if (query.Range is not { } range)
        {
            return new AvailabilityPeriod(today, offeredEnd, today, offeredEnd, null, null);
        }

        DateOnly lastOffered = offeredEnd.AddDays(-1);
        DateOnly wanted = range.Containing ?? today;
        DateOnly start = StartOf(range.Unit, wanted < today ? today : wanted > lastOffered ? lastOffered : wanted);
        DateOnly end = Following(range.Unit, start);
        return new AvailabilityPeriod(
            today,
            offeredEnd,
            start,
            end,
            start > StartOf(range.Unit, today) ? StartOf(range.Unit, start.AddDays(-1)) : null,
            start < StartOf(range.Unit, lastOffered) ? end : null);
    }

    private static DateOnly StartOf(ShownRange unit, DateOnly day) => unit switch
    {
        ShownRange.Week => day.AddDays(-(((int)day.DayOfWeek + 6) % 7)),
        ShownRange.Month => new DateOnly(day.Year, day.Month, 1),
        _ => throw new InvalidOperationException("Neznámý rozsah."),
    };

    private static DateOnly Following(ShownRange unit, DateOnly start) => unit switch
    {
        ShownRange.Week => start.AddDays(7),
        ShownRange.Month => start.AddMonths(1),
        _ => throw new InvalidOperationException("Neznámý rozsah."),
    };

    // Stav dne z už zveřejněných termínů (rozhodnutí uživatele 2026-11-09). Lekce: volno = aspoň jedna lekce není plná,
    // plno = všechny plné, zavřeno = žádná lekce. Individuální typy: volno = aspoň jeden volný úsek, plno = některý
    // veřejný zdroj pohledu ten den pracuje, zavřeno = nepracuje žádný. Pracovní doba cizích zdrojů se nepočítá.
    private DayAvailability[] DayStates(
        SlotKind kind,
        AvailabilitySlot[] slots,
        IReadOnlyList<ResourceInterval>? working,
        IReadOnlyList<PublicResource> resources,
        DateOnly first,
        DateOnly after)
    {
        HashSet<int> own = [.. resources.Select(resource => resource.Id)];
        ResourceInterval[] hours = [.. (working ?? []).Where(item => own.Contains(item.ResourceId))];
        ILookup<DateOnly, AvailabilitySlot> byDay = slots.ToLookup(slot => DateOnly.FromDateTime(slot.Start.DateTime));
        List<DayAvailability> days = [];
        for (DateOnly day = first; day < after; day = day.AddDays(1))
        {
            AvailabilitySlot[] ofDay = [.. byDay[day]];
            DateTimeOffset dayStart = _calendar.StartOf(day);
            DateTimeOffset dayEnd = _calendar.StartOf(day.AddDays(1));
            bool open = kind is SlotKind.Lesson
                ? ofDay.Length > 0
                : hours.Any(item => item.Start < dayEnd && item.End > dayStart);
            DayState state = ofDay.Any(slot => !slot.IsFull) ? DayState.Free : open ? DayState.Full : DayState.Closed;
            days.Add(new DayAvailability(day, state));
        }

        return [.. days];
    }

    // Úseky jen v nabízených dnech týdne a od teď, minulost se nekreslí (náčrt potvrzený 2026-11-09). Volno se bere
    // z už zveřejněných termínů, pracovní doba a rezervace jen u veřejných zdrojů pohledu.
    private IReadOnlyList<TimeBand> Bands(
        SourceTimeline? timeline,
        AvailabilitySlot[] slots,
        IReadOnlyList<PublicResource> resources,
        AvailabilityPeriod period,
        DateTimeOffset notBefore)
    {
        if (timeline is null)
        {
            return [];
        }

        DateOnly first = period.ShownStart < period.Today ? period.Today : period.ShownStart;
        DateOnly last = Min(period.ShownEnd, period.OfferedEnd);
        if (first >= last)
        {
            return [];
        }

        return [.. TimelineBands.Build(
                [.. slots.Select(slot => (slot.Start, slot.End))],
                timeline,
                resources.Select(resource => resource.Id).ToHashSet(),
                Max(notBefore, _calendar.StartOf(first)),
                _calendar.StartOf(last))
            .Select(band => new TimeBand(_calendar.ToLocal(band.Start), _calendar.ToLocal(band.End), band.State))];
    }


    private static DateOnly Min(DateOnly first, DateOnly second) => first < second ? first : second;

    private static DateOnly Max(DateOnly first, DateOnly second) => first > second ? first : second;

    private static DateTimeOffset Max(DateTimeOffset first, DateTimeOffset second) => first > second ? first : second;

    // Jediná brána mezi daty z API a výstupem: názvy se berou z veřejného katalogu, takže termín neveřejného zdroje
    // nebo služby nemá co zobrazit a vypadne celý (rozhodnutí uživatele 2026-11-06). Termín musí patřit i k vybranému
    // zaměření a provozovně, na odpověď API se v tom nespoléhá. Volný úsek bez služby sestavilo čtení jen z veřejných
    // služeb pohledu a název služby nenese (SPEC sekce 3).
    // Volný úsek sjednocený přes celé období se rozdělí o půlnoci, každý den ukáže své volno (rozhodnutí uživatele
    // 2026-11-09). Část dne v zobrazeném týdnu se tak ukáže, i když úsek začal dřív. Termín lekce patří jen do dne,
    // kdy začíná.
    private IEnumerable<AvailabilitySlot> Publish(
        IReadOnlyList<SourceSlot> found,
        SlotKind kind,
        PublicCatalog publicCatalog,
        AvailabilitySelection selection,
        DateTimeOffset notBefore,
        DateTimeOffset shownStart,
        DateTimeOffset end)
    {
        var services = publicCatalog.Services.ToDictionary(service => service.Id);
        var resources = publicCatalog.Resources.ToDictionary(resource => resource.Id);

        return found
            .Where(slot => slot.Start >= notBefore)
            .Where(slot => selection.Service is null || slot.ServiceId is null || slot.ServiceId == selection.Service.Id)
            .Where(slot => selection.Resource is null || slot.ResourceId == selection.Resource.Id)
            .Where(slot => slot.ServiceId is not { } serviceId
                || (services.TryGetValue(serviceId, out PublicService? service) && service.SegmentId == selection.Segment.Id))
            .Where(slot => resources.TryGetValue(slot.ResourceId, out PublicResource? resource)
                && resource.LocationId == selection.Location.Id
                && resource.SegmentIds.Contains(selection.Segment.Id))
            .SelectMany(slot => kind is SlotKind.FreeTime ? ByDay(slot) : [slot])
            .Where(slot => slot.Start >= shownStart && slot.Start < end)
            .OrderBy(slot => slot.Start)
            .ThenBy(slot => resources[slot.ResourceId].Name, StringComparer.Ordinal)
            .Select(slot => new AvailabilitySlot(
                _calendar.ToLocal(slot.Start),
                _calendar.ToLocal(slot.End),
                slot.ServiceId is { } serviceId ? services[serviceId].Name : null,
                resources[slot.ResourceId].Name,
                slot.OccupancyText,
                slot.IsFull));
    }

    private IEnumerable<SourceSlot> ByDay(SourceSlot slot)
    {
        DateTimeOffset start = slot.Start;
        DateTimeOffset midnight = _calendar.StartOf(_calendar.Today(start).AddDays(1));
        while (midnight < slot.End)
        {
            yield return slot with { Start = start, End = midnight };
            start = midnight;
            midnight = _calendar.StartOf(_calendar.Today(start).AddDays(1));
        }

        yield return slot with { Start = start };
    }
}
