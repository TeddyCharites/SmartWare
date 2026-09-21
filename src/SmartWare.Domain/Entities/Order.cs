using SmartWare.Domain.Enums;

namespace SmartWare.Domain.Entities;

public sealed class Order
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal TotalAmount { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string CreatedById { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Customer Customer { get; set; } = null!;
    public ICollection<OrderDetail> Details { get; set; } = new List<OrderDetail>();
    public ICollection<ExportReceipt> ExportReceipts { get; set; } = new List<ExportReceipt>();
}
