namespace SmartWare.Application.Reports;

public sealed record ReportQuery(DateOnly FromDate, DateOnly ToDate);

public sealed record ReportPeriod(DateOnly FromDate, DateOnly ToDate, int DayCount);

public sealed record ReportKpis(
    long ImportQuantity,
    long ExportQuantity,
    decimal ImportValue,
    decimal Revenue,
    decimal CostOfGoodsSold,
    decimal GrossProfit,
    decimal InventoryValue);

public sealed record DailyMovement(
    DateOnly Date,
    int ImportQuantity,
    int ExportQuantity,
    decimal ImportValue,
    decimal ExportCost);

public sealed record CategoryInventory(
    string CategoryName,
    int AvailableQuantity,
    decimal InventoryValue);

public sealed record ProductReportItem(
    int ProductId,
    string Sku,
    string ProductName,
    string CategoryName,
    string UnitOfMeasure,
    int ImportQuantity,
    int ExportQuantity,
    int CurrentQuantity,
    int ReservedQuantity,
    int AvailableQuantity,
    decimal AverageCost,
    decimal InventoryValue,
    decimal Revenue);

public sealed record SupplierReportItem(
    int SupplierId,
    string SupplierCode,
    string SupplierName,
    int ReceiptCount,
    int ImportQuantity,
    decimal ImportValue,
    DateTimeOffset? LastReceiptAt);

public sealed record CustomerReportItem(
    int CustomerId,
    string CustomerCode,
    string CustomerName,
    int CompletedOrderCount,
    int PurchasedQuantity,
    decimal Revenue,
    DateTimeOffset? LastOrderAt);

public sealed record DemandForecastItem(
    int ProductId,
    string Sku,
    string ProductName,
    int AvailableQuantity,
    int MinimumStock,
    int MaximumStock,
    double AverageDailyDemand,
    int ForecastThirtyDays,
    double? DaysUntilMinimum,
    double? DaysUntilOutOfStock,
    int SuggestedReplenishment,
    string RiskLevel,
    double? Mae,
    double? Rmse,
    double? Mape,
    bool HasHistory);

public sealed record WarehouseReport(
    ReportPeriod Period,
    ReportKpis Kpis,
    IReadOnlyList<DailyMovement> Movements,
    IReadOnlyList<CategoryInventory> CategoryInventory,
    IReadOnlyList<ProductReportItem> Products,
    IReadOnlyList<SupplierReportItem> Suppliers,
    IReadOnlyList<CustomerReportItem> Customers,
    IReadOnlyList<DemandForecastItem> Forecasts);

public interface IReportService
{
    Task<WarehouseReport> GetAsync(
        ReportQuery query,
        CancellationToken cancellationToken = default);
}
