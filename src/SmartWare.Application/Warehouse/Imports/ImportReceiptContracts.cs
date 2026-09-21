using SmartWare.Application.Common;
using SmartWare.Domain.Enums;

namespace SmartWare.Application.Warehouse.Imports;

public sealed record ImportReceiptQuery(
    string? Search,
    int? SupplierId,
    ReceiptStatus? Status,
    DateOnly? FromDate,
    DateOnly? ToDate,
    int Page = 1,
    int PageSize = 10);

public sealed record ImportReceiptListItem(
    int Id,
    string ReceiptNumber,
    string SupplierName,
    string WarehouseName,
    string CreatedByName,
    int TotalQuantity,
    decimal TotalValue,
    ReceiptStatus Status,
    DateTimeOffset CreatedAt);

public sealed record ImportReceiptLineDetails(
    int ProductId,
    string Sku,
    string ProductName,
    string UnitOfMeasure,
    int Quantity,
    decimal UnitCost,
    decimal LineTotal);

public sealed record ImportReceiptDetails(
    int Id,
    string ReceiptNumber,
    int SupplierId,
    string SupplierName,
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
    IReadOnlyList<ImportReceiptLineDetails> Lines);

public sealed record ImportReceiptStatistics(
    int TotalReceipts,
    int PendingReceipts,
    int ApprovedReceipts,
    int CompletedReceipts,
    decimal CompletedValue);

public sealed record ImportLookupItem(int Id, string Code, string Name);

public sealed record ImportProductLookupItem(int Id, string Code, string Name, int SupplierId);

public sealed record ImportReceiptFormOptions(
    IReadOnlyList<ImportLookupItem> Suppliers,
    IReadOnlyList<ImportLookupItem> Warehouses,
    IReadOnlyList<ImportProductLookupItem> Products);

public sealed record ImportReceiptPage(
    PagedResult<ImportReceiptListItem> Results,
    ImportReceiptStatistics Statistics,
    IReadOnlyList<ImportLookupItem> Suppliers);

public sealed record CreateImportReceiptLine(int ProductId, int Quantity, decimal UnitCost);

public sealed record CreateImportReceiptCommand(
    int SupplierId,
    int WarehouseId,
    IReadOnlyList<CreateImportReceiptLine> Lines,
    string CreatedById);

public sealed record ReviewImportReceiptCommand(
    int Id,
    string RowVersion,
    bool Approve,
    string? RejectionReason,
    string ReviewedById);

public sealed record CompleteImportReceiptCommand(
    int Id,
    string RowVersion,
    string CompletedById);

public interface IImportReceiptService
{
    Task<ImportReceiptPage> GetPageAsync(
        ImportReceiptQuery query,
        CancellationToken cancellationToken = default);
    Task<ImportReceiptDetails?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ImportReceiptFormOptions> GetFormOptionsAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> CreateAsync(
        CreateImportReceiptCommand command,
        CancellationToken cancellationToken = default);
    Task<OperationResult> ReviewAsync(
        ReviewImportReceiptCommand command,
        CancellationToken cancellationToken = default);
    Task<OperationResult> CompleteAsync(
        CompleteImportReceiptCommand command,
        CancellationToken cancellationToken = default);
}
