using SmartWare.Application.Common;
using SmartWare.Domain.Enums;

namespace SmartWare.Application.Warehouse.Inventory;

public sealed record InventoryQuery(
    string? Search,
    int? CategoryId,
    StockLevelStatus? Status,
    int Page = 1,
    int PageSize = 10);

public sealed record InventoryHistoryQuery(
    string? Search,
    InventoryTransactionType? Type,
    DateOnly? FromDate,
    DateOnly? ToDate,
    int Page = 1,
    int PageSize = 10);

public sealed record InventoryListItem(
    int ProductId,
    string Sku,
    string ProductName,
    string CategoryName,
    string UnitOfMeasure,
    decimal AverageCost,
    int CurrentQuantity,
    int ReservedQuantity,
    int AvailableQuantity,
    int MinimumStock,
    int MaximumStock,
    decimal InventoryValue,
    StockLevelStatus Status,
    DateTimeOffset? LastMovementAt);

public sealed record InventoryHistoryItem(
    long Id,
    DateTimeOffset OccurredAt,
    InventoryTransactionType Type,
    string Sku,
    string ProductName,
    string WarehouseName,
    int Quantity,
    decimal UnitCost,
    decimal TotalValue,
    string ReferenceType,
    string ReferenceNumber,
    string PerformedByName);

public sealed record InventoryStatistics(
    int TotalProducts,
    long CurrentQuantity,
    long ReservedQuantity,
    long AvailableQuantity,
    decimal InventoryValue);

public sealed record InventoryCategoryFilter(int Id, string Name);

public sealed record SlowMovingInventoryItem(
    int ProductId,
    string Sku,
    string ProductName,
    int CurrentQuantity,
    DateTimeOffset? LastMovementAt,
    int DaysWithoutMovement);

public sealed record InventoryOverviewPage(
    PagedResult<InventoryListItem> Results,
    InventoryStatistics Statistics,
    IReadOnlyList<InventoryCategoryFilter> Categories,
    IReadOnlyList<SlowMovingInventoryItem> SlowMovingItems);

public sealed record InventoryHistoryPage(
    PagedResult<InventoryHistoryItem> Results,
    InventoryStatistics Statistics);

public interface IInventoryQueryService
{
    Task<InventoryOverviewPage> GetOverviewAsync(
        InventoryQuery query,
        CancellationToken cancellationToken = default);
    Task<InventoryHistoryPage> GetHistoryAsync(
        InventoryHistoryQuery query,
        CancellationToken cancellationToken = default);
}
