using FluentAssertions;
using HouseFlow.Application.Common;

namespace HouseFlow.UnitTests.Common;

public class DeviceChipsTests
{
    private static readonly DateTime T0 = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TypesInCreationOrder_KeepsOnePerDevice_OldestFirst()
    {
        var chips = DeviceChips.TypesInCreationOrder(
        [
            (T0.AddMinutes(2), Guid.NewGuid(), "VMC"),
            (T0, Guid.NewGuid(), "Chaudière Gaz"),
            (T0.AddMinutes(1), Guid.NewGuid(), "Détecteur de fumée"),
            (T0.AddMinutes(3), Guid.NewGuid(), "Détecteur de fumée"),
        ]);

        chips.Should().Equal("Chaudière Gaz", "Détecteur de fumée", "VMC", "Détecteur de fumée");
    }

    [Fact]
    public void TypesInCreationOrder_SameTimestamp_OrdersById()
    {
        var first = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000002");

        DeviceChips.TypesInCreationOrder([(T0, second, "B"), (T0, first, "A")]).Should().Equal("A", "B");
    }

    [Fact]
    public void TypesInCreationOrder_NoDevice_IsEmpty() =>
        DeviceChips.TypesInCreationOrder([]).Should().BeEmpty();
}
