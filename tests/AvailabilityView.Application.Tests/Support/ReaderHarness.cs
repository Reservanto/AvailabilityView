using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Connection;
using Microsoft.Extensions.Time.Testing;

namespace AvailabilityView.Application.Tests.Support;

/// <summary>
/// Reader nad ručními náhradami. Katalog: zaměření 1 individuální, 2 lekce, 3 nepodporované, 4 individuální bez
/// zdrojů, provozovny 10 a 20, zdroje 100 (provozovna 10), 101 (provozovna 20), 102 (lekce, provozovna 20),
/// služby 1000 (zdroj 101), 1001 (zdroje 100 a 101), 1002 (lekce, zdroj 102).
/// </summary>
internal sealed class ReaderHarness
{
    public const string TimeZoneId = "Europe/Prague";

    public static PublicCatalog DefaultCatalog { get; } = new(
        [
            new PublicSegment(3, "Půjčovna", "RentalLike"),
            new PublicSegment(4, "Prázdné", "Individual"),
            new PublicSegment(1, "Masáže", "Individual"),
            new PublicSegment(2, "Lekce", "Lessons"),
        ],
        [new PublicLocation(10, "Praha"), new PublicLocation(20, "Brno")],
        [
            new PublicService(1000, "Klasická", 1, TimeSpan.FromMinutes(60), [101]),
            new PublicService(1001, "Sportovní", 1, TimeSpan.FromMinutes(30), [100, 101]),
            new PublicService(1002, "Jóga", 2, TimeSpan.FromMinutes(45), [102]),
        ],
        [
            new PublicResource(100, "Jana", 10, [1]),
            new PublicResource(101, "Hana", 20, [1]),
            new PublicResource(102, "Sál", 20, [2]),
        ]);

    public FakeConnection Connection { get; } = new();

    public FakeCatalog Catalog { get; } = new();

    public FakeMerchant Merchant { get; } = new();

    public FakeOccupancy Occupancy { get; } = new();

    public FakeSource Individual { get; } = new(["Individual"], SlotKind.FreeTime);

    public FakeSource Lessons { get; } = new(["Lessons"], SlotKind.Lesson);

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero));

    public int MaxDays { get; set; } = 60;

    public string TimeZone { get; set; } = TimeZoneId;

    public AvailabilityReader Reader =>
        new(Connection, Catalog, [Individual, Lessons], new AvailabilitySettings(TimeZoneInfo.FindSystemTimeZoneById(TimeZone), MaxDays), Time);

    public ConnectionCheck Check => new(Connection, Catalog, Reader, Merchant, Occupancy, Time);

    public Task<AvailabilityResult> ReadAsync(int? segment = null, int? location = null, int? service = null, int? resource = null, int days = 14) =>
        ReadAsync(new AvailabilityQuery(segment, location, service, resource, days));

    public Task<AvailabilityResult> ReadAsync(AvailabilityQuery query) => Reader.ReadAsync(query, TestContext.Current.CancellationToken);

    internal sealed class FakeConnection : IInstanceConnection
    {
        public bool IsConnected { get; set; } = true;
    }

    internal sealed class FakeCatalog : IPublicCatalog
    {
        public PublicCatalog Value { get; set; } = DefaultCatalog;

        public int Calls { get; private set; }

        public ConnectionState? Failure { get; set; }

        public Task<PublicCatalog> GetAsync(CancellationToken ct)
        {
            Calls++;
            return Failure is { } reason ? throw new ReservationSystemException(reason) : Task.FromResult(Value);
        }
    }

    internal sealed class FakeMerchant : IMerchantInfo
    {
        public string Name { get; set; } = "Studio Testovací";

        public int Calls { get; private set; }

        public ConnectionState? Failure { get; set; }

        public Task<string> GetNameAsync(CancellationToken ct)
        {
            Calls++;
            return Failure is { } reason ? throw new ReservationSystemException(reason) : Task.FromResult(Name);
        }
    }

    internal sealed class FakeOccupancy : IResourceOccupancy
    {
        public List<ResourceInterval> Busy { get; } = [];

        public ConnectionState? Failure { get; set; }

        public AvailabilityInterval? LastInterval { get; private set; }

        public int Calls { get; private set; }

        public bool Permitted { get; set; } = true;

        public Task<OccupancyReading> ReadAsync(AvailabilityInterval interval, CancellationToken ct)
        {
            Calls++;
            LastInterval = interval;
            return Failure is { } reason
                ? throw new ReservationSystemException(reason)
                : Task.FromResult(Permitted ? new OccupancyReading(true, Busy) : OccupancyReading.NotPermitted);
        }
    }

    internal sealed class FakeSource(IReadOnlyList<string> types, SlotKind kind) : ISegmentAvailabilitySource
    {
        public IReadOnlyList<string> SegmentTypes => types;

        public SlotKind Kind => kind;

        public List<SourceSlot> Slots { get; } = [];

        public ConnectionState? Failure { get; set; }

        public AvailabilitySelection? LastSelection { get; private set; }

        public IReadOnlyList<PublicService>? LastServices { get; private set; }

        public AvailabilityInterval? LastInterval { get; private set; }

        public int Calls { get; private set; }

        /// <summary>Co se stane během čtení, třeba posun času.</summary>
        public Action? WhileReading { get; set; }

        public SourceTimeline? Timeline { get; set; }

        public AvailabilityInterval? LastWeek { get; private set; }

        public int TimelineCalls { get; private set; }

        public AvailabilityInterval? LastWorkingInterval { get; private set; }

        public int WorkingCalls { get; private set; }

        /// <summary>Co se stane, když pracovní doba dorazí až po volných termínech, třeba posun času.</summary>
        public Action? WhileReadingWorkingHours { get; set; }

        public Task<IReadOnlyList<ResourceInterval>?> ReadWorkingHoursAsync(SourceRequest request, AvailabilityInterval interval, CancellationToken ct)
        {
            WorkingCalls++;
            LastWorkingInterval = interval;
            if (Failure is { } reason)
            {
                throw new ReservationSystemException(reason);
            }

            return WhileReadingWorkingHours is { } whileReading ? LaterAsync(whileReading) : Task.FromResult(Timeline?.Working);

            async Task<IReadOnlyList<ResourceInterval>?> LaterAsync(Action action)
            {
                await Task.Delay(50, ct);
                action();
                return Timeline?.Working;
            }
        }

        public Task<SourceTimeline?> ReadTimelineAsync(SourceRequest request, AvailabilityInterval week, CancellationToken ct)
        {
            TimelineCalls++;
            LastWeek = week;
            return Failure is { } reason ? throw new ReservationSystemException(reason) : Task.FromResult(Timeline);
        }

        public Task<IReadOnlyList<SourceSlot>> ReadAsync(SourceRequest request, CancellationToken ct)
        {
            Calls++;
            LastSelection = request.Selection;
            LastServices = request.Services;
            LastInterval = request.Interval;
            WhileReading?.Invoke();
            return Failure is { } reason
                ? throw new ReservationSystemException(reason)
                : Task.FromResult<IReadOnlyList<SourceSlot>>(Slots);
        }
    }
}
