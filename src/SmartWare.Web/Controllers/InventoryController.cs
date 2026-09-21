using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartWare.Application.Warehouse.Inventory;
using SmartWare.Domain.Enums;
using SmartWare.Web.ViewModels.Inventory;

namespace SmartWare.Web.Controllers;

[Authorize]
[Route("ton-kho")]
public sealed class InventoryController(IInventoryQueryService inventoryQueryService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string tab = "stock",
        string? search = null,
        int? categoryId = null,
        StockLevelStatus? status = null,
        InventoryTransactionType? type = null,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var showHistory = string.Equals(tab, "history", StringComparison.OrdinalIgnoreCase);
        if (showHistory)
        {
            var history = await inventoryQueryService.GetHistoryAsync(
                new InventoryHistoryQuery(search, type, fromDate, toDate, page),
                cancellationToken);
            return View(new InventoryIndexViewModel
            {
                Tab = "history",
                Search = search,
                Type = type,
                FromDate = fromDate,
                ToDate = toDate,
                History = history
            });
        }

        var overview = await inventoryQueryService.GetOverviewAsync(
            new InventoryQuery(search, categoryId, status, page),
            cancellationToken);
        return View(new InventoryIndexViewModel
        {
            Tab = "stock",
            Search = search,
            CategoryId = categoryId,
            Status = status,
            Overview = overview
        });
    }
}
