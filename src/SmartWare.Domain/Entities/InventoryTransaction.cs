using SmartWare.Domain.Enums;

namespace SmartWare.Domain.Entities;

public sealed class InventoryTransaction
{
    public long InventoryTransactionId { get; set; }
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public InventoryTransactionType Type { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string ReferenceType { get; set; } = string.Empty;
    public int ReferenceId { get; set; }
    public string PerformedById { get; set; } = string.Empty;

    public Product Product { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
}
