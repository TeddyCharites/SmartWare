using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartWare.Application.AI.Chatbot;
using SmartWare.Application.Common;
using SmartWare.Application.Warehouse.Exports;
using SmartWare.Domain.Constants;
using SmartWare.Domain.Enums;
using SmartWare.Web.ViewModels.ExportReceipts;

namespace SmartWare.Web.Controllers;

[Authorize]
[Route("xuat-kho")]
public sealed class ExportReceiptsController(
    IExportReceiptService exportReceiptService,
    IChatDraftStore draftStore) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? search,
        int? customerId,
        ReceiptStatus? status,
        DateOnly? fromDate,
        DateOnly? toDate,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var receiptPage = await exportReceiptService.GetPageAsync(
            new ExportReceiptQuery(search, customerId, status, fromDate, toDate, page),
            cancellationToken);
        return View(new ExportReceiptIndexViewModel
        {
            Search = search,
            CustomerId = customerId,
            Status = status,
            FromDate = fromDate,
            ToDate = toDate,
            Page = receiptPage
        });
    }

    [HttpGet("chi-tiet/{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var receipt = await exportReceiptService.GetByIdAsync(id, cancellationToken);
        return receipt is null ? NotFound() : View(receipt);
    }

    [Authorize(Policy = AuthorizationPolicies.CreateReceipts)]
    [HttpGet("tao-phieu")]
    public async Task<IActionResult> Create(
        int? orderId,
        Guid? draftId,
        CancellationToken cancellationToken)
    {
        var model = new CreateExportReceiptViewModel { OrderId = orderId };
        // "Mở trong form" from the assistant: prefill from the user's own draft so it can be edited.
        if (draftId is { } id &&
            draftStore.Get(GetCurrentUserId(), id) is { Type: ChatDraftTypes.Export } draft &&
            draft.Lines.Count > 0)
        {
            model.OrderId = draft.OrderId;
            model.WarehouseId = draft.WarehouseId;
            model.Lines = draft.Lines
                .Select(line => new ExportReceiptLineViewModel
                {
                    ProductId = line.ProductId,
                    Quantity = line.Quantity
                })
                .ToList();
        }

        await PopulateOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [Authorize(Policy = AuthorizationPolicies.CreateReceipts)]
    [HttpPost("tao-phieu")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        CreateExportReceiptViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model, cancellationToken);
            return View(model);
        }

        var result = await exportReceiptService.CreateAsync(
            new CreateExportReceiptCommand(
                model.OrderId,
                model.WarehouseId,
                model.Lines.Select(line => new CreateExportReceiptLine(
                    line.ProductId,
                    line.Quantity)).ToArray(),
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

    [Authorize(Policy = AuthorizationPolicies.ApproveReceipts)]
    [HttpPost("duyet")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Approve(
        ReviewExportReceiptViewModel model,
        CancellationToken cancellationToken) =>
        ReviewAsync(model, true, cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.ApproveReceipts)]
    [HttpPost("tu-choi")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Reject(
        ReviewExportReceiptViewModel model,
        CancellationToken cancellationToken) =>
        ReviewAsync(model, false, cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.CompleteReceipts)]
    [HttpPost("hoan-tat")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(
        CompleteExportReceiptViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Yêu cầu hoàn tất phiếu xuất không hợp lệ.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        var result = await exportReceiptService.CompleteAsync(
            new CompleteExportReceiptCommand(model.Id, model.RowVersion, GetCurrentUserId()),
            cancellationToken);
        SetResultMessage(result);
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    [Authorize(Policy = AuthorizationPolicies.ApproveReceipts)]
    [HttpPost("huy")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        CancelExportReceiptViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Vui lòng nhập lý do hủy phiếu.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        var result = await exportReceiptService.CancelAsync(
            new CancelExportReceiptCommand(model.Id, model.RowVersion, model.Reason, GetCurrentUserId()),
            cancellationToken);
        SetResultMessage(result);
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    private async Task<IActionResult> ReviewAsync(
        ReviewExportReceiptViewModel model,
        bool approve,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Yêu cầu xử lý phiếu xuất không hợp lệ.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        var result = await exportReceiptService.ReviewAsync(
            new ReviewExportReceiptCommand(
                model.Id,
                model.RowVersion,
                approve,
                model.RejectionReason,
                GetCurrentUserId()),
            cancellationToken);
        SetResultMessage(result);
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    private async Task PopulateOptionsAsync(
        CreateExportReceiptViewModel model,
        CancellationToken cancellationToken)
    {
        var options = await exportReceiptService.GetFormOptionsAsync(
            model.WarehouseId > 0 ? model.WarehouseId : null,
            cancellationToken);
        model.Warehouses = options.Warehouses;
        model.Products = options.Products;
        model.Orders = options.Orders;
        if (model.WarehouseId == 0)
        {
            model.WarehouseId = options.Warehouses.FirstOrDefault()?.Id ?? 0;
        }
    }

    private void SetResultMessage(OperationResult result)
    {
        if (result.Succeeded)
        {
            TempData["SuccessMessage"] = result.Message;
        }
        else
        {
            TempData["ErrorMessage"] = result.Errors.First();
        }
    }

    private string GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Không xác định được người dùng hiện tại.");
}
