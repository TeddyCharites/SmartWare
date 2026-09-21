using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartWare.Application.Catalog.Customers;
using SmartWare.Application.Common;
using SmartWare.Domain.Constants;
using SmartWare.Web.Helpers;
using SmartWare.Web.ViewModels.Customers;

namespace SmartWare.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("khach-hang")]
public sealed class CustomersController(ICustomerService customerService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? search,
        bool? isActive,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var customerPage = await customerService.GetPageAsync(
            new CustomerQuery(search, isActive, page),
            cancellationToken);
        return View(new CustomerIndexViewModel
        {
            Search = search,
            IsActive = isActive,
            Page = customerPage
        });
    }

    [HttpGet("chi-tiet/{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var customer = await customerService.GetByIdAsync(id, cancellationToken);
        return customer is null ? NotFound() : View(customer);
    }

    [HttpGet("xuat-excel")]
    public async Task<IActionResult> Export(
        string? search,
        bool? isActive,
        CancellationToken cancellationToken)
    {
        var rows = await customerService.GetExportAsync(
            new CustomerQuery(search, isActive),
            cancellationToken);
        var content = CsvFileBuilder.Build(
            ["Mã khách hàng", "Tên khách hàng", "Điện thoại", "Email", "Địa chỉ", "Tổng đơn", "Doanh thu hoàn thành", "Đơn gần nhất", "Trạng thái"],
            rows.Select(row => (IReadOnlyList<object?>)
            [
                row.Code,
                row.Name,
                row.Phone,
                row.Email,
                row.Address,
                row.OrderCount,
                row.CompletedRevenue,
                row.LastOrderAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                row.IsActive ? "Hoạt động" : "Ngừng hoạt động"
            ]));
        return File(content, "text/csv; charset=utf-8", $"khach-hang-{DateTime.Today:yyyyMMdd}.csv");
    }

    [HttpGet("them")]
    public IActionResult Create() => View(new CustomerFormViewModel());

    [HttpPost("them")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        CustomerFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await customerService.CreateAsync(ToCommand(model), cancellationToken);
        return HandleSaveResult(result, model);
    }

    [HttpGet("sua/{id:int}")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var customer = await customerService.GetByIdAsync(id, cancellationToken);
        if (customer is null)
        {
            return NotFound();
        }

        return View(new CustomerFormViewModel
        {
            Id = customer.Id,
            Code = customer.Code,
            Name = customer.Name,
            Phone = customer.Phone,
            Email = customer.Email,
            Address = customer.Address,
            IsActive = customer.IsActive
        });
    }

    [HttpPost("sua/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        CustomerFormViewModel model,
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

        var result = await customerService.UpdateAsync(ToCommand(model), cancellationToken);
        return HandleSaveResult(result, model);
    }

    [HttpGet("xoa/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var customer = await customerService.GetByIdAsync(id, cancellationToken);
        return customer is null ? NotFound() : View(customer);
    }

    [HttpPost("xoa/{id:int}")]
    [ActionName(nameof(Delete))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id, CancellationToken cancellationToken)
    {
        var result = await customerService.DeleteAsync(id, GetCurrentUserId(), cancellationToken);
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] =
            result.Succeeded ? result.Message : result.Errors.First();
        return RedirectToAction(nameof(Index));
    }

    private SaveCustomerCommand ToCommand(CustomerFormViewModel model) => new(
        model.Id,
        model.Code,
        model.Name,
        model.Phone,
        model.Email,
        model.Address,
        model.IsActive,
        GetCurrentUserId());

    private IActionResult HandleSaveResult(OperationResult result, CustomerFormViewModel model)
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
