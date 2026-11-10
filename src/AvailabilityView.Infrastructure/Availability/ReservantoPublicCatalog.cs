using AvailabilityView.Application.Availability;
using Reservanto.ApiClient;
using Reservanto.ApiClient.Endpoints;
using Reservanto.ApiClient.Models;

namespace AvailabilityView.Infrastructure.Availability;

/// <summary>
/// Veřejné objekty z API. Filtruje podle <see cref="ObjectState"/> i po dotazu s <c>OnlyPublic</c>: na filtr API
/// se u dat, která jdou na veřejnou stránku, sám nespoléhá. Převod vybírá jen pole povolená pro zobrazení.
/// </summary>
internal sealed class ReservantoPublicCatalog(ReservantoApiClient client, ReadCache cache) : IPublicCatalog
{
    private const string CacheKey = "catalog";

    // SPEC sekce 9.
    private static readonly TimeSpan _lifetime = TimeSpan.FromHours(1);

    /// <inheritdoc />
    public Task<PublicCatalog> GetAsync(CancellationToken ct) =>
        cache.GetAsync(CacheKey, _lifetime, () => ApiFailures.GuardAsync(LoadAsync), ct);

    // Bez tokenu zrušení: načítání je sdílené, viz ReadCache. Mezi stránkami se položka může posunout a přijít dvakrát,
    // proto DistinctBy.
    private async Task<PublicCatalog> LoadAsync()
    {
        IReadOnlyList<SegmentApiModel> segments = await client.Segment.GetListAsync(new ObjectStateListRequest { OnlyPublic = true }, CancellationToken.None);
        IReadOnlyList<LocationApiModel> locations = await client.Location.GetListAsync(new ObjectStateListRequest { OnlyPublic = true }, CancellationToken.None);
        IReadOnlyList<BookingServiceApiModel> services = await client.BookingService.GetListAsync(new ObjectStateModifiedAfterListRequest { OnlyPublic = true }, CancellationToken.None);
        IReadOnlyList<BookingResourceApiModel> resources = await client.BookingResource.GetListAsync(new BookingResourceGetListRequest { OnlyPublic = true }, CancellationToken.None);

        PublicSegment[] publicSegments = [.. segments.Where(segment => segment.State == ObjectState.Public).DistinctBy(segment => segment.Id).Select(ToPublic)];
        HashSet<int> segmentIds = [.. publicSegments.Select(segment => segment.Id)];
        HashSet<int> locationIds = [.. locations.Where(location => location.State == ObjectState.Public).Select(location => location.Id)];

        PublicResource[] publicResources = [.. resources
            .Where(resource => resource.State == ObjectState.Public && locationIds.Contains(resource.LocationId))
            .DistinctBy(resource => resource.Id)
            .Select(resource => ToPublic(resource, segmentIds))];
        HashSet<int> resourceIds = [.. publicResources.Select(resource => resource.Id)];

        return new PublicCatalog(
            publicSegments,
            [.. locations.Where(location => location.State == ObjectState.Public).DistinctBy(location => location.Id).Select(location => new PublicLocation(location.Id, location.Name ?? string.Empty))],
            [.. services
                .Where(service => service.State == ObjectState.Public && segmentIds.Contains(service.SegmentId))
                .DistinctBy(service => service.Id)
                .Select(service => ToPublic(service, resourceIds))],
            publicResources);
    }

    // InternalName je interní pojmenování merchanta, na veřejnou stránku nepatří.
    private static PublicSegment ToPublic(SegmentApiModel segment) =>
        new(
            segment.Id,
            (string.IsNullOrWhiteSpace(segment.CustomizedName) ? segment.LocalizedName : segment.CustomizedName) ?? string.Empty,
            segment.SegmentType ?? string.Empty);

    private static PublicResource ToPublic(BookingResourceApiModel resource, HashSet<int> segmentIds) =>
        new(resource.Id, resource.Name ?? string.Empty, resource.LocationId, [.. (resource.SegmentIds ?? []).Where(segmentIds.Contains)]);

    private static PublicService ToPublic(BookingServiceApiModel service, HashSet<int> resourceIds) =>
        new(
            service.Id,
            service.Name ?? string.Empty,
            service.SegmentId,
            TimeSpan.FromMinutes(service.Duration),
            [.. (service.BookingResourceIds ?? []).Where(resourceIds.Contains)]);
}
