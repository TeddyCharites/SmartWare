using SmartWare.Application.Common;

namespace SmartWare.Application.Catalog.Suppliers;

public sealed record SupplierQuery(string? Search, bool? IsActive, int Page = 1, int PageSize = 10);

public sealed record SupplierListItem(
    int Id,
    string Code,
    string Name,
    string? ContactName,
    string? Phone,
    string? Email,
    string? Address,
    bool IsActive,
    int ProductCount,
    decimal TotalImportedValue,
    DateTimeOffset CreatedAt);

public sealed record SupplierDetails(
    int Id,
    string Code,
    string Name,
    string? ContactName,
    string? Phone,
    string? Email,
    string? Address,
    bool IsActive,
    int ProductCount,
    int ReceiptCount,
    decimal TotalImportedValue,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record SupplierExportRow(
    string Code,
    string Name,
    string? ContactName,
    string? Phone,
    string? Email,
    string? Address,
    int ProductCount,
    decimal TotalImportedValue,
    bool IsActive);

public sealed record SaveSupplierCommand(
    int? Id,
    string Code,
    string Name,
    string? ContactName,
    string? Phone,
    string? Email,
    string? Address,
    bool IsActive,
    string PerformedById);

public sealed record SupplierStatistics(
    int TotalSuppliers,
    int ActiveSuppliers,
    int TotalProducts,
    decimal TotalImportedValue,
    int NewThisMonth);

public sealed record SupplierPage(
    PagedResult<SupplierListItem> Results,
    SupplierStatistics Statistics);

public interface ISupplierService
{
    Task<SupplierPage> GetPageAsync(
        SupplierQuery query,
        CancellationToken cancellationToken = default);
    Task<SupplierDetails?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupplierExportRow>> GetExportAsync(
        SupplierQuery query,
        CancellationToken cancellationToken = default);
    Task<OperationResult> CreateAsync(
        SaveSupplierCommand command,
        CancellationToken cancellationToken = default);
    Task<OperationResult> UpdateAsync(
        SaveSupplierCommand command,
        CancellationToken cancellationToken = default);
    Task<OperationResult> DeleteAsync(
        int id,
        string performedById,
        CancellationToken cancellationToken = default);
}
