using SmartWare.Domain.Entities;

namespace SmartWare.UnitTests.Domain;

public sealed class InventoryTests
{
    [Fact]
    public void AvailableQuantity_IsCurrentQuantityMinusReservedQuantity()
    {
        var inventory = new Inventory
        {
            CurrentQuantity = 120,
            ReservedQuantity = 35
        };

        Assert.Equal(85, inventory.AvailableQuantity);
    }

    [Fact]
    public void Reservation_ReducesAvailableButNotPhysicalQuantity()
    {
        var inventory = new Inventory
        {
            CurrentQuantity = 50,
            ReservedQuantity = 0
        };

        inventory.ReservedQuantity += 12;

        Assert.Equal(50, inventory.CurrentQuantity);
        Assert.Equal(12, inventory.ReservedQuantity);
        Assert.Equal(38, inventory.AvailableQuantity);
    }

    [Fact]
    public void CompletingExport_ReducesCurrentAndReservedTogether()
    {
        var inventory = new Inventory
        {
            CurrentQuantity = 50,
            ReservedQuantity = 12
        };

        inventory.CurrentQuantity -= 12;
        inventory.ReservedQuantity -= 12;

        Assert.Equal(38, inventory.CurrentQuantity);
        Assert.Equal(0, inventory.ReservedQuantity);
        Assert.Equal(38, inventory.AvailableQuantity);
    }
}
