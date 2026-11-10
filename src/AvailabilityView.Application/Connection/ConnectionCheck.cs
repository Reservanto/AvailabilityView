using AvailabilityView.Application.Availability;

namespace AvailabilityView.Application.Connection;

/// <inheritdoc />
public sealed class ConnectionCheck(
    IInstanceConnection connection,
    IPublicCatalog catalog,
    IAvailabilityReader reader,
    IMerchantInfo merchant,
    IResourceOccupancy occupancy,
    TimeProvider timeProvider) : IConnectionCheck
{
    private static readonly TimeSpan _hour = TimeSpan.FromHours(1);

    /// <inheritdoc />
    public async Task<ConnectionCheckResult> CheckAsync(CancellationToken ct)
    {
        if (!connection.IsConnected)
        {
            return new ConnectionCheckResult(ConnectionState.NotConnected);
        }

        try
        {
            PublicCatalog publicCatalog = await catalog.GetAsync(ct);

            // Katalog nepotřebuje právo FreeSpace_r a bývá hodinu v cache. Až čtení termínů ověří toto právo a to,
            // že token pořád platí. Výchozí pohled na jeden den, ten sdílí cache s návštěvníky.
            AvailabilityResult availability = await reader.ReadAsync(new AvailabilityQuery(null, null, null, null, 1), ct);
            if (availability.Failure is { } failure)
            {
                return new ConnectionCheckResult(failure);
            }

            // Název účtu potřebuje právo Merchant_r. Token bez něj hlásí chybějící práva jako každé jiné.
            string merchantName = await merchant.GetNameAsync(ct);

            // Obsazenost je volba správce, token bez práva číst rezervace je v pořádku. Celá hodina, aby opakované
            // ověření sdílelo cache.
            DateTimeOffset now = timeProvider.GetUtcNow();
            var hour = new DateTimeOffset(now.Ticks - (now.Ticks % _hour.Ticks), TimeSpan.Zero);
            OccupancyReading reading = await occupancy.ReadAsync(new AvailabilityInterval(hour, hour + _hour), ct);

            return new ConnectionCheckResult(ConnectionState.Connected, publicCatalog.Segments.Count, publicCatalog.Locations.Count, merchantName)
            {
                ShowsOccupancy = reading.IsPermitted,
            };
        }
        catch (ReservationSystemException exception)
        {
            return new ConnectionCheckResult(exception.Reason);
        }
    }
}
