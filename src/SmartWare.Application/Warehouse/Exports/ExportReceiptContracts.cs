using SmartWare.Application.Common;
using SmartWare.Domain.Enums;

namespace SmartWare.Application.Warehouse.Exports;

public sealed record ExportReceiptQuery(
    string? Search,
    int? CustomerId,
    ReceiptStatus? Status,
    DateOnly? FromDate,
    DateOnly? ToDate,
    int Page = 1,
    int PageSize = 10);

public sealed record ExportReceiptListItem(
    int Id,
    string ReceiptNumber,
    string? OrderNumber,
    string? CustomerName,
    string WarehouseName,
    string CreatedByName,
    int TotalQuantity,
    decimal TotalValue,
    ReceiptStatus Status,
    DateTimeOffset CreatedAt);

public sealed record ExportReceiptLineDetails(
    int ProductId,
    string Sku,
    string ProductName,
    string UnitOfMeasure,
    int Quantity,
    decimal UnitCost,
    decimal LineTotal);

public sealed record ExportReceiptDetails(
    int Id,
    string ReceiptNumber,
    int? OrderId,
    string? OrderNumber,
    string? CustomerName,
    int WarehouseId,
    string WarehouseName,
    ReceiptStatus Status,
    string CreatedByName,
    string? ApprovedByName,
    string? CompletedByName,
    string? RejectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? CompletedAt,
    int TotalQuantity,
    decimal TotalValue,
    string RowVersion,
    IReadOnlyList<ExportReceiptLineDetails> Lines);

public sealed record ExportReceiptStatistics(
    int TotalReceipts,
    int PendingReceipts,
    int ApprovedReceipts,
    int CompletedReceipts,
    decimal CompletedValue);

public sealed record ExportLookupItem(int Id, string Code, string Name);

public sealed record ExportProductLookupItem(
    int Id,
    string Code,
    string Name,
    int CurrentQuantity,
    int ReservedQuantity,
    int AvailableQuantity,
    decimal AverageCost);

public sealed record ExportOrderLookupItem(
    int Id,
    string OrderNumber,
    string CustomerName,
    OrderStatus Status);

public sealed record ExportReceiptFormOptions(
    IReadOnlyList<ExportLookupItem> Warehouses,
    IReadOnlyList<ExportProductLookupItem> Products,
    IReadOnlyList<ExportOrderLookupItem> Orders);

public sealed record ExportReceiptPage(
    PagedResult<ExportReceiptListItem> Results,
    ExportReceiptStatistics Statistics,
    IReadOnlyList<ExportLookupItem> Customers);

public sealed record CreateExportReceiptLine(int ProductId, int Quantity);

public sealed record CreateExportReceiptCommand(
    int? OrderId,
    int WarehouseId,
    IReadOnlyList<CreateExportReceiptLine> Lines,
    string CreatedById);

public sealed record ReviewExportReceiptCommand(
    int Id,
    string RowVersion,
    bool Approve,
    string? RejectionReason,
    string ReviewedById);

public sealed record CompleteExportReceiptCommand(
    int Id,
    string RowVersion,
    string CompletedById);

public interface IExportReceiptService
{
    Task<ExportReceiptPage> GetPageAsync(
        ExportReceiptQuery query,
        CancellationToken cancellationToken = default);
    Task<ExportReceiptDetails?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ExportReceiptFormOptions> GetFormOptionsAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default);
    Task<OperationResult> CreateAsync(
        CreateExportReceiptCommand command,
        CancellationToken cancellationToken = default);
    Task<OperationResult> ReviewAsync(
        ReviewExportReceiptCommand command,
        CancellationToken cancellationToken = default);
    Task<OperationResult> CompleteAsync(
        CompleteExportReceiptCommand command,
        CancellationToken cancellationToken = default);
}
