namespace SmartWare.Domain.Entities;

public sealed class ImportReceiptDetail
{
    public int ImportReceiptDetailId { get; set; }
    public int ImportReceiptId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }

    public ImportReceipt ImportReceipt { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
