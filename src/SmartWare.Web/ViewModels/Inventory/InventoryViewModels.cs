using SmartWare.Application.Warehouse.Inventory;
using SmartWare.Domain.Enums;

namespace SmartWare.Web.ViewModels.Inventory;

public sealed class InventoryIndexViewModel
{
    public string Tab { get; init; } = "stock";
    public string? Search { get; init; }
    public int? CategoryId { get; init; }
    public StockLevelStatus? Status { get; init; }
    public InventoryTransactionType? Type { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public InventoryOverviewPage? Overview { get; init; }
    public InventoryHistoryPage? History { get; init; }

    public InventoryStatistics Statistics =>
        Overview?.Statistics ?? History?.Statistics
        ?? new InventoryStatistics(0, 0, 0, 0, 0);
}
