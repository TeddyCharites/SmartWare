using System.ComponentModel.DataAnnotations.Schema;

namespace SmartWare.Domain.Entities;

public sealed class Inventory
{
    public int InventoryId { get; set; }
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public int CurrentQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public decimal AverageCost { get; set; }
    public byte[] RowVersion { get; set; } = [];

    [NotMapped]
    public int AvailableQuantity => CurrentQuantity - ReservedQuantity;

    public Product Product { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
}
