using AvailabilityView.Application.Connection;
using Microsoft.Extensions.Options;
using Reservanto.ApiClient;

namespace AvailabilityView.Infrastructure.Connection;

/// <inheritdoc />
internal sealed class ReservantoInstanceConnection(IOptionsMonitor<ReservantoApiOptions> options) : IInstanceConnection
{
    /// <inheritdoc />
    public bool IsConnected => !string.IsNullOrWhiteSpace(options.CurrentValue.LongTimeToken);
}
