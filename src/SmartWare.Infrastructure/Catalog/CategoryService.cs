using Microsoft.EntityFrameworkCore;
using SmartWare.Application.Catalog.Categories;
using SmartWare.Application.Common;
using SmartWare.Domain.Entities;
using SmartWare.Infrastructure.Auditing;
using SmartWare.Infrastructure.Data;

namespace SmartWare.Infrastructure.Catalog;

internal sealed class CategoryService(ApplicationDbContext dbContext) : ICategoryService
{
    public async Task<PagedResult<CategoryListItem>> GetPageAsync(
        CategoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var source = dbContext.Categories.AsNoTracking();
        var search = query.Search?.Trim();

        if (!string.IsNullOrWhiteSpace(search))
        {
            source = source.Where(category =>
                category.Name.Contains(search) ||
                (category.Description != null && category.Description.Contains(search)));
        }

        if (query.IsActive.HasValue)
        {
            source = source.Where(category => category.IsActive == query.IsActive.Value);
        }

        var totalCount = await source.CountAsync(cancellationToken);
        var pageSize = Math.Clamp(query.PageSize, 5, 100);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Clamp(query.Page, 1, totalPages);

        var items = await source
            .OrderBy(category => category.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(category => new CategoryListItem(
                category.CategoryId,
                category.Name,
                category.Description,
                category.IsActive,
                category.Products.Count,
                category.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<CategoryListItem>(items, page, pageSize, totalCount);
    }

    public Task<CategoryDetails?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        dbContext.Categories
            .AsNoTracking()
            .Where(category => category.CategoryId == id)
            .Select(category => new CategoryDetails(
                category.CategoryId,
                category.Name,
                category.Description,
                category.IsActive,
                category.Products.Count,
                category.CreatedAt,
                category.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<OperationResult> CreateAsync(
        SaveCategoryCommand command,
        CancellationToken cancellationToken = default)
    {
        var name = command.Name.Trim();
        if (await dbContext.Categories.AnyAsync(category => category.Name == name, cancellationToken))
        {
            return OperationResult.Failure("Tên danh mục đã tồn tại.");
        }

        var category = new Category
        {
            Name = name,
            Description = NormalizeOptional(command.Description),
            IsActive = command.IsActive,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.Categories.Add(category);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            AuditTrail.Add(
                dbContext,
                command.PerformedById,
                "Create",
                "Catalog",
                nameof(Category),
                category.CategoryId.ToString(),
                null,
                Snapshot(category));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Success("Đã thêm danh mục thành công.");
        }
        catch (DbUpdateException)
        {
            return OperationResult.Failure("Không thể lưu danh mục. Vui lòng kiểm tra tên không bị trùng.");
        }
    }

    public async Task<OperationResult> UpdateAsync(
        SaveCategoryCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!command.Id.HasValue)
        {
            return OperationResult.Failure("Danh mục không hợp lệ.");
        }

        var category = await dbContext.Categories.FindAsync([command.Id.Value], cancellationToken);
        if (category is null)
        {
            return OperationResult.Failure("Không tìm thấy danh mục.");
        }

        var name = command.Name.Trim();
        if (await dbContext.Categories.AnyAsync(
                item => item.CategoryId != category.CategoryId && item.Name == name,
                cancellationToken))
        {
            return OperationResult.Failure("Tên danh mục đã tồn tại.");
        }

        var oldValues = Snapshot(category);
        category.Name = name;
        category.Description = NormalizeOptional(command.Description);
        category.IsActive = command.IsActive;
        category.UpdatedAt = DateTimeOffset.UtcNow;

        AuditTrail.Add(
            dbContext,
            command.PerformedById,
            "Update",
            "Catalog",
            nameof(Category),
            category.CategoryId.ToString(),
            oldValues,
            Snapshot(category));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return OperationResult.Success("Đã cập nhật danh mục thành công.");
        }
        catch (DbUpdateException)
        {
            return OperationResult.Failure("Không thể cập nhật danh mục. Vui lòng kiểm tra dữ liệu.");
        }
    }

    public async Task<OperationResult> DeleteAsync(
        int id,
        string performedById,
        CancellationToken cancellationToken = default)
    {
        var category = await dbContext.Categories.FindAsync([id], cancellationToken);
        if (category is null)
        {
            return OperationResult.Failure("Không tìm thấy danh mục.");
        }

        var oldValues = Snapshot(category);
        var isReferenced = await dbContext.Products
            .AnyAsync(product => product.CategoryId == id, cancellationToken);

        string message;
        if (isReferenced)
        {
            category.IsActive = false;
            category.UpdatedAt = DateTimeOffset.UtcNow;
            message = "Danh mục đang được sản phẩm sử dụng nên đã chuyển sang ngừng hoạt động.";
        }
        else
        {
            dbContext.Categories.Remove(category);
            message = "Đã xóa danh mục thành công.";
        }

        AuditTrail.Add(
            dbContext,
            performedById,
            isReferenced ? "Deactivate" : "Delete",
            "Catalog",
            nameof(Category),
            id.ToString(),
            oldValues,
            isReferenced ? Snapshot(category) : null);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return OperationResult.Success(message);
        }
        catch (DbUpdateException)
        {
            return OperationResult.Failure("Không thể xóa danh mục vì dữ liệu đang được sử dụng.");
        }
    }

    private static object Snapshot(Category category) => new
    {
        category.Name,
        category.Description,
        category.IsActive
    };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
