using AvailabilityView.Application.Connection;
using Microsoft.Extensions.Options;
using Reservanto.ApiClient;
using Reservanto.ApiClient.Authorization;

namespace AvailabilityView.Infrastructure.Connection;

/// <inheritdoc />
internal sealed class ReservantoConnectionSetup(IOptionsMonitor<ReservantoApiOptions> options) : IConnectionSetup
{
    private const string Accepted = "1";

    /// <inheritdoc />
    public string? PrefilledClientId => options.CurrentValue.ClientId;

    /// <inheritdoc />
    public IReadOnlyList<string> BaseRights => MerchantConnect.BaseRights;

    /// <inheritdoc />
    public IReadOnlyList<string> OccupancyRights => MerchantConnect.OccupancyRights;

    /// <inheritdoc />
    public Uri BuildConsentUrl(string clientId, Uri redirectUrl, bool occupancy) =>
        MerchantConnect.BuildConsentUrl(options.CurrentValue.ApiAddress(), clientId, redirectUrl, occupancy);

    /// <inheritdoc />
    public ConsentResult ReadConsent(string? accepted, string? token)
    {
        if (accepted != Accepted)
        {
            return new ConsentResult(ConsentOutcome.Denied);
        }

        return MerchantConnect.HasLongTimeTokenShape(token)
            ? new ConsentResult(ConsentOutcome.Accepted, token)
            : new ConsentResult(ConsentOutcome.Invalid);
    }
}
