using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartWare.Application.Common;
using SmartWare.Application.Warehouse.Imports;
using SmartWare.Domain.Constants;
using SmartWare.Domain.Enums;
using SmartWare.Web.ViewModels.ImportReceipts;

namespace SmartWare.Web.Controllers;

[Authorize]
[Route("nhap-kho")]
public sealed class ImportReceiptsController(IImportReceiptService importReceiptService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? search,
        int? supplierId,
        ReceiptStatus? status,
        DateOnly? fromDate,
        DateOnly? toDate,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var receiptPage = await importReceiptService.GetPageAsync(
            new ImportReceiptQuery(search, supplierId, status, fromDate, toDate, page),
            cancellationToken);
        return View(new ImportReceiptIndexViewModel
        {
            Search = search,
            SupplierId = supplierId,
            Status = status,
            FromDate = fromDate,
            ToDate = toDate,
            Page = receiptPage
        });
    }

    [HttpGet("chi-tiet/{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var receipt = await importReceiptService.GetByIdAsync(id, cancellationToken);
        return receipt is null ? NotFound() : View(receipt);
    }

    [Authorize(Policy = AuthorizationPolicies.CreateReceipts)]
    [HttpGet("tao-phieu")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new CreateImportReceiptViewModel();
        await PopulateOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [Authorize(Policy = AuthorizationPolicies.CreateReceipts)]
    [HttpPost("tao-phieu")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        CreateImportReceiptViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model, cancellationToken);
            return View(model);
        }

        var result = await importReceiptService.CreateAsync(
            new CreateImportReceiptCommand(
                model.SupplierId,
                model.WarehouseId,
                model.Lines.Select(line => new CreateImportReceiptLine(
                    line.ProductId,
                    line.Quantity,
                    line.UnitCost)).ToArray(),
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
        ReviewImportReceiptViewModel model,
        CancellationToken cancellationToken) =>
        ReviewAsync(model, true, cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.ApproveReceipts)]
    [HttpPost("tu-choi")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Reject(
        ReviewImportReceiptViewModel model,
        CancellationToken cancellationToken) =>
        ReviewAsync(model, false, cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.CompleteReceipts)]
    [HttpPost("hoan-tat")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(
        CompleteImportReceiptViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Yêu cầu hoàn tất phiếu nhập không hợp lệ.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        var result = await importReceiptService.CompleteAsync(
            new CompleteImportReceiptCommand(model.Id, model.RowVersion, GetCurrentUserId()),
            cancellationToken);
        SetResultMessage(result);
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    private async Task<IActionResult> ReviewAsync(
        ReviewImportReceiptViewModel model,
        bool approve,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Yêu cầu xử lý phiếu nhập không hợp lệ.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        var result = await importReceiptService.ReviewAsync(
            new ReviewImportReceiptCommand(
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
        CreateImportReceiptViewModel model,
        CancellationToken cancellationToken)
    {
        var options = await importReceiptService.GetFormOptionsAsync(cancellationToken);
        model.Suppliers = options.Suppliers;
        model.Warehouses = options.Warehouses;
        model.Products = options.Products;
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
