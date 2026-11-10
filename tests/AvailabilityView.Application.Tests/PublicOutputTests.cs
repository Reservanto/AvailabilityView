using System.Reflection;
using AvailabilityView.Application.Availability;
using AvailabilityView.Application.Connection;
using AvailabilityView.Application.Tests.Support;

namespace AvailabilityView.Application.Tests;

/// <summary>Co se smí dostat na veřejnou stránku (<c>docs/SPEC.md</c> sekce 10).</summary>
public sealed class PublicOutputTests
{
    private static readonly DateTimeOffset _start = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    // Přidání pole je rozhodnutí uživatele (profil projektu), seznam se proto mění jen s jeho souhlasem.
    private static readonly Dictionary<Type, string[]> _allowedFields = new()
    {
        [typeof(AvailabilityResult)] = ["Bands", "Days", "Failure", "Filters", "Kind", "Outcome", "Period", "Selection", "Slots"],
        // Stav dne měsíce nese jen den a stav (rozhodnutí uživatele 2026-11-09).
        [typeof(DayAvailability)] = ["Day", "State"],
        // Nabídka filtrů pro generátor, stejné typy jako filtry stránky.
        [typeof(AvailabilityOptions)] = ["Failure", "Filters", "Outcome", "Selection"],
        // Úseky hodinové osy nesou jen čas a stav, žádný údaj rezervace.
        [typeof(TimeBand)] = ["End", "Start", "State"],
        // Období ve výsledku a filtry jen s Id a názvem veřejných objektů.
        [typeof(AvailabilityPeriod)] = ["Next", "OfferedEnd", "Previous", "ShownEnd", "ShownStart", "Today"],
        [typeof(FilterChoices)] = ["AllowsAll", "Filter", "Options", "SelectedId"],
        [typeof(FilterOption)] = ["Id", "Name"],
        [typeof(PublicCatalog)] = ["Locations", "Resources", "Segments", "Services"],
        [typeof(AvailabilitySlot)] = ["End", "IsFull", "OccupancyText", "ResourceName", "ServiceName", "Start"],
        [typeof(AvailabilitySelection)] = ["Location", "Resource", "Segment", "Service"],
        [typeof(PublicSegment)] = ["Id", "Name", "Type"],
        [typeof(PublicLocation)] = ["Id", "Name"],
        [typeof(PublicService)] = ["Duration", "Id", "Name", "ResourceIds", "SegmentId"],
        [typeof(PublicResource)] = ["Id", "LocationId", "Name", "SegmentIds"],
        // Stránka připojení je také veřejná. Z údajů merchanta jen název (docs/decisions.md).
        [typeof(ConnectionCheckResult)] = ["MerchantName", "PublicLocations", "PublicSegments", "ShowsOccupancy", "State"],
    };

    public static TheoryData<Type> OutputTypes => [.. _allowedFields.Keys];

    [Theory]
    [MemberData(nameof(OutputTypes))]
    public void OutputType_HasOnlyAllowedFields(Type type)
    {
        Assert.Empty(ForbiddenFields(type, _allowedFields[type]));
    }

    [Fact]
    public void EveryTypeReachableFromOutput_IsInAllowList()
    {
        // Nový typ ve výstupu se tak nedostane mimo kontrolu, i když na něj seznam zapomene.
        HashSet<Type> reachable = [];
        Reach(typeof(AvailabilityResult), reachable);
        Reach(typeof(AvailabilityOptions), reachable);
        Reach(typeof(PublicCatalog), reachable);
        Reach(typeof(ConnectionCheckResult), reachable);

        Assert.Empty(reachable.Where(type => !_allowedFields.ContainsKey(type)).Select(type => type.Name));
    }

    [Fact]
    public void Reach_FindsTypesInsideLists()
    {
        HashSet<Type> reachable = [];
        Reach(typeof(PublicCatalog), reachable);

        Assert.Contains(typeof(PublicResource), reachable);
    }

    [Fact]
    public void Rule_CatchesNewField()
    {
        Assert.Equal(["Email"], ForbiddenFields(typeof(LeakingSlot), ["Start"]));
    }

    [Fact]
    public async Task SlotOfPrivateResource_IsLeftOutCompletely()
    {
        var harness = new ReaderHarness();
        harness.Lessons.Slots.Add(Slot(service: 1002, resource: 777));

        AvailabilityResult result = await harness.ReadAsync(segment: 2);

        Assert.Equal(AvailabilityOutcome.NothingFree, result.Outcome);
    }

