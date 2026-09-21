using SmartWare.Domain.Enums;

namespace SmartWare.Domain.Entities;

public sealed class ImportReceipt
{
    public int ImportReceiptId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public int SupplierId { get; set; }
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

    public Supplier Supplier { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public ICollection<ImportReceiptDetail> Details { get; set; } = new List<ImportReceiptDetail>();
}
