using SmartWare.Application.Common;

namespace SmartWare.Application.Catalog.Products;

public sealed record ProductQuery(
    string? Search,
    int? CategoryId,
    int? SupplierId,
    bool? IsActive,
    int Page = 1,
    int PageSize = 10);

public sealed record ProductListItem(
    int Id,
    string Sku,
    string Name,
    string CategoryName,
    string SupplierName,
    string UnitOfMeasure,
    string? ImageUrl,
    decimal AverageCost,
    decimal SellingPrice,
    int CurrentQuantity,
    int ReservedQuantity,
    int AvailableQuantity,
    int MinimumStock,
    bool IsActive);

public sealed record ProductExportRow(
    string Sku,
    string Name,
    string CategoryName,
    string SupplierName,
    string UnitOfMeasure,
    decimal AverageCost,
    decimal SellingPrice,
    int CurrentQuantity,
    int ReservedQuantity,
    int AvailableQuantity,
    int MinimumStock,
    int MaximumStock,
    bool IsActive);

public sealed record ProductDetails(
    int Id,
    string Sku,
    string Name,
    int CategoryId,
    string CategoryName,
    int SupplierId,
    string SupplierName,
    string UnitOfMeasure,
    string? ImageUrl,
    decimal AverageCost,
    decimal SellingPrice,
    int CurrentQuantity,
    int ReservedQuantity,
    int AvailableQuantity,
    int MinimumStock,
    int MaximumStock,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record SaveProductCommand(
    int? Id,
    string Sku,
    string Name,
    int CategoryId,
    int SupplierId,
    string UnitOfMeasure,
    string? ImageUrl,
    decimal SellingPrice,
    int MinimumStock,
    int MaximumStock,
    string? Description,
    bool IsActive,
    string PerformedById);

public sealed record LookupItem(int Id, string Name, bool IsActive);

public sealed record ProductFormOptions(
    IReadOnlyList<LookupItem> Categories,
    IReadOnlyList<LookupItem> Suppliers);

public sealed record ProductStatistics(
    int TotalProducts,
    int ActiveProducts,
    int LowStockProducts,
    int OutOfStockProducts,
    decimal InventoryValue);

public sealed record ProductPage(
    PagedResult<ProductListItem> Results,
    ProductStatistics Statistics,
    ProductFormOptions Filters);

public interface IProductService
{
    Task<ProductPage> GetPageAsync(
        ProductQuery query,
        CancellationToken cancellationToken = default);
    Task<ProductDetails?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductExportRow>> GetExportAsync(
        ProductQuery query,
        CancellationToken cancellationToken = default);
    Task<ProductFormOptions> GetFormOptionsAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> CreateAsync(
        SaveProductCommand command,
        CancellationToken cancellationToken = default);
    Task<OperationResult> UpdateAsync(
        SaveProductCommand command,
        CancellationToken cancellationToken = default);
    Task<OperationResult> DeleteAsync(
        int id,
        string performedById,
        CancellationToken cancellationToken = default);
}
