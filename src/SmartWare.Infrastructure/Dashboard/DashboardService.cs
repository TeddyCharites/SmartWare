using Microsoft.EntityFrameworkCore;
using SmartWare.Application.Dashboard;
using SmartWare.Domain.Enums;
using SmartWare.Infrastructure.Data;

namespace SmartWare.Infrastructure.Dashboard;

internal sealed class DashboardService(ApplicationDbContext dbContext) : IDashboardService
{
    public async Task<DashboardSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var firstMonth = new DateTimeOffset(
            now.Year,
            now.Month,
            1,
            0,
            0,
            0,
            TimeSpan.Zero).AddMonths(-5);

        var totalProducts = await dbContext.Products
            .AsNoTracking()
            .CountAsync(product => product.IsActive, cancellationToken);
        var totalCategories = await dbContext.Categories
            .AsNoTracking()
            .CountAsync(category => category.IsActive, cancellationToken);
        var totalStock = await dbContext.Inventories
            .AsNoTracking()
            .Where(inventory => inventory.Product.IsActive && inventory.Warehouse.IsActive)
            .SumAsync(inventory => (long?)inventory.CurrentQuantity, cancellationToken);
        var pendingImports = await dbContext.ImportReceipts
            .AsNoTracking()
            .CountAsync(receipt => receipt.Status == ReceiptStatus.Pending, cancellationToken);
        var pendingExports = await dbContext.ExportReceipts
            .AsNoTracking()
            .CountAsync(receipt => receipt.Status == ReceiptStatus.Pending, cancellationToken);

        var lowStockQuery = dbContext.Products
            .AsNoTracking()
            .Where(product => product.IsActive)
            .Select(product => new
            {
                product.ProductId,
                product.Sku,
                product.Name,
                product.MinStock,
                AvailableQuantity = product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)(inventory.CurrentQuantity - inventory.ReservedQuantity)) ?? 0
            })
            .Where(product => product.AvailableQuantity <= product.MinStock)
            .OrderBy(product => product.AvailableQuantity - product.MinStock)
            .ThenBy(product => product.Name);

        var lowStockProductCount = await lowStockQuery.CountAsync(cancellationToken);
        var stockRows = await lowStockQuery
            .Take(6)
            .ToListAsync(cancellationToken);

        var stockAlerts = stockRows
            .Select(product => new StockAlertItem(
                product.ProductId,
                product.Sku,
                product.Name,
                product.AvailableQuantity,
                product.MinStock))
            .ToArray();

        var recentImports = await dbContext.ImportReceipts
            .AsNoTracking()
            .OrderByDescending(receipt => receipt.CreatedAt)
            .Take(6)
            .Select(receipt => new RecentReceiptItem(
                receipt.ImportReceiptId,
                receipt.ReceiptNumber,
                DashboardReceiptType.Import,
                receipt.Supplier.Name,
                receipt.Warehouse.Name,
                receipt.Details.Sum(detail => (int?)detail.Quantity) ?? 0,
                receipt.Status,
                receipt.CreatedAt))
            .ToListAsync(cancellationToken);

        var recentExports = await dbContext.ExportReceipts
            .AsNoTracking()
            .OrderByDescending(receipt => receipt.CreatedAt)
            .Take(6)
            .Select(receipt => new RecentReceiptItem(
                receipt.ExportReceiptId,
                receipt.ReceiptNumber,
                DashboardReceiptType.Export,
                receipt.Order == null ? "Xuất trực tiếp" : receipt.Order.Customer.Name,
                receipt.Warehouse.Name,
                receipt.Details.Sum(detail => (int?)detail.Quantity) ?? 0,
                receipt.Status,
                receipt.CreatedAt))
            .ToListAsync(cancellationToken);

        var importMovements = await dbContext.ImportReceiptDetails
            .AsNoTracking()
            .Where(detail =>
                detail.ImportReceipt.Status == ReceiptStatus.Completed &&
                detail.ImportReceipt.CompletedAt >= firstMonth)
            .GroupBy(detail => new
            {
                detail.ImportReceipt.CompletedAt!.Value.Year,
                detail.ImportReceipt.CompletedAt.Value.Month
            })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                Quantity = group.Sum(detail => detail.Quantity)
            })
            .ToListAsync(cancellationToken);

        var exportMovements = await dbContext.ExportReceiptDetails
            .AsNoTracking()
            .Where(detail =>
                detail.ExportReceipt.Status == ReceiptStatus.Completed &&
                detail.ExportReceipt.CompletedAt >= firstMonth)
            .GroupBy(detail => new
            {
                detail.ExportReceipt.CompletedAt!.Value.Year,
                detail.ExportReceipt.CompletedAt.Value.Month
            })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                Quantity = group.Sum(detail => detail.Quantity)
            })
            .ToListAsync(cancellationToken);

        var importByMonth = importMovements
            .ToDictionary(item => (item.Year, item.Month), item => item.Quantity);
        var exportByMonth = exportMovements
            .ToDictionary(item => (item.Year, item.Month), item => item.Quantity);

        var monthlyMovements = Enumerable.Range(0, 6)
            .Select(offset => firstMonth.AddMonths(offset))
            .Select(month => new MonthlyStockMovement(
                new DateOnly(month.Year, month.Month, 1),
                importByMonth.GetValueOrDefault((month.Year, month.Month)),
                exportByMonth.GetValueOrDefault((month.Year, month.Month))))
            .ToArray();

        var recentReceipts = recentImports
            .Concat(recentExports)
            .OrderByDescending(receipt => receipt.CreatedAt)
            .Take(6)
            .ToArray();

        return new DashboardSnapshot(
            totalProducts,
            totalCategories,
            totalStock ?? 0,
            lowStockProductCount,
            pendingImports + pendingExports,
            stockAlerts,
            recentReceipts,
            monthlyMovements);
    }
}
