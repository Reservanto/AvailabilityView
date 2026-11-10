using System.Globalization;
using AvailabilityView.Application.Availability;
using Reservanto.ApiClient;
using Reservanto.ApiClient.Endpoints;

namespace AvailabilityView.Infrastructure.Availability;

/// <summary>
/// Volno zdrojů individuálních služeb (<c>docs/SPEC.md</c> sekce 3). Zdroj má volno v čase, kdy u něj jde začít
/// aspoň jednu veřejnou službu pohledu: volné úseky jsou sjednocení intervalů od začátku do konce služby přes všechny
/// tyto služby. Jen čtení pro provozovnu: <c>OneToOne/GetAvailableStarts</c> pro <c>AutoClasses</c> a <c>EmsLike</c>
/// API odmítá (ověřeno na produkci 2026-11-06). Zdroj se vybírá z výsledku.
/// </summary>
internal sealed class IndividualAvailabilitySource(
    ReservantoApiClient client,
    ReadCache cache,
    IWorkingHours workingHours,
    IResourceOccupancy occupancy,
    TimeProvider timeProvider) : ISegmentAvailabilitySource
{
    // SPEC sekce 9.
    private static readonly TimeSpan _lifetime = TimeSpan.FromSeconds(60);

    /// <inheritdoc />
    public IReadOnlyList<string> SegmentTypes { get; } = ["OneToOne", "AutoClasses", "EmsLike"];

    /// <inheritdoc />
    public SlotKind Kind => SlotKind.FreeTime;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SourceSlot>> ReadAsync(SourceRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Každá služba zvlášť a souběžně. Selhání kterékoli dá výpadek, ne neúplné volno.
        IReadOnlyList<SourceSlot>[] perService = await Task.WhenAll(request.Services.Select(service =>
            StartsAsync(service, request.Selection.Location, request.Interval, ct)));

        // Načtení z API může chvíli trvat. Začátek, který mezitím prošel, už volno nedělá. Celý úsek se kvůli němu
        // zahodit nesmí, platí jeho další začátky.
        DateTimeOffset notBefore = Max(request.NotBefore, timeProvider.GetUtcNow());
        return FreeTime.Merge(perService.SelectMany(slots => slots).Where(slot => slot.Start >= notBefore));
    }

    /// <inheritdoc />
    public async Task<SourceTimeline?> ReadTimelineAsync(SourceRequest request, AvailabilityInterval week, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(week);

        // Rezervace za celý týden jednou pro všechny zdroje. Token bez práva je číst dá prázdnou obsazenost.
        Task<IReadOnlyList<ResourceInterval>> working = workingHours.ReadAsync([.. request.Resources.Select(item => item.Id)], week, ct);
        Task<OccupancyReading> busy = occupancy.ReadAsync(week, ct);
        return new SourceTimeline(await working, (await busy).Busy);
    }

    /// <inheritdoc />
    /// <remarks>Rezervace se pro měsíc nečtou.</remarks>
    public async Task<IReadOnlyList<ResourceInterval>?> ReadWorkingHoursAsync(SourceRequest request, AvailabilityInterval interval, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await workingHours.ReadAsync([.. request.Resources.Select(item => item.Id)], interval, ct);
    }

    // Volné začátky jedné služby jako intervaly od začátku do konce služby. Cache po službách, ať je mohou sdílet
    // pohledy s různou volbou služby.
    private Task<IReadOnlyList<SourceSlot>> StartsAsync(PublicService service, PublicLocation location, AvailabilityInterval interval, CancellationToken ct)
    {
        string key = string.Create(CultureInfo.InvariantCulture, $"individual:{service.Id}:{location.Id}:{interval.Start.ToUnixTimeSeconds()}:{interval.End.ToUnixTimeSeconds()}");
        return cache.GetAsync(key, _lifetime, () => ApiFailures.GuardAsync(() => LoadAsync(service, location, interval)), ct);
    }

    // Bez tokenu zrušení: načítání je sdílené, viz ReadCache.
    private async Task<IReadOnlyList<SourceSlot>> LoadAsync(PublicService service, PublicLocation location, AvailabilityInterval interval)
    {
        IReadOnlyDictionary<int, IReadOnlyList<DateTimeOffset>> starts = await client.OneToOne.GetAvailableStartsForLocationAsync(
            new AvailableStartsForLocationRequest
            {
                BookingServiceId = service.Id,
                LocationId = location.Id,
                IntervalStart = interval.Start,
                IntervalEnd = interval.End,
            },
            CancellationToken.None);

        return [.. starts.SelectMany(pair => pair.Value.Select(start => new SourceSlot(
            start,
            start + service.Duration,
            ServiceId: null,
            pair.Key,
            OccupancyText: null,
            IsFull: false)))];
    }

    private static DateTimeOffset Max(DateTimeOffset first, DateTimeOffset second) => first > second ? first : second;
}
