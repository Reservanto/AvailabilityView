using System.Globalization;
using AvailabilityView.Application.Availability;
using Reservanto.ApiClient;
using Reservanto.ApiClient.Endpoints;
using Reservanto.ApiClient.Models;

namespace AvailabilityView.Infrastructure.Availability;

/// <summary>
/// Pracovní doba zdrojů z <c>WorkingHours/GetForPeriod</c>, po zdrojích: zdroj může pracovat jiné dny než provozovna
/// (ověřeno na produkci 2026-11-08). Cache 1 hodinu jako seznamy (SPEC sekce 9).
/// </summary>
internal sealed class ReservantoWorkingHours(ReservantoApiClient client, ReadCache cache) : IWorkingHours
{
    private static readonly TimeSpan _lifetime = TimeSpan.FromHours(1);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResourceInterval>> ReadAsync(IReadOnlyList<int> resourceIds, AvailabilityInterval interval, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(resourceIds);
        ArgumentNullException.ThrowIfNull(interval);

        IReadOnlyList<ResourceInterval>[] perResource = await Task.WhenAll(resourceIds.Distinct().Select(resourceId =>
        {
            string key = string.Create(CultureInfo.InvariantCulture, $"working:{resourceId}:{interval.Start.ToUnixTimeSeconds()}:{interval.End.ToUnixTimeSeconds()}");
            return cache.GetAsync(key, _lifetime, () => ApiFailures.GuardAsync(() => LoadAsync(resourceId, interval)), ct);
        }));
        return [.. perResource.SelectMany(items => items)];
    }

    // Bez tokenu zrušení: načítání je sdílené, viz ReadCache.
    private async Task<IReadOnlyList<ResourceInterval>> LoadAsync(int resourceId, AvailabilityInterval interval)
    {
        IReadOnlyList<WorkingHourApiModel> hours = await client.WorkingHours.GetForPeriodAsync(
            new WorkingHoursForPeriodRequest
            {
                BookingResourceId = resourceId,
                IntervalStart = interval.Start,
                IntervalEnd = interval.End,
            },
            CancellationToken.None);

        return [.. hours.Where(hour => hour.End > hour.Start).Select(hour => new ResourceInterval(resourceId, hour.Start, hour.End))];
    }
}
