using Microsoft.EntityFrameworkCore;
using SmartWare.Application.Reports;
using SmartWare.Domain.Enums;
using SmartWare.Domain.Services;
using SmartWare.Infrastructure.Data;

namespace SmartWare.Infrastructure.Reports;

internal sealed class ReportService(ApplicationDbContext dbContext) : IReportService
{
    private const int ForecastHorizonDays = 30;

    public async Task<WarehouseReport> GetAsync(
        ReportQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.FromDate > query.ToDate)
        {
            throw new ArgumentException("Ngày bắt đầu không được sau ngày kết thúc.", nameof(query));
        }

        var dayCount = query.ToDate.DayNumber - query.FromDate.DayNumber + 1;
        if (dayCount > 366)
        {
            throw new ArgumentException("Khoảng báo cáo không được vượt quá 366 ngày.", nameof(query));
        }

        var from = ToUtcBoundary(query.FromDate);
        var toExclusive = ToUtcBoundary(query.ToDate.AddDays(1));
        var transactions = dbContext.InventoryTransactions
            .AsNoTracking()
            .Where(item => item.OccurredAt >= from && item.OccurredAt < toExclusive);

        var movementData = await transactions
            .GroupBy(item => item.OccurredAt.Date)
            .Select(group => new
            {
                Date = group.Key,
                ImportQuantity = group
                    .Where(item => item.Type == InventoryTransactionType.In)
                    .Sum(item => (int?)item.Quantity) ?? 0,
                ExportQuantity = group
                    .Where(item => item.Type == InventoryTransactionType.Out)
                    .Sum(item => (int?)item.Quantity) ?? 0,
                ImportValue = group
                    .Where(item => item.Type == InventoryTransactionType.In)
                    .Sum(item => (decimal?)(item.Quantity * item.UnitCost)) ?? 0,
                ExportCost = group
                    .Where(item => item.Type == InventoryTransactionType.Out)
                    .Sum(item => (decimal?)(item.Quantity * item.UnitCost)) ?? 0
            })
            .ToListAsync(cancellationToken);
        var movementLookup = movementData.ToDictionary(item => DateOnly.FromDateTime(item.Date));
        var movements = Enumerable.Range(0, dayCount)
            .Select(offset => query.FromDate.AddDays(offset))
            .Select(date => movementLookup.TryGetValue(date, out var item)
                ? new DailyMovement(
                    date,
                    item.ImportQuantity,
                    item.ExportQuantity,
                    item.ImportValue,
                    item.ExportCost)
                : new DailyMovement(date, 0, 0, 0, 0))
            .ToArray();

        var revenue = await dbContext.Orders
            .AsNoTracking()
            .Where(order =>
                order.Status == OrderStatus.Completed &&
                order.CompletedAt >= from &&
                order.CompletedAt < toExclusive)
            .SumAsync(order => (decimal?)order.TotalAmount, cancellationToken) ?? 0;
        var inventoryValue = await dbContext.Inventories
            .AsNoTracking()
            .Where(item => item.Product.IsActive && item.Warehouse.IsActive)
            .SumAsync(item => (decimal?)(item.CurrentQuantity * item.AverageCost), cancellationToken) ?? 0;

        var categoryData = await dbContext.Inventories
            .AsNoTracking()
            .Where(item => item.Product.IsActive && item.Warehouse.IsActive)
            .GroupBy(item => item.Product.Category.Name)
            .Select(group => new
            {
                CategoryName = group.Key,
                AvailableQuantity = group.Sum(item => item.CurrentQuantity - item.ReservedQuantity),
                InventoryValue = group.Sum(item => item.CurrentQuantity * item.AverageCost)
            })
            .ToListAsync(cancellationToken);
        var categories = categoryData
            .Select(item => new CategoryInventory(
                item.CategoryName,
                item.AvailableQuantity,
                item.InventoryValue))
            .OrderByDescending(item => item.InventoryValue)
            .ToArray();

