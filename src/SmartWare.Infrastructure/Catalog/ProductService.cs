using Microsoft.EntityFrameworkCore;
using SmartWare.Application.Catalog.Products;
using SmartWare.Application.Common;
using SmartWare.Domain.Entities;
using SmartWare.Infrastructure.Auditing;
using SmartWare.Infrastructure.Data;

namespace SmartWare.Infrastructure.Catalog;

internal sealed class ProductService(ApplicationDbContext dbContext) : IProductService
{
    public async Task<ProductPage> GetPageAsync(
        ProductQuery query,
        CancellationToken cancellationToken = default)
    {
        var source = ApplyFilters(query);

        var totalCount = await source.CountAsync(cancellationToken);
        var pageSize = Math.Clamp(query.PageSize, 5, 100);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Clamp(query.Page, 1, totalPages);

        var items = await source
            .OrderBy(product => product.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(product => new ProductListItem(
                product.ProductId,
                product.Sku,
                product.Name,
                product.Category.Name,
                product.Supplier.Name,
                product.UnitOfMeasure,
                product.ImageUrl,
                product.Inventories.Average(inventory => (decimal?)inventory.AverageCost) ?? 0,
                product.SellingPrice,
                product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)inventory.CurrentQuantity) ?? 0,
                product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)inventory.ReservedQuantity) ?? 0,
                product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)(inventory.CurrentQuantity - inventory.ReservedQuantity)) ?? 0,
                product.MinStock,
                product.IsActive))
            .ToListAsync(cancellationToken);

        var activeProducts = dbContext.Products.Where(product => product.IsActive);
        var statistics = new ProductStatistics(
            await dbContext.Products.CountAsync(cancellationToken),
            await activeProducts.CountAsync(cancellationToken),
            await activeProducts.CountAsync(
                product => (product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)(inventory.CurrentQuantity - inventory.ReservedQuantity)) ?? 0) > 0 &&
                    (product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)(inventory.CurrentQuantity - inventory.ReservedQuantity)) ?? 0) <= product.MinStock,
                cancellationToken),
            await activeProducts.CountAsync(
                product => (product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)(inventory.CurrentQuantity - inventory.ReservedQuantity)) ?? 0) <= 0,
                cancellationToken),
            await dbContext.Inventories
                .Where(inventory => inventory.Product.IsActive && inventory.Warehouse.IsActive)
                .SumAsync(
                    inventory => (decimal?)(inventory.CurrentQuantity * inventory.AverageCost),
                    cancellationToken) ?? 0);

        return new ProductPage(
            new PagedResult<ProductListItem>(items, page, pageSize, totalCount),
            statistics,
            await GetFormOptionsAsync(cancellationToken));
    }

    public Task<ProductDetails?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        dbContext.Products
            .AsNoTracking()
            .Where(product => product.ProductId == id)
            .Select(product => new ProductDetails(
                product.ProductId,
                product.Sku,
                product.Name,
                product.CategoryId,
                product.Category.Name,
                product.SupplierId,
                product.Supplier.Name,
                product.UnitOfMeasure,
                product.ImageUrl,
                product.Inventories.Average(inventory => (decimal?)inventory.AverageCost) ?? 0,
                product.SellingPrice,
                product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)inventory.CurrentQuantity) ?? 0,
                product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)inventory.ReservedQuantity) ?? 0,
                product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)(inventory.CurrentQuantity - inventory.ReservedQuantity)) ?? 0,
                product.MinStock,
                product.MaxStock,
                product.Description,
                product.IsActive,
                product.CreatedAt,
                product.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ProductExportRow>> GetExportAsync(
        ProductQuery query,
        CancellationToken cancellationToken = default) =>
        await ApplyFilters(query)
            .OrderBy(product => product.Name)
            .Select(product => new ProductExportRow(
                product.Sku,
                product.Name,
                product.Category.Name,
                product.Supplier.Name,
                product.UnitOfMeasure,
                product.Inventories.Average(inventory => (decimal?)inventory.AverageCost) ?? 0,
                product.SellingPrice,
                product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)inventory.CurrentQuantity) ?? 0,
                product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)inventory.ReservedQuantity) ?? 0,
                product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)(inventory.CurrentQuantity - inventory.ReservedQuantity)) ?? 0,
                product.MinStock,
                product.MaxStock,
                product.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<ProductFormOptions> GetFormOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var categories = await dbContext.Categories
            .AsNoTracking()
            .OrderBy(item => item.Name)
            .Select(item => new LookupItem(item.CategoryId, item.Name, item.IsActive))
            .ToListAsync(cancellationToken);
        var suppliers = await dbContext.Suppliers
            .AsNoTracking()
            .OrderBy(item => item.Name)
            .Select(item => new LookupItem(item.SupplierId, item.Name, item.IsActive))
            .ToListAsync(cancellationToken);

        return new ProductFormOptions(categories, suppliers);
    }

    public async Task<OperationResult> CreateAsync(
        SaveProductCommand command,
        CancellationToken cancellationToken = default)
    {
        var sku = NormalizeCode(command.Sku);
        if (await dbContext.Products.AnyAsync(product => product.Sku == sku, cancellationToken))
        {
            return OperationResult.Failure("SKU đã tồn tại.");
        }

        var relationError = await ValidateRelationsAsync(
            command.CategoryId,
            command.SupplierId,
            requireActiveCategory: true,
            requireActiveSupplier: true,
            cancellationToken);
        if (relationError is not null)
        {
            return OperationResult.Failure(relationError);
        }

        var product = new Product
        {
            Sku = sku,
            Name = command.Name.Trim(),
            CategoryId = command.CategoryId,
            SupplierId = command.SupplierId,
            UnitOfMeasure = command.UnitOfMeasure.Trim(),
            ImageUrl = NormalizeOptional(command.ImageUrl),
            SellingPrice = command.SellingPrice,
            MinStock = command.MinimumStock,
            MaxStock = command.MaximumStock,
            Description = NormalizeOptional(command.Description),
            IsActive = command.IsActive,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.Products.Add(product);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            AuditTrail.Add(
                dbContext,
                command.PerformedById,
                "Create",
                "Product",
                nameof(Product),
                product.ProductId.ToString(),
                null,
                Snapshot(product));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Success("Đã thêm sản phẩm thành công.");
        }
        catch (DbUpdateException)
        {
            return OperationResult.Failure("Không thể lưu sản phẩm. Vui lòng kiểm tra SKU và dữ liệu liên quan.");
        }
    }

    public async Task<OperationResult> UpdateAsync(
        SaveProductCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!command.Id.HasValue)
        {
            return OperationResult.Failure("Sản phẩm không hợp lệ.");
        }

        var product = await dbContext.Products.FindAsync([command.Id.Value], cancellationToken);
        if (product is null)
        {
            return OperationResult.Failure("Không tìm thấy sản phẩm.");
        }

        var sku = NormalizeCode(command.Sku);
        if (await dbContext.Products.AnyAsync(
                item => item.ProductId != product.ProductId && item.Sku == sku,
                cancellationToken))
        {
            return OperationResult.Failure("SKU đã tồn tại.");
        }

        var relationError = await ValidateRelationsAsync(
            command.CategoryId,
            command.SupplierId,
            requireActiveCategory: product.CategoryId != command.CategoryId,
            requireActiveSupplier: product.SupplierId != command.SupplierId,
            cancellationToken);
        if (relationError is not null)
        {
            return OperationResult.Failure(relationError);
        }

        var oldValues = Snapshot(product);
        product.Sku = sku;
        product.Name = command.Name.Trim();
        product.CategoryId = command.CategoryId;
        product.SupplierId = command.SupplierId;
        product.UnitOfMeasure = command.UnitOfMeasure.Trim();
        product.ImageUrl = NormalizeOptional(command.ImageUrl);
        product.SellingPrice = command.SellingPrice;
        product.MinStock = command.MinimumStock;
        product.MaxStock = command.MaximumStock;
        product.Description = NormalizeOptional(command.Description);
        product.IsActive = command.IsActive;
        product.UpdatedAt = DateTimeOffset.UtcNow;

        AuditTrail.Add(
            dbContext,
            command.PerformedById,
            "Update",
            "Product",
            nameof(Product),
            product.ProductId.ToString(),
            oldValues,
            Snapshot(product));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return OperationResult.Success("Đã cập nhật sản phẩm thành công.");
        }
        catch (DbUpdateException)
        {
            return OperationResult.Failure("Không thể cập nhật sản phẩm. Vui lòng kiểm tra dữ liệu.");
        }
    }

    public async Task<OperationResult> DeleteAsync(
        int id,
        string performedById,
        CancellationToken cancellationToken = default)
    {
        var product = await dbContext.Products.FindAsync([id], cancellationToken);
        if (product is null)
        {
            return OperationResult.Failure("Không tìm thấy sản phẩm.");
        }

        var oldValues = Snapshot(product);
        var isReferenced = await IsReferencedAsync(id, cancellationToken);
        string message;

        if (isReferenced)
        {
            product.IsActive = false;
            product.UpdatedAt = DateTimeOffset.UtcNow;
            message = "Sản phẩm đã phát sinh dữ liệu nên được chuyển sang ngừng hoạt động.";
        }
        else
        {
            dbContext.Products.Remove(product);
            message = "Đã xóa sản phẩm thành công.";
        }

        AuditTrail.Add(
            dbContext,
            performedById,
            isReferenced ? "Deactivate" : "Delete",
            "Product",
            nameof(Product),
            id.ToString(),
            oldValues,
            isReferenced ? Snapshot(product) : null);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return OperationResult.Success(message);
        }
        catch (DbUpdateException)
        {
            return OperationResult.Failure("Không thể xóa sản phẩm vì dữ liệu đang được sử dụng.");
        }
    }

    private async Task<string?> ValidateRelationsAsync(
        int categoryId,
        int supplierId,
        bool requireActiveCategory,
        bool requireActiveSupplier,
        CancellationToken cancellationToken)
    {
        var category = await dbContext.Categories
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CategoryId == categoryId, cancellationToken);
        if (category is null)
        {
            return "Danh mục không tồn tại.";
        }

        if (requireActiveCategory && !category.IsActive)
        {
            return "Không thể chọn danh mục đã ngừng hoạt động.";
        }

        var supplier = await dbContext.Suppliers
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.SupplierId == supplierId, cancellationToken);
        if (supplier is null)
        {
            return "Nhà cung cấp không tồn tại.";
        }

        return requireActiveSupplier && !supplier.IsActive
            ? "Không thể chọn nhà cung cấp đã ngừng hoạt động."
            : null;
    }

    private async Task<bool> IsReferencedAsync(int id, CancellationToken cancellationToken) =>
        await dbContext.Inventories.AnyAsync(item => item.ProductId == id, cancellationToken) ||
        await dbContext.OrderDetails.AnyAsync(item => item.ProductId == id, cancellationToken) ||
        await dbContext.ImportReceiptDetails.AnyAsync(item => item.ProductId == id, cancellationToken) ||
        await dbContext.ExportReceiptDetails.AnyAsync(item => item.ProductId == id, cancellationToken) ||
        await dbContext.StockReservations.AnyAsync(item => item.ProductId == id, cancellationToken) ||
        await dbContext.InventoryTransactions.AnyAsync(item => item.ProductId == id, cancellationToken);

    private static object Snapshot(Product product) => new
    {
        product.Sku,
        product.Name,
        product.CategoryId,
        product.SupplierId,
        product.UnitOfMeasure,
        product.ImageUrl,
        product.SellingPrice,
        product.MinStock,
        product.MaxStock,
        product.Description,
        product.IsActive
    };

    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private IQueryable<Product> ApplyFilters(ProductQuery query)
    {
        var source = dbContext.Products.AsNoTracking();
        var search = query.Search?.Trim();

        if (!string.IsNullOrWhiteSpace(search))
        {
            source = source.Where(product =>
                product.Sku.Contains(search) ||
                product.Name.Contains(search) ||
                product.Category.Name.Contains(search) ||
                product.Supplier.Name.Contains(search));
        }

        if (query.CategoryId.HasValue)
        {
            source = source.Where(product => product.CategoryId == query.CategoryId.Value);
        }

        if (query.SupplierId.HasValue)
        {
            source = source.Where(product => product.SupplierId == query.SupplierId.Value);
        }

        if (query.IsActive.HasValue)
        {
            source = source.Where(product => product.IsActive == query.IsActive.Value);
        }

        return source;
    }
}
