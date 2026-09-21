using SmartWare.Domain.Enums;

namespace SmartWare.Domain.Entities;

public sealed class ExportReceipt
{
    public int ExportReceiptId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public int WarehouseId { get; set; }
    public ReceiptStatus Status { get; set; } = ReceiptStatus.Pending;
    public string CreatedById { get; set; } = string.Empty;
    public string? ApprovedById { get; set; }
    public string? CompletedById { get; set; }
    public string? RejectionReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Order? Order { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public ICollection<ExportReceiptDetail> Details { get; set; } = new List<ExportReceiptDetail>();
    public ICollection<StockReservation> Reservations { get; set; } = new List<StockReservation>();
}
