using SmartWare.Domain.Enums;

namespace SmartWare.Application.Dashboard;

public sealed record DashboardSnapshot(
    int TotalProducts,
    int TotalCategories,
    long TotalStockQuantity,
    int LowStockProductCount,
    int PendingReceiptCount,
    IReadOnlyList<StockAlertItem> StockAlerts,
    IReadOnlyList<RecentReceiptItem> RecentReceipts,
    IReadOnlyList<MonthlyStockMovement> MonthlyMovements)
{
    public int ChartMaximum => Math.Max(
        1,
        MonthlyMovements.Count == 0
            ? 0
            : MonthlyMovements.Max(item => Math.Max(item.ImportQuantity, item.ExportQuantity)));
}

public sealed record StockAlertItem(
    int ProductId,
    string Sku,
    string ProductName,
    int AvailableQuantity,
    int MinimumQuantity);

public sealed record RecentReceiptItem(
    int ReceiptId,
    string ReceiptNumber,
    DashboardReceiptType Type,
    string PartnerName,
    string WarehouseName,
    int TotalQuantity,
    ReceiptStatus Status,
    DateTimeOffset CreatedAt);

public sealed record MonthlyStockMovement(
    DateOnly Month,
    int ImportQuantity,
    int ExportQuantity);

public enum DashboardReceiptType
{
    Import = 1,
    Export = 2
}
