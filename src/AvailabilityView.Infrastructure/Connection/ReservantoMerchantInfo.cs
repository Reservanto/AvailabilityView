using AvailabilityView.Application.Connection;
using AvailabilityView.Infrastructure.Availability;
using Reservanto.ApiClient;
using Reservanto.ApiClient.Endpoints;
using Reservanto.ApiClient.Models;

namespace AvailabilityView.Infrastructure.Connection;

/// <summary>
/// Název účtu z <c>Merchant/GetInfo</c>. Odpověď nese i IČO, kontakty a adresy, do aplikace z ní jde jen název.
/// Stránka připojení je veřejná, proto název drží cache stejně dlouho jako katalog.
/// </summary>
internal sealed class ReservantoMerchantInfo(ReservantoApiClient client, ReadCache cache) : IMerchantInfo
{
    private const string CacheKey = "merchant";

    private static readonly TimeSpan _lifetime = TimeSpan.FromHours(1);

    /// <inheritdoc />
    public Task<string> GetNameAsync(CancellationToken ct) =>
        cache.GetAsync(CacheKey, _lifetime, () => ApiFailures.GuardAsync(LoadAsync), ct);

    // Bez tokenu zrušení: načítání je sdílené, viz ReadCache.
    private async Task<string> LoadAsync()
    {
        MerchantInfoApiModel merchant = await client.Merchant.GetInfoAsync(new EmptyRequest(), CancellationToken.None);
        return merchant.Name?.Trim() ?? string.Empty;
    }
}
