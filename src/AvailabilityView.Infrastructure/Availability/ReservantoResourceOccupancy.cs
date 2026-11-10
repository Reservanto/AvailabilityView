using System.Net;
using AvailabilityView.Application.Availability;
using Reservanto.ApiClient;
using Reservanto.ApiClient.Core;
using Reservanto.ApiClient.Endpoints;
using Reservanto.ApiClient.Models;

namespace AvailabilityView.Infrastructure.Availability;

/// <summary>
/// Obsazenost zdrojů z <c>Event/GetList</c>. Klient údaje zákazníků při čtení zahodí, sem dojde jen zdroj, čas a stav.
/// Cache jako volné termíny, 60 sekund. Token bez práva <c>Event_r</c> je volba správce (SPEC sekce 6), ne chyba.
/// </summary>
internal sealed class ReservantoResourceOccupancy(ReservantoApiClient client, ReadCache cache) : IResourceOccupancy
{
    private static readonly TimeSpan _lifetime = TimeSpan.FromSeconds(60);

    // Právo se mění jen s novým tokenem a ten celou cache zahodí. Hodina šetří volání, která API stejně odmítne.
    private static readonly TimeSpan _notPermittedLifetime = TimeSpan.FromHours(1);

    // Rezervace čekající na potvrzení čas drží. Zamítnuté, stornované a náhradníci ne.
    private static readonly HashSet<AppointmentStatus> _occupying = [AppointmentStatus.Confirmed, AppointmentStatus.ConfirmationRequested];

    /// <inheritdoc />
    public Task<OccupancyReading> ReadAsync(AvailabilityInterval interval, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(interval);
        string key = $"events:{interval.Start.ToUnixTimeSeconds()}:{interval.End.ToUnixTimeSeconds()}";
        return cache.GetAsync(
            key,
            reading => reading.IsPermitted ? _lifetime : _notPermittedLifetime,
            () => ApiFailures.GuardAsync(() => LoadAsync(interval)),
            ct);
    }

    // Bez tokenu zrušení: načítání je sdílené, viz ReadCache.
    private async Task<OccupancyReading> LoadAsync(AvailabilityInterval interval)
    {
        IReadOnlyList<EventApiModel> events;
        try
        {
            events = await client.Event.GetListAsync(
                new EventListRequest { StartDate = interval.Start, EndDate = interval.End }, CancellationToken.None);
        }
        catch (ReservantoApiException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            // Produkční API bez práva Event_r odpoví 403 (ověřeno 2026-11-08). Výpadek má jiný stav a zůstává chybou.
            return OccupancyReading.NotPermitted;
        }

        return new OccupancyReading(true, [.. events
            .Where(item => _occupying.Contains(item.Status))
            .Select(item => new ResourceInterval(item.SourceId, item.StartDate, item.EndDate))]);
    }
}
