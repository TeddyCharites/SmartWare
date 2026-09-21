using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartWare.Application.Catalog.Suppliers;
using SmartWare.Application.Common;
using SmartWare.Domain.Constants;
using SmartWare.Web.Helpers;
using SmartWare.Web.ViewModels.Suppliers;

namespace SmartWare.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("nha-cung-cap")]
public sealed class SuppliersController(ISupplierService supplierService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? search,
        bool? isActive,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var supplierPage = await supplierService.GetPageAsync(
            new SupplierQuery(search, isActive, page),
            cancellationToken);
        return View(new SupplierIndexViewModel
        {
            Search = search,
            IsActive = isActive,
            Page = supplierPage
        });
    }

    [HttpGet("chi-tiet/{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var supplier = await supplierService.GetByIdAsync(id, cancellationToken);
        return supplier is null ? NotFound() : View(supplier);
    }

    [HttpGet("xuat-excel")]
    public async Task<IActionResult> Export(
        string? search,
        bool? isActive,
        CancellationToken cancellationToken)
    {
        var rows = await supplierService.GetExportAsync(
            new SupplierQuery(search, isActive),
            cancellationToken);
        var content = CsvFileBuilder.Build(
            ["Mã NCC", "Tên nhà cung cấp", "Người liên hệ", "Điện thoại", "Email", "Địa chỉ", "Số sản phẩm", "Giá trị nhập hoàn tất", "Trạng thái"],
            rows.Select(row => (IReadOnlyList<object?>)
            [
                row.Code,
                row.Name,
                row.ContactName,
                row.Phone,
                row.Email,
                row.Address,
                row.ProductCount,
                row.TotalImportedValue,
                row.IsActive ? "Hoạt động" : "Ngừng hoạt động"
            ]));

        return File(content, "text/csv; charset=utf-8", $"nha-cung-cap-{DateTime.Today:yyyyMMdd}.csv");
    }

    [HttpGet("them")]
    public IActionResult Create() => View(new SupplierFormViewModel());

    [HttpPost("them")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        SupplierFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await supplierService.CreateAsync(ToCommand(model), cancellationToken);
        return HandleSaveResult(result, model);
    }

    [HttpGet("sua/{id:int}")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var supplier = await supplierService.GetByIdAsync(id, cancellationToken);
        if (supplier is null)
        {
            return NotFound();
        }

        return View(new SupplierFormViewModel
        {
            Id = supplier.Id,
            Code = supplier.Code,
            Name = supplier.Name,
            ContactName = supplier.ContactName,
            Phone = supplier.Phone,
            Email = supplier.Email,
            Address = supplier.Address,
            IsActive = supplier.IsActive
        });
    }

    [HttpPost("sua/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        SupplierFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await supplierService.UpdateAsync(ToCommand(model), cancellationToken);
        return HandleSaveResult(result, model);
    }

    [HttpGet("xoa/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var supplier = await supplierService.GetByIdAsync(id, cancellationToken);
        return supplier is null ? NotFound() : View(supplier);
    }

    [HttpPost("xoa/{id:int}")]
    [ActionName(nameof(Delete))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id, CancellationToken cancellationToken)
    {
        var result = await supplierService.DeleteAsync(id, GetCurrentUserId(), cancellationToken);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors.First();
        }
        else
        {
            TempData["SuccessMessage"] = result.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    private SaveSupplierCommand ToCommand(SupplierFormViewModel model) =>
        new(
            model.Id,
            model.Code,
            model.Name,
            model.ContactName,
            model.Phone,
            model.Email,
            model.Address,
            model.IsActive,
            GetCurrentUserId());

    private IActionResult HandleSaveResult(OperationResult result, SupplierFormViewModel model)
    {
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return View(model.Id.HasValue ? nameof(Edit) : nameof(Create), model);
        }

        TempData["SuccessMessage"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    private string GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Không xác định được người dùng hiện tại.");
}