        var productData = await dbContext.Products
            .AsNoTracking()
            .Where(product => product.IsActive)
            .Select(product => new
            {
                product.ProductId,
                product.Sku,
                product.Name,
                CategoryName = product.Category.Name,
                product.UnitOfMeasure,
                product.MinStock,
                product.MaxStock,
                ImportQuantity = product.InventoryTransactions
                    .Where(item =>
                        item.Type == InventoryTransactionType.In &&
                        item.OccurredAt >= from &&
                        item.OccurredAt < toExclusive)
                    .Sum(item => (int?)item.Quantity) ?? 0,
                ExportQuantity = product.InventoryTransactions
                    .Where(item =>
                        item.Type == InventoryTransactionType.Out &&
                        item.OccurredAt >= from &&
                        item.OccurredAt < toExclusive)
                    .Sum(item => (int?)item.Quantity) ?? 0,
                CurrentQuantity = product.Inventories
                    .Where(item => item.Warehouse.IsActive)
                    .Sum(item => (int?)item.CurrentQuantity) ?? 0,
                ReservedQuantity = product.Inventories
                    .Where(item => item.Warehouse.IsActive)
                    .Sum(item => (int?)item.ReservedQuantity) ?? 0,
                InventoryValue = product.Inventories
                    .Where(item => item.Warehouse.IsActive)
                    .Sum(item => (decimal?)(item.CurrentQuantity * item.AverageCost)) ?? 0,
                Revenue = product.OrderDetails
                    .Where(detail =>
                        detail.Order.Status == OrderStatus.Completed &&
                        detail.Order.CompletedAt >= from &&
                        detail.Order.CompletedAt < toExclusive)
                    .Sum(detail => (decimal?)(detail.Quantity * detail.UnitPrice)) ?? 0
            })
            .ToListAsync(cancellationToken);
        var products = productData
            .Select(item => new ProductReportItem(
                item.ProductId,
                item.Sku,
                item.Name,
                item.CategoryName,
                item.UnitOfMeasure,
                item.ImportQuantity,
                item.ExportQuantity,
                item.CurrentQuantity,
                item.ReservedQuantity,
                item.CurrentQuantity - item.ReservedQuantity,
                item.CurrentQuantity == 0 ? 0 : item.InventoryValue / item.CurrentQuantity,
                item.InventoryValue,
                item.Revenue))
            .OrderByDescending(item => item.ExportQuantity)
            .ThenBy(item => item.ProductName)
            .ToArray();

        var supplierData = await dbContext.Suppliers
            .AsNoTracking()
            .Select(supplier => new SupplierReportItem(
                supplier.SupplierId,
                supplier.Code,
                supplier.Name,
                supplier.ImportReceipts.Count(receipt =>
                    receipt.Status == ReceiptStatus.Completed &&
                    receipt.CompletedAt >= from &&
                    receipt.CompletedAt < toExclusive),
                supplier.ImportReceipts
                    .Where(receipt =>
                        receipt.Status == ReceiptStatus.Completed &&
                        receipt.CompletedAt >= from &&
                        receipt.CompletedAt < toExclusive)
                    .SelectMany(receipt => receipt.Details)
                    .Sum(detail => (int?)detail.Quantity) ?? 0,
                supplier.ImportReceipts
                    .Where(receipt =>
                        receipt.Status == ReceiptStatus.Completed &&
                        receipt.CompletedAt >= from &&
                        receipt.CompletedAt < toExclusive)
                    .SelectMany(receipt => receipt.Details)
                    .Sum(detail => (decimal?)(detail.Quantity * detail.UnitCost)) ?? 0,
                supplier.ImportReceipts
                    .Where(receipt =>
                        receipt.Status == ReceiptStatus.Completed &&
                        receipt.CompletedAt >= from &&
                        receipt.CompletedAt < toExclusive)
                    .Max(receipt => receipt.CompletedAt)))
            .ToListAsync(cancellationToken);
        var suppliers = supplierData
            .Where(item => item.ReceiptCount > 0)
            .OrderByDescending(item => item.ImportValue)
            .ToArray();

        var customerData = await dbContext.Customers
            .AsNoTracking()
            .Select(customer => new CustomerReportItem(
                customer.CustomerId,
                customer.Code,
                customer.Name,
                customer.Orders.Count(order =>
                    order.Status == OrderStatus.Completed &&
                    order.CompletedAt >= from &&
                    order.CompletedAt < toExclusive),
                customer.Orders
                    .Where(order =>
                        order.Status == OrderStatus.Completed &&
                        order.CompletedAt >= from &&
                        order.CompletedAt < toExclusive)
                    .SelectMany(order => order.Details)
                    .Sum(detail => (int?)detail.Quantity) ?? 0,
                customer.Orders
                    .Where(order =>
                        order.Status == OrderStatus.Completed &&
                        order.CompletedAt >= from &&
                        order.CompletedAt < toExclusive)
                    .Sum(order => (decimal?)order.TotalAmount) ?? 0,
                customer.Orders
                    .Where(order =>
                        order.Status == OrderStatus.Completed &&
                        order.CompletedAt >= from &&
                        order.CompletedAt < toExclusive)
                    .Max(order => order.CompletedAt)))
            .ToListAsync(cancellationToken);
        var customers = customerData
            .Where(item => item.CompletedOrderCount > 0)
            .OrderByDescending(item => item.Revenue)
            .ToArray();

