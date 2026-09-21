using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartWare.Application.Catalog.Categories;
using SmartWare.Application.Common;
using SmartWare.Domain.Constants;
using SmartWare.Web.ViewModels.Categories;

namespace SmartWare.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("danh-muc")]
public sealed class CategoriesController(ICategoryService categoryService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? search,
        bool? isActive,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var results = await categoryService.GetPageAsync(
            new CategoryQuery(search, isActive, page),
            cancellationToken);
        return View(new CategoryIndexViewModel
        {
            Search = search,
            IsActive = isActive,
            Results = results
        });
    }

    [HttpGet("chi-tiet/{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var category = await categoryService.GetByIdAsync(id, cancellationToken);
        return category is null ? NotFound() : View(category);
    }

    [HttpGet("them")]
    public IActionResult Create() => View(new CategoryFormViewModel());

    [HttpPost("them")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        CategoryFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await categoryService.CreateAsync(ToCommand(model), cancellationToken);
        return HandleSaveResult(result, model);
    }

    [HttpGet("sua/{id:int}")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var category = await categoryService.GetByIdAsync(id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        return View(new CategoryFormViewModel
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            IsActive = category.IsActive
        });
    }

    [HttpPost("sua/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        CategoryFormViewModel model,
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

        var result = await categoryService.UpdateAsync(ToCommand(model), cancellationToken);
        return HandleSaveResult(result, model);
    }

    [HttpGet("xoa/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var category = await categoryService.GetByIdAsync(id, cancellationToken);
        return category is null ? NotFound() : View(category);
    }

    [HttpPost("xoa/{id:int}")]
    [ActionName(nameof(Delete))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id, CancellationToken cancellationToken)
    {
        var result = await categoryService.DeleteAsync(id, GetCurrentUserId(), cancellationToken);
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

    private SaveCategoryCommand ToCommand(CategoryFormViewModel model) =>
        new(model.Id, model.Name, model.Description, model.IsActive, GetCurrentUserId());

    private IActionResult HandleSaveResult(OperationResult result, CategoryFormViewModel model)
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
