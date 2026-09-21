using SmartWare.Application.Common;

namespace SmartWare.Application.Catalog.Categories;

public sealed record CategoryQuery(string? Search, bool? IsActive, int Page = 1, int PageSize = 10);

public sealed record CategoryListItem(
    int Id,
    string Name,
    string? Description,
    bool IsActive,
    int ProductCount,
    DateTimeOffset CreatedAt);

public sealed record CategoryDetails(
    int Id,
    string Name,
    string? Description,
    bool IsActive,
    int ProductCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record SaveCategoryCommand(
    int? Id,
    string Name,
    string? Description,
    bool IsActive,
    string PerformedById);

public interface ICategoryService
{
    Task<PagedResult<CategoryListItem>> GetPageAsync(
        CategoryQuery query,
        CancellationToken cancellationToken = default);
    Task<CategoryDetails?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<OperationResult> CreateAsync(
        SaveCategoryCommand command,
        CancellationToken cancellationToken = default);
    Task<OperationResult> UpdateAsync(
        SaveCategoryCommand command,
        CancellationToken cancellationToken = default);
    Task<OperationResult> DeleteAsync(
        int id,
        string performedById,
        CancellationToken cancellationToken = default);
}
