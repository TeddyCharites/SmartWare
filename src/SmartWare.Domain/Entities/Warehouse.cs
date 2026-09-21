namespace SmartWare.Domain.Entities;

public sealed class Warehouse
{
    public int WarehouseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<Inventory> Inventories { get; set; } = new List<Inventory>();
    public ICollection<ImportReceipt> ImportReceipts { get; set; } = new List<ImportReceipt>();
    public ICollection<ExportReceipt> ExportReceipts { get; set; } = new List<ExportReceipt>();
    public ICollection<StockReservation> StockReservations { get; set; } = new List<StockReservation>();
    public ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();
}