    [Fact]
    public async Task SlotOfPrivateService_IsLeftOutCompletely()
    {
        var harness = new ReaderHarness();
        harness.Lessons.Slots.Add(Slot(service: 777, resource: 102));

        AvailabilityResult result = await harness.ReadAsync(segment: 2);

        Assert.Equal(AvailabilityOutcome.NothingFree, result.Outcome);
    }

    [Fact]
    public async Task PublicSlot_TakesNamesFromCatalog()
    {
        var harness = new ReaderHarness();
        harness.Lessons.Slots.Add(Slot(service: 1002, resource: 102) with { OccupancyText = "2/10", IsFull = true });

        AvailabilitySlot slot = Assert.Single((await harness.ReadAsync(segment: 2)).Slots);

        Assert.Equal("Jóga", slot.ServiceName);
        Assert.Equal("Sál", slot.ResourceName);
        Assert.Equal("2/10", slot.OccupancyText);
        Assert.True(slot.IsFull);
    }

    [Fact]
    public async Task ChosenService_FiltersLessons()
    {
        var harness = new ReaderHarness();
        harness.Catalog.Value = ReaderHarness.DefaultCatalog with
        {
            Services = [.. ReaderHarness.DefaultCatalog.Services, new PublicService(1003, "Pilates", 2, TimeSpan.FromHours(1), [102])],
        };
        harness.Lessons.Slots.AddRange([Slot(service: 1002, resource: 102), Slot(service: 1003, resource: 102)]);

        AvailabilitySlot slot = Assert.Single((await harness.ReadAsync(segment: 2, service: 1003)).Slots);

        Assert.Equal("Pilates", slot.ServiceName);
    }

    [Fact]
    public async Task ChosenResource_FiltersSlots()
    {
        var harness = new ReaderHarness();
        harness.Individual.Slots.AddRange([Slot(service: 1001, resource: 100), Slot(service: 1001, resource: 101)]);

        AvailabilitySlot slot = Assert.Single((await harness.ReadAsync(service: 1001, resource: 101)).Slots);

        Assert.Equal("Hana", slot.ResourceName);
    }

    [Fact]
    public async Task Slots_AreOrderedByTime()
    {
        var harness = new ReaderHarness();
        harness.Individual.Slots.AddRange([
            Slot(service: 1001, resource: 100).MovedBy(TimeSpan.FromHours(1)),
            Slot(service: 1001, resource: 100),
        ]);

        AvailabilityResult result = await harness.ReadAsync(service: 1001, location: 10);

        Assert.Equal([_start, _start.AddHours(1)], result.Slots.Select(slot => slot.Start.ToUniversalTime()));
    }

    [Fact]
    public async Task SlotOfResourceFromOtherLocation_IsLeftOut()
    {
        var harness = new ReaderHarness();
        harness.Individual.Slots.AddRange([Slot(service: 1001, resource: 100), Slot(service: 1001, resource: 101)]);

        AvailabilityResult result = await harness.ReadAsync(service: 1001, location: 10);

        Assert.Equal(["Jana"], result.Slots.Select(slot => slot.ResourceName));
    }

    [Fact]
    public async Task SlotOfResourceOrServiceFromOtherSegment_IsLeftOut()
    {
        var harness = new ReaderHarness();
        harness.Lessons.Slots.AddRange([
            Slot(service: 1002, resource: 101), // zdroj zaměření 1 ve stejné provozovně
            Slot(service: 1001, resource: 102), // služba zaměření 1
            Slot(service: 1002, resource: 102),
        ]);

        AvailabilityResult result = await harness.ReadAsync(segment: 2);

        AvailabilitySlot slot = Assert.Single(result.Slots);
        Assert.Equal(("Jóga", "Sál"), (slot.ServiceName, slot.ResourceName));
    }

    private static string[] ForbiddenFields(Type type, string[] allowed) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Where(name => name != "EqualityContract")
            .Except(allowed, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    private static void Reach(Type type, HashSet<Type> reachable)
    {
        if (type.Assembly != typeof(AvailabilityResult).Assembly || type.IsEnum || !reachable.Add(type))
        {
            return;
        }

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Type propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            foreach (Type candidate in propertyType.IsGenericType ? propertyType.GetGenericArguments() : [propertyType])
            {
                Reach(candidate, reachable);
            }
        }
    }

    private static SourceSlot Slot(int service, int resource) =>
        new(_start, _start.AddHours(1), service, resource, null, false);

    private sealed record LeakingSlot(DateTimeOffset Start, string Email);
}

internal static class SourceSlotExtensions
{
    public static SourceSlot MovedBy(this SourceSlot slot, TimeSpan shift) =>
        slot with { Start = slot.Start + shift, End = slot.End + shift };
}
