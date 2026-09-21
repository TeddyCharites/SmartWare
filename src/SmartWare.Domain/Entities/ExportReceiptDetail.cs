namespace SmartWare.Domain.Entities;

public sealed class ExportReceiptDetail
{
    public int ExportReceiptDetailId { get; set; }
    public int ExportReceiptId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }

    public ExportReceipt ExportReceipt { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