        var dailyProductDemand = await transactions
            .Where(item => item.Type == InventoryTransactionType.Out)
            .GroupBy(item => new { item.ProductId, Date = item.OccurredAt.Date })
            .Select(group => new
            {
                group.Key.ProductId,
                Date = DateOnly.FromDateTime(group.Key.Date),
                Quantity = group.Sum(item => item.Quantity)
            })
            .ToListAsync(cancellationToken);
        var demandLookup = dailyProductDemand
            .GroupBy(item => item.ProductId)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(item => item.Date, item => item.Quantity));
        var forecasts = productData
            .Select(item => BuildForecast(item.ProductId, item.Sku, item.Name,
                item.MinStock, item.MaxStock,
                item.CurrentQuantity - item.ReservedQuantity,
                query.FromDate, dayCount, demandLookup))
            .OrderBy(item => RiskOrder(item.RiskLevel))
            .ThenByDescending(item => item.SuggestedReplenishment)
            .ThenBy(item => item.ProductName)
            .ToArray();

        var importQuantity = movements.Sum(item => (long)item.ImportQuantity);
        var exportQuantity = movements.Sum(item => (long)item.ExportQuantity);
        var importValue = movements.Sum(item => item.ImportValue);
        var costOfGoodsSold = movements.Sum(item => item.ExportCost);
        var kpis = new ReportKpis(
            importQuantity,
            exportQuantity,
            importValue,
            revenue,
            costOfGoodsSold,
            revenue - costOfGoodsSold,
            inventoryValue);

        return new WarehouseReport(
            new ReportPeriod(query.FromDate, query.ToDate, dayCount),
            kpis,
            movements,
            categories,
            products,
            suppliers,
            customers,
            forecasts);
    }

    private static DemandForecastItem BuildForecast(
        int productId,
        string sku,
        string productName,
        int minimumStock,
        int maximumStock,
        int availableQuantity,
        DateOnly fromDate,
        int dayCount,
        IReadOnlyDictionary<int, Dictionary<DateOnly, int>> demandLookup)
    {
        demandLookup.TryGetValue(productId, out var productDemand);
        var history = Enumerable.Range(0, dayCount)
            .Select(offset => fromDate.AddDays(offset))
            .Select(date => productDemand?.GetValueOrDefault(date) ?? 0)
            .ToArray();
        var result = DemandForecastCalculator.Calculate(history, ForecastHorizonDays);
        double? daysUntilMinimum = result.AverageDailyDemand <= 0
            ? null
            : Math.Max(0, (availableQuantity - minimumStock) / result.AverageDailyDemand);
        double? daysUntilOut = result.AverageDailyDemand <= 0
            ? null
            : Math.Max(0, availableQuantity / result.AverageDailyDemand);
        var forecastQuantity = (int)Math.Ceiling(result.ForecastQuantity);
        var targetStock = Math.Max(maximumStock, forecastQuantity + minimumStock);
        var suggested = result.HasHistory || availableQuantity <= minimumStock
            ? Math.Max(0, targetStock - availableQuantity)
            : 0;
        var risk = availableQuantity <= 0
            ? "Hết hàng"
            : daysUntilOut <= 7
                ? "Nghiêm trọng"
                : availableQuantity <= minimumStock || daysUntilMinimum <= 7
                    ? "Cao"
                    : daysUntilMinimum <= 30
                        ? "Trung bình"
                        : "Thấp";

        return new DemandForecastItem(
            productId,
            sku,
            productName,
            availableQuantity,
            minimumStock,
            maximumStock,
            result.AverageDailyDemand,
            forecastQuantity,
            daysUntilMinimum,
            daysUntilOut,
            suggested,
            risk,
            result.Mae,
            result.Rmse,
            result.Mape,
            result.HasHistory);
    }

    private static int RiskOrder(string riskLevel) => riskLevel switch
    {
        "Hết hàng" => 0,
        "Nghiêm trọng" => 1,
        "Cao" => 2,
        "Trung bình" => 3,
        _ => 4
    };

    private static DateTimeOffset ToUtcBoundary(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime();
    }
}
