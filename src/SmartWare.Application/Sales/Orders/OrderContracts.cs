using SmartWare.Application.Common;
using SmartWare.Domain.Enums;

namespace SmartWare.Application.Sales.Orders;

public sealed record OrderQuery(
    string? Search,
    OrderStatus? Status,
    DateOnly? FromDate,
    DateOnly? ToDate,
    int Page = 1,
    int PageSize = 10);

public sealed record OrderListItem(
    int Id,
    string OrderNumber,
    string CustomerName,
    int TotalQuantity,
    decimal TotalAmount,
    OrderStatus Status,
    string? ExportReceiptNumber,
    DateTimeOffset OrderDate,
    string CreatedByName);

public sealed record OrderLineDetails(
    int ProductId,
    string Sku,
    string ProductName,
    string UnitOfMeasure,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record OrderReceiptLink(
    int Id,
    string ReceiptNumber,
    ReceiptStatus Status,
    DateTimeOffset CreatedAt);

public sealed record OrderDetails(
    int Id,
    string OrderNumber,
    int CustomerId,
    string CustomerCode,
    string CustomerName,
    string? CustomerPhone,
    string? CustomerAddress,
    OrderStatus Status,
    decimal TotalAmount,
    int TotalQuantity,
    DateTimeOffset OrderDate,
    DateTimeOffset? CompletedAt,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string RowVersion,
    IReadOnlyList<OrderLineDetails> Lines,
    IReadOnlyList<OrderReceiptLink> ExportReceipts);

public sealed record OrderExportRow(
    string OrderNumber,
    string CustomerCode,
    string CustomerName,
    DateTimeOffset OrderDate,
    int TotalQuantity,
    decimal TotalAmount,
    OrderStatus Status,
    string? ExportReceiptNumber,
    string CreatedByName);

public sealed record OrderStatistics(
    int TotalOrders,
    int PendingOrders,
    int ProcessingOrders,
    int ShippingOrders,
    int CompletedOrders,
    decimal CompletedRevenue);

public sealed record OrderLookupItem(int Id, string Code, string Name);

public sealed record OrderProductLookupItem(
    int Id,
    string Sku,
    string Name,
    string UnitOfMeasure,
    decimal SellingPrice,
    int AvailableQuantity);

public sealed record OrderFormOptions(
    IReadOnlyList<OrderLookupItem> Customers,
    IReadOnlyList<OrderProductLookupItem> Products);

public sealed record OrderPage(
    PagedResult<OrderListItem> Results,
    OrderStatistics Statistics);

public sealed record CreateOrderLine(int ProductId, int Quantity, decimal UnitPrice);

public sealed record CreateOrderCommand(
    int CustomerId,
    DateTimeOffset OrderDate,
    IReadOnlyList<CreateOrderLine> Lines,
    string CreatedById);

public sealed record ChangeOrderStatusCommand(
    int Id,
    string RowVersion,
    OrderStatus TargetStatus,
    string PerformedById);

public interface IOrderService
{
    Task<OrderPage> GetPageAsync(OrderQuery query, CancellationToken cancellationToken = default);
    Task<OrderDetails?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<OrderFormOptions> GetFormOptionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderExportRow>> GetExportAsync(
        OrderQuery query,
        CancellationToken cancellationToken = default);
    Task<OperationResult> CreateAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken = default);
    Task<OperationResult> ChangeStatusAsync(
        ChangeOrderStatusCommand command,
        CancellationToken cancellationToken = default);
}
