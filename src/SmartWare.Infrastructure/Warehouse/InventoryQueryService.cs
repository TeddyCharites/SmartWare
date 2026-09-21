using Microsoft.EntityFrameworkCore;
using SmartWare.Application.Common;
using SmartWare.Application.Warehouse.Inventory;
using SmartWare.Domain.Entities;
using SmartWare.Domain.Enums;
using SmartWare.Domain.Services;
using SmartWare.Infrastructure.Data;

namespace SmartWare.Infrastructure.InventoryOperations;

internal sealed class InventoryQueryService(ApplicationDbContext dbContext) : IInventoryQueryService
{
    public async Task<InventoryOverviewPage> GetOverviewAsync(
        InventoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var source = ApplyOverviewFilters(query);
        var totalCount = await source.CountAsync(cancellationToken);
        var pageSize = Math.Clamp(query.PageSize, 5, 100);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Clamp(query.Page, 1, totalPages);

        var rows = await source
            .OrderBy(product => product.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(product => new InventoryRow(
                product.ProductId,
                product.Sku,
                product.Name,
                product.Category.Name,
                product.UnitOfMeasure,
                product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)inventory.CurrentQuantity) ?? 0,
                product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)inventory.ReservedQuantity) ?? 0,
                product.MinStock,
                product.MaxStock,
                product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (decimal?)(inventory.CurrentQuantity * inventory.AverageCost)) ?? 0,
                product.InventoryTransactions.Max(transaction =>
                    (DateTimeOffset?)transaction.OccurredAt)))
            .ToListAsync(cancellationToken);

        var items = rows.Select(row =>
        {
            var available = row.CurrentQuantity - row.ReservedQuantity;
            var averageCost = row.CurrentQuantity == 0
                ? 0
                : decimal.Round(row.InventoryValue / row.CurrentQuantity, 4);
            return new InventoryListItem(
                row.ProductId,
                row.Sku,
                row.ProductName,
                row.CategoryName,
                row.UnitOfMeasure,
                averageCost,
                row.CurrentQuantity,
                row.ReservedQuantity,
                available,
                row.MinimumStock,
                row.MaximumStock,
                row.InventoryValue,
                StockLevelClassifier.Classify(
                    row.CurrentQuantity,
                    row.ReservedQuantity,
                    row.MinimumStock,
                    row.MaximumStock),
                row.LastMovementAt);
        }).ToArray();

        var categories = await dbContext.Categories
            .AsNoTracking()
            .OrderBy(category => category.Name)
            .Select(category => new InventoryCategoryFilter(category.CategoryId, category.Name))
            .ToListAsync(cancellationToken);

        return new InventoryOverviewPage(
            new PagedResult<InventoryListItem>(items, page, pageSize, totalCount),
            await GetStatisticsAsync(cancellationToken),
            categories,
            await GetSlowMovingItemsAsync(cancellationToken));
    }

    public async Task<InventoryHistoryPage> GetHistoryAsync(
        InventoryHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var source = ApplyHistoryFilters(query);
        var totalCount = await source.CountAsync(cancellationToken);
        var pageSize = Math.Clamp(query.PageSize, 5, 100);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Clamp(query.Page, 1, totalPages);

        var rows = await source
            .OrderByDescending(transaction => transaction.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(transaction => new InventoryHistoryRow(
                transaction.InventoryTransactionId,
                transaction.OccurredAt,
                transaction.Type,
                transaction.Product.Sku,
                transaction.Product.Name,
                transaction.Warehouse.Name,
                transaction.Quantity,
                transaction.UnitCost,
                transaction.Quantity * transaction.UnitCost,
                transaction.ReferenceType,
                transaction.ReferenceId,
                transaction.ReferenceType == nameof(ImportReceipt)
                    ? dbContext.ImportReceipts
                        .Where(receipt => receipt.ImportReceiptId == transaction.ReferenceId)
                        .Select(receipt => receipt.ReceiptNumber)
                        .FirstOrDefault()
                    : transaction.ReferenceType == nameof(ExportReceipt)
                        ? dbContext.ExportReceipts
                            .Where(receipt => receipt.ExportReceiptId == transaction.ReferenceId)
                            .Select(receipt => receipt.ReceiptNumber)
                            .FirstOrDefault()
                        : null,
                dbContext.Users
                    .Where(user => user.Id == transaction.PerformedById)
                    .Select(user => user.FullName)
                    .Single()))
            .ToListAsync(cancellationToken);

        var items = rows.Select(row => new InventoryHistoryItem(
            row.Id,
            row.OccurredAt,
            row.Type,
            row.Sku,
            row.ProductName,
            row.WarehouseName,
            row.Quantity,
            row.UnitCost,
            row.TotalValue,
            row.ReferenceType,
            row.ReferenceNumber ?? $"{row.ReferenceType} #{row.ReferenceId}",
            row.PerformedByName)).ToArray();

        return new InventoryHistoryPage(
            new PagedResult<InventoryHistoryItem>(items, page, pageSize, totalCount),
            await GetStatisticsAsync(cancellationToken));
    }

    private IQueryable<Product> ApplyOverviewFilters(InventoryQuery query)
    {
        var source = dbContext.Products.AsNoTracking().Where(product => product.IsActive);
        var search = query.Search?.Trim();

        if (!string.IsNullOrWhiteSpace(search))
        {
            source = source.Where(product =>
                product.Sku.Contains(search) ||
                product.Name.Contains(search) ||
                product.Category.Name.Contains(search));
        }

        if (query.CategoryId.HasValue)
        {
            source = source.Where(product => product.CategoryId == query.CategoryId.Value);
        }

        if (query.Status.HasValue)
        {
            source = ApplyStatusFilter(source, query.Status.Value);
        }

        return source;
    }

    private IQueryable<InventoryTransaction> ApplyHistoryFilters(InventoryHistoryQuery query)
    {
        var source = dbContext.InventoryTransactions.AsNoTracking();
        var search = query.Search?.Trim();

        if (!string.IsNullOrWhiteSpace(search))
        {
            source = source.Where(transaction =>
                transaction.Product.Sku.Contains(search) ||
                transaction.Product.Name.Contains(search) ||
                (transaction.ReferenceType == nameof(ImportReceipt) &&
                    dbContext.ImportReceipts.Any(receipt =>
                        receipt.ImportReceiptId == transaction.ReferenceId &&
                        receipt.ReceiptNumber.Contains(search))) ||
                (transaction.ReferenceType == nameof(ExportReceipt) &&
                    dbContext.ExportReceipts.Any(receipt =>
                        receipt.ExportReceiptId == transaction.ReferenceId &&
                        receipt.ReceiptNumber.Contains(search))));
        }

        if (query.Type.HasValue)
        {
            source = source.Where(transaction => transaction.Type == query.Type.Value);
        }

        if (query.FromDate.HasValue)
        {
            var from = new DateTimeOffset(
                query.FromDate.Value.ToDateTime(TimeOnly.MinValue),
                TimeSpan.Zero);
            source = source.Where(transaction => transaction.OccurredAt >= from);
        }

        if (query.ToDate.HasValue)
        {
            var toExclusive = new DateTimeOffset(
                query.ToDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue),
                TimeSpan.Zero);
            source = source.Where(transaction => transaction.OccurredAt < toExclusive);
        }

        return source;
    }

    private static IQueryable<Product> ApplyStatusFilter(
        IQueryable<Product> source,
        StockLevelStatus status)
    {
        return status switch
        {
            StockLevelStatus.Out => source.Where(product =>
                (product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)(inventory.CurrentQuantity - inventory.ReservedQuantity)) ?? 0) <= 0),
            StockLevelStatus.Low => source.Where(product =>
                (product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)(inventory.CurrentQuantity - inventory.ReservedQuantity)) ?? 0) > 0 &&
                (product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)(inventory.CurrentQuantity - inventory.ReservedQuantity)) ?? 0) <= product.MinStock),
            StockLevelStatus.Excess => source.Where(product =>
                (product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)inventory.CurrentQuantity) ?? 0) > product.MaxStock &&
                product.MaxStock > 0 &&
                (product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)(inventory.CurrentQuantity - inventory.ReservedQuantity)) ?? 0) > product.MinStock),
            _ => source.Where(product =>
                (product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)(inventory.CurrentQuantity - inventory.ReservedQuantity)) ?? 0) > product.MinStock &&
                (product.MaxStock <= 0 ||
                    (product.Inventories
                        .Where(inventory => inventory.Warehouse.IsActive)
                        .Sum(inventory => (int?)inventory.CurrentQuantity) ?? 0) <= product.MaxStock))
        };
    }

    private async Task<InventoryStatistics> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        var activeInventories = dbContext.Inventories
            .Where(inventory => inventory.Product.IsActive && inventory.Warehouse.IsActive);
        return new InventoryStatistics(
            await dbContext.Products.CountAsync(product => product.IsActive, cancellationToken),
            await activeInventories.SumAsync(
                inventory => (long?)inventory.CurrentQuantity,
                cancellationToken) ?? 0,
            await activeInventories.SumAsync(
                inventory => (long?)inventory.ReservedQuantity,
                cancellationToken) ?? 0,
            await activeInventories.SumAsync(
                inventory => (long?)(inventory.CurrentQuantity - inventory.ReservedQuantity),
                cancellationToken) ?? 0,
            await activeInventories.SumAsync(
                inventory => (decimal?)(inventory.CurrentQuantity * inventory.AverageCost),
                cancellationToken) ?? 0);
    }

    private async Task<IReadOnlyList<SlowMovingInventoryItem>> GetSlowMovingItemsAsync(
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Products
            .AsNoTracking()
            .Where(product =>
                product.IsActive &&
                (product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)inventory.CurrentQuantity) ?? 0) > 0)
            .Select(product => new
            {
                product.ProductId,
                product.Sku,
                ProductName = product.Name,
                CurrentQuantity = product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)inventory.CurrentQuantity) ?? 0,
                LastMovementAt = product.InventoryTransactions.Max(transaction =>
                    (DateTimeOffset?)transaction.OccurredAt),
                ProductCreatedAt = product.CreatedAt
            })
            .OrderBy(row => row.LastMovementAt ?? row.ProductCreatedAt)
            .Take(5)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        return rows.Select(row =>
        {
            var baseline = row.LastMovementAt ?? row.ProductCreatedAt;
            return new SlowMovingInventoryItem(
                row.ProductId,
                row.Sku,
                row.ProductName,
                row.CurrentQuantity,
                row.LastMovementAt,
                Math.Max(0, (int)(now - baseline).TotalDays));
        }).ToArray();
    }

    private sealed record InventoryRow(
        int ProductId,
        string Sku,
        string ProductName,
        string CategoryName,
        string UnitOfMeasure,
        int CurrentQuantity,
        int ReservedQuantity,
        int MinimumStock,
        int MaximumStock,
        decimal InventoryValue,
        DateTimeOffset? LastMovementAt);

    private sealed record InventoryHistoryRow(
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
        int ReferenceId,
        string? ReferenceNumber,
        string PerformedByName);

}
