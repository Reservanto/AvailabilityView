using System.Net;
using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Connection;
using Reservanto.ApiClient.Core;

namespace AvailabilityView.Infrastructure.Availability;

/// <summary>Převod chyb klienta na typ z Application.</summary>
internal static class ApiFailures
{
    /// <summary>Spustí čtení z API. Každé selhání skončí jako <see cref="ReservationSystemException"/>.</summary>
    public static async Task<T> GuardAsync<T>(Func<Task<T>> read)
    {
        try
        {
            return await read();
        }
        catch (ReservantoApiException exception)
        {
            throw new ReservationSystemException(ReasonOf(exception));
        }
    }

    private static ConnectionState ReasonOf(ReservantoApiException exception) => exception switch
    {
        ReservantoTokenRejectedException => ConnectionState.TokenRejected,
        { StatusCode: HttpStatusCode.Unauthorized } => ConnectionState.TokenRejected,
        { StatusCode: HttpStatusCode.Forbidden } => ConnectionState.MissingRights,
        _ => ConnectionState.Unavailable,
    };
}
