using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartWare.Application.Catalog.Products;
using SmartWare.Application.Common;
using SmartWare.Domain.Constants;
using SmartWare.Web.Helpers;
using SmartWare.Web.Services.Products;
using SmartWare.Web.ViewModels.Products;

namespace SmartWare.Web.Controllers;

[Authorize]
[Route("san-pham")]
public sealed class ProductsController(
    IProductService productService,
    IProductImageService productImageService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? search,
        int? categoryId,
        int? supplierId,
        bool? isActive,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var productPage = await productService.GetPageAsync(
            new ProductQuery(search, categoryId, supplierId, isActive, page),
            cancellationToken);
        return View(new ProductIndexViewModel
        {
            Search = search,
            CategoryId = categoryId,
            SupplierId = supplierId,
            IsActive = isActive,
            Page = productPage
        });
    }

    [HttpGet("chi-tiet/{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var product = await productService.GetByIdAsync(id, cancellationToken);
        return product is null ? NotFound() : View(product);
    }

    [HttpGet("xuat-excel")]
    public async Task<IActionResult> Export(
        string? search,
        int? categoryId,
        int? supplierId,
        bool? isActive,
        CancellationToken cancellationToken)
    {
        var rows = await productService.GetExportAsync(
            new ProductQuery(search, categoryId, supplierId, isActive),
            cancellationToken);
        var content = CsvFileBuilder.Build(
            ["SKU", "Tên sản phẩm", "Danh mục", "Nhà cung cấp", "Đơn vị", "Giá vốn", "Giá bán", "Tồn thực tế", "Đã giữ", "Khả dụng", "Tồn tối thiểu", "Tồn tối đa", "Trạng thái"],
            rows.Select(row => (IReadOnlyList<object?>)
            [
                row.Sku,
                row.Name,
                row.CategoryName,
                row.SupplierName,
                row.UnitOfMeasure,
                row.AverageCost,
                row.SellingPrice,
                row.CurrentQuantity,
                row.ReservedQuantity,
                row.AvailableQuantity,
                row.MinimumStock,
                row.MaximumStock,
                row.IsActive ? "Đang kinh doanh" : "Ngừng kinh doanh"
            ]));

        return File(content, "text/csv; charset=utf-8", $"san-pham-{DateTime.Today:yyyyMMdd}.csv");
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpGet("them")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new ProductFormViewModel();
        await PopulateOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("them")]
    [ValidateAntiForgeryToken]
    [RequestFormLimits(MultipartBodyLengthLimit = ProductImageService.MaximumFileSize + 1024 * 1024)]
    [RequestSizeLimit(ProductImageService.MaximumFileSize + 1024 * 1024)]
    public async Task<IActionResult> Create(
        ProductFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model, cancellationToken);
            return View(model);
        }

        model.ImageUrl = null;
        string? uploadedImageUrl = null;
        if (model.ImageFile is not null)
        {
            var imageResult = await productImageService.SaveAsync(model.ImageFile, cancellationToken);
            if (!imageResult.Succeeded)
            {
                ModelState.AddModelError(nameof(model.ImageFile), imageResult.Error!);
                await PopulateOptionsAsync(model, cancellationToken);
                return View(model);
            }

            uploadedImageUrl = imageResult.ImageUrl;
            model.ImageUrl = uploadedImageUrl;
        }

        var result = await productService.CreateAsync(ToCommand(model), cancellationToken);
        if (!result.Succeeded && uploadedImageUrl is not null)
        {
            await productImageService.DeleteManagedImageAsync(uploadedImageUrl, cancellationToken);
            model.ImageUrl = null;
        }

        return await HandleSaveResultAsync(result, model, cancellationToken);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpGet("sua/{id:int}")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var product = await productService.GetByIdAsync(id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        var model = new ProductFormViewModel
        {
            Id = product.Id,
            Sku = product.Sku,
            Name = product.Name,
            CategoryId = product.CategoryId,
            SupplierId = product.SupplierId,
            UnitOfMeasure = product.UnitOfMeasure,
            ImageUrl = product.ImageUrl,
            SellingPrice = product.SellingPrice,
            MinimumStock = product.MinimumStock,
            MaximumStock = product.MaximumStock,
            Description = product.Description,
            IsActive = product.IsActive
        };
        await PopulateOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("sua/{id:int}")]
    [ValidateAntiForgeryToken]
    [RequestFormLimits(MultipartBodyLengthLimit = ProductImageService.MaximumFileSize + 1024 * 1024)]
    [RequestSizeLimit(ProductImageService.MaximumFileSize + 1024 * 1024)]
    public async Task<IActionResult> Edit(
        int id,
        ProductFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        var currentProduct = await productService.GetByIdAsync(id, cancellationToken);
        if (currentProduct is null)
        {
            return NotFound();
        }

        model.ImageUrl = currentProduct.ImageUrl;
        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model, cancellationToken);
            return View(model);
        }

        var previousImageUrl = currentProduct.ImageUrl;
        string? uploadedImageUrl = null;
        if (model.ImageFile is not null)
        {
            var imageResult = await productImageService.SaveAsync(model.ImageFile, cancellationToken);
            if (!imageResult.Succeeded)
            {
                ModelState.AddModelError(nameof(model.ImageFile), imageResult.Error!);
                await PopulateOptionsAsync(model, cancellationToken);
                return View(model);
            }

            uploadedImageUrl = imageResult.ImageUrl;
            model.ImageUrl = uploadedImageUrl;
        }
        else if (model.RemoveImage)
        {
            model.ImageUrl = null;
        }

        var result = await productService.UpdateAsync(ToCommand(model), cancellationToken);
        if (!result.Succeeded && uploadedImageUrl is not null)
        {
            await productImageService.DeleteManagedImageAsync(uploadedImageUrl, cancellationToken);
            model.ImageUrl = previousImageUrl;
        }
        else if (result.Succeeded
                 && !string.Equals(previousImageUrl, model.ImageUrl, StringComparison.OrdinalIgnoreCase))
        {
            await productImageService.DeleteManagedImageAsync(previousImageUrl, cancellationToken);
        }

        return await HandleSaveResultAsync(result, model, cancellationToken);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpGet("xoa/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var product = await productService.GetByIdAsync(id, cancellationToken);
        return product is null ? NotFound() : View(product);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("xoa/{id:int}")]
    [ActionName(nameof(Delete))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id, CancellationToken cancellationToken)
    {
        var result = await productService.DeleteAsync(id, GetCurrentUserId(), cancellationToken);
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

    private SaveProductCommand ToCommand(ProductFormViewModel model) =>
        new(
            model.Id,
            model.Sku,
            model.Name,
            model.CategoryId,
            model.SupplierId,
            model.UnitOfMeasure,
            model.ImageUrl,
            model.SellingPrice,
            model.MinimumStock,
            model.MaximumStock,
            model.Description,
            model.IsActive,
            GetCurrentUserId());

    private async Task<IActionResult> HandleSaveResultAsync(
        OperationResult result,
        ProductFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            await PopulateOptionsAsync(model, cancellationToken);
            return View(model.Id.HasValue ? nameof(Edit) : nameof(Create), model);
        }

        TempData["SuccessMessage"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateOptionsAsync(
        ProductFormViewModel model,
        CancellationToken cancellationToken)
    {
        var options = await productService.GetFormOptionsAsync(cancellationToken);
        model.Categories = options.Categories;
        model.Suppliers = options.Suppliers;
    }

    private string GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Không xác định được người dùng hiện tại.");
}
