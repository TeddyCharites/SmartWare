using SmartWare.Domain.Enums;

namespace SmartWare.Domain.Entities;

public sealed class StockReservation
{
    public int StockReservationId { get; set; }
    public int ExportReceiptId { get; set; }
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public int Quantity { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }

    public ExportReceipt ExportReceipt { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
}
