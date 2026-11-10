using System.Globalization;
using AvailabilityView.Application.Availability;
using Reservanto.ApiClient;
using Reservanto.ApiClient.Endpoints;
using Reservanto.ApiClient.Models;

namespace AvailabilityView.Infrastructure.Availability;

/// <summary>Lekce s pevným termínem. API vrací i plně obsazené, ty zůstávají vidět (<c>docs/SPEC.md</c> sekce 3).</summary>
internal sealed class ClassesAvailabilitySource(ReservantoApiClient client, ReadCache cache) : ISegmentAvailabilitySource
{
    // SPEC sekce 9.
    private static readonly TimeSpan _lifetime = TimeSpan.FromSeconds(60);

    /// <inheritdoc />
    public IReadOnlyList<string> SegmentTypes { get; } = ["Classes"];

    /// <inheritdoc />
    public SlotKind Kind => SlotKind.Lesson;

    /// <inheritdoc />
    public Task<IReadOnlyList<SourceSlot>> ReadAsync(SourceRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        AvailabilitySelection selection = request.Selection;
        AvailabilityInterval interval = request.Interval;

        // Služba a zdroj v klíči nejsou, filtruje se až z výsledku. Všechny pohledy jedné provozovny tak sdílí jeden záznam.
        string key = string.Create(CultureInfo.InvariantCulture, $"classes:{selection.Segment.Id}:{selection.Location.Id}:{interval.Start.ToUnixTimeSeconds()}:{interval.End.ToUnixTimeSeconds()}");
        return cache.GetAsync(key, _lifetime, () => ApiFailures.GuardAsync(() => LoadAsync(selection, interval)), ct);
    }

    /// <inheritdoc />
    /// <remarks>Lekce pracovní dobu nemají, hodinová osa je ukáže jako bloky podle termínů.</remarks>
    public Task<SourceTimeline?> ReadTimelineAsync(SourceRequest request, AvailabilityInterval week, CancellationToken ct) =>
        Task.FromResult<SourceTimeline?>(null);

    /// <inheritdoc />
    public Task<IReadOnlyList<ResourceInterval>?> ReadWorkingHoursAsync(SourceRequest request, AvailabilityInterval interval, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ResourceInterval>?>(null);

    // Bez tokenu zrušení: načítání je sdílené, viz ReadCache.
    private async Task<IReadOnlyList<SourceSlot>> LoadAsync(AvailabilitySelection selection, AvailabilityInterval interval)
    {
        IReadOnlyList<ExtendedAppointmentClassApiModel> appointments = await client.Classes.GetAvailableAppointmentsAsync(
            new AvailableClassesRequest
            {
                SegmentId = selection.Segment.Id,
                LocationId = selection.Location.Id,
                IntervalStart = interval.Start,
                IntervalEnd = interval.End,
            },
            CancellationToken.None);

        return [.. appointments.Select(appointment => new SourceSlot(
            appointment.StartDate,
            appointment.EndDate,
            appointment.BookingServiceId,
            appointment.BookingResourceId,
            appointment.FormattedAvailability,
            !appointment.IsAvailable))];
    }
}
