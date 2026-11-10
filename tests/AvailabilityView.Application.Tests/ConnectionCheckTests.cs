using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Connection;
using AvailabilityView.Application.Tests.Support;

namespace AvailabilityView.Application.Tests;

/// <summary>Ověření připojení v průvodci (<c>docs/SPEC.md</c> sekce 6 krok 4).</summary>
public sealed class ConnectionCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Connected_CountsPublicObjects_AndNamesAccount()
    {
        var harness = new ReaderHarness();

        ConnectionCheckResult result = await harness.Check.CheckAsync(Ct);

        Assert.Equal(new ConnectionCheckResult(ConnectionState.Connected, 4, 2, "Studio Testovací") { ShowsOccupancy = true }, result);
    }

    [Fact]
    public async Task TokenWithoutRightToReadReservations_IsConnected_WithoutOccupancy()
    {
        // Obsazenost je volba správce (SPEC sekce 6), token bez práva Event_r není chyba.
        var harness = new ReaderHarness();
        harness.Occupancy.Permitted = false;

        ConnectionCheckResult result = await harness.Check.CheckAsync(Ct);

        Assert.Equal(new ConnectionCheckResult(ConnectionState.Connected, 4, 2, "Studio Testovací"), result);
    }

    [Fact]
    public async Task NotConnected_DoesNotCallApi()
    {
        var harness = new ReaderHarness();
        harness.Connection.IsConnected = false;

        ConnectionCheckResult result = await harness.Check.CheckAsync(Ct);

        Assert.Equal(ConnectionState.NotConnected, result.State);
        Assert.Equal(0, harness.Catalog.Calls);
        Assert.Equal(0, harness.Merchant.Calls);
    }

    [Theory]
    [InlineData(ConnectionState.TokenRejected)]
    [InlineData(ConnectionState.MissingRights)]
    [InlineData(ConnectionState.Unavailable)]
    public async Task AvailabilityFailure_IsReported_EvenWhenCatalogWorks(ConnectionState reason)
    {
        // Katalog nepotřebuje právo FreeSpace_r a bývá z cache. Až čtení termínů ověří páté právo a platnost tokenu.
        var harness = new ReaderHarness();
        harness.Individual.Failure = reason;

        ConnectionCheckResult result = await harness.Check.CheckAsync(Ct);

        Assert.Equal(reason, result.State);
        Assert.Equal(1, harness.Individual.Calls);
    }

    [Theory]
    [InlineData(ConnectionState.TokenRejected)]
    [InlineData(ConnectionState.MissingRights)]
    [InlineData(ConnectionState.Unavailable)]
    public async Task Failure_KeepsReason(ConnectionState reason)
    {
        var harness = new ReaderHarness();
        harness.Catalog.Failure = reason;

        ConnectionCheckResult result = await harness.Check.CheckAsync(Ct);

        Assert.Equal(reason, result.State);
    }

    [Theory]
    [InlineData(ConnectionState.TokenRejected)]
    [InlineData(ConnectionState.MissingRights)]
    [InlineData(ConnectionState.Unavailable)]
    public async Task MerchantFailure_IsReported_EvenWhenEverythingElseWorks(ConnectionState reason)
    {
        // Token připojený před přidáním práva Merchant_r: termíny jdou, název ne. Správce se má připojit znovu.
        var harness = new ReaderHarness();
        harness.Merchant.Failure = reason;

        ConnectionCheckResult result = await harness.Check.CheckAsync(Ct);

        Assert.Equal(new ConnectionCheckResult(reason), result);
    }

    [Theory]
    [InlineData(ConnectionState.TokenRejected)]
    [InlineData(ConnectionState.MissingRights)]
    [InlineData(ConnectionState.Unavailable)]
    public async Task OccupancyFailure_IsReported_EvenWhenEverythingElseWorks(ConnectionState reason)
    {
        // Chybějící právo hlásí čtení obsazenosti jako NotPermitted, sem se dostane jen skutečné selhání.
        var harness = new ReaderHarness();
        harness.Occupancy.Failure = reason;

        ConnectionCheckResult result = await harness.Check.CheckAsync(Ct);

        Assert.Equal(new ConnectionCheckResult(reason), result);
    }

    [Fact]
    public async Task Occupancy_IsCheckedForWholeCurrentHour()
    {
        // Celá hodina: opakované ověření v téže hodině sdílí cache a API se neptá znovu.
        var harness = new ReaderHarness();
        harness.Time.SetUtcNow(new DateTimeOffset(2026, 11, 6, 8, 42, 17, TimeSpan.Zero));

        await harness.Check.CheckAsync(Ct);

        var hour = new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero);
        Assert.Equal(new AvailabilityInterval(hour, hour.AddHours(1)), harness.Occupancy.LastInterval);
    }
}
