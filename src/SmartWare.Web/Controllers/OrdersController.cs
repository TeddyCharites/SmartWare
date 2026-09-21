using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartWare.Application.Sales.Orders;
using SmartWare.Domain.Constants;
using SmartWare.Domain.Enums;
using SmartWare.Web.Helpers;
using SmartWare.Web.ViewModels.Orders;

namespace SmartWare.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("don-hang")]
public sealed class OrdersController(IOrderService orderService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? search,
        OrderStatus? status,
        DateOnly? fromDate,
        DateOnly? toDate,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var orderPage = await orderService.GetPageAsync(
            new OrderQuery(search, status, fromDate, toDate, page),
            cancellationToken);
        return View(new OrderIndexViewModel
        {
            Search = search,
            Status = status,
            FromDate = fromDate,
            ToDate = toDate,
            Page = orderPage
        });
    }

    [HttpGet("chi-tiet/{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var order = await orderService.GetByIdAsync(id, cancellationToken);
        return order is null ? NotFound() : View(order);
    }

    [HttpGet("xuat-excel")]
    public async Task<IActionResult> Export(
        string? search,
        OrderStatus? status,
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken cancellationToken)
    {
        var rows = await orderService.GetExportAsync(
            new OrderQuery(search, status, fromDate, toDate),
            cancellationToken);
        var content = CsvFileBuilder.Build(
            ["Mã đơn", "Mã khách", "Khách hàng", "Ngày đặt", "Số lượng", "Tổng tiền", "Trạng thái", "Phiếu xuất", "Người tạo"],
            rows.Select(row => (IReadOnlyList<object?>)
            [
                row.OrderNumber,
                row.CustomerCode,
                row.CustomerName,
                row.OrderDate.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                row.TotalQuantity,
                row.TotalAmount,
                row.Status,
                row.ExportReceiptNumber,
                row.CreatedByName
            ]));
        return File(content, "text/csv; charset=utf-8", $"don-hang-{DateTime.Today:yyyyMMdd}.csv");
    }

    [HttpGet("tao-don")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new CreateOrderViewModel();
        await PopulateOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost("tao-don")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        CreateOrderViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model, cancellationToken);
            return View(model);
        }

        var localOrderDate = DateTime.SpecifyKind(model.OrderDate, DateTimeKind.Local);
        var result = await orderService.CreateAsync(
            new CreateOrderCommand(
                model.CustomerId,
                new DateTimeOffset(localOrderDate),
                model.Lines.Select(line => new CreateOrderLine(
                    line.ProductId,
                    line.Quantity,
                    line.UnitPrice)).ToArray(),
                GetCurrentUserId()),
            cancellationToken);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            await PopulateOptionsAsync(model, cancellationToken);
            return View(model);
        }

        TempData["SuccessMessage"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("cap-nhat-trang-thai")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(
        ChangeOrderStatusViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Yêu cầu chuyển trạng thái không hợp lệ.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        var result = await orderService.ChangeStatusAsync(
            new ChangeOrderStatusCommand(
                model.Id,
                model.RowVersion,
                model.TargetStatus,
                GetCurrentUserId()),
            cancellationToken);
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] =
            result.Succeeded ? result.Message : result.Errors.First();
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    private async Task PopulateOptionsAsync(
        CreateOrderViewModel model,
        CancellationToken cancellationToken)
    {
        var options = await orderService.GetFormOptionsAsync(cancellationToken);
        model.Customers = options.Customers;
        model.Products = options.Products;
    }

    private string GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Không xác định được người dùng hiện tại.");
}
