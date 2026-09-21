using SmartWare.Application.Common;

namespace SmartWare.Application.Catalog.Customers;

public sealed record CustomerQuery(string? Search, bool? IsActive, int Page = 1, int PageSize = 10);

public sealed record CustomerListItem(
    int Id,
    string Code,
    string Name,
    string? Phone,
    string? Email,
    string? Address,
    bool IsActive,
    int OrderCount,
    decimal CompletedRevenue,
    DateTimeOffset? LastOrderAt,
    DateTimeOffset CreatedAt);

public sealed record CustomerDetails(
    int Id,
    string Code,
    string Name,
    string? Phone,
    string? Email,
    string? Address,
    bool IsActive,
    int OrderCount,
    int CompletedOrderCount,
    decimal CompletedRevenue,
    DateTimeOffset? LastOrderAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record CustomerExportRow(
    string Code,
    string Name,
    string? Phone,
    string? Email,
    string? Address,
    int OrderCount,
    decimal CompletedRevenue,
    DateTimeOffset? LastOrderAt,
    bool IsActive);

public sealed record SaveCustomerCommand(
    int? Id,
    string Code,
    string Name,
    string? Phone,
    string? Email,
    string? Address,
    bool IsActive,
    string PerformedById);

public sealed record CustomerStatistics(
    int TotalCustomers,
    int ActiveCustomers,
    int CustomersWithOrders,
    int NewThisMonth,
    int TotalOrders,
    decimal CompletedRevenue);

public sealed record CustomerPage(
    PagedResult<CustomerListItem> Results,
    CustomerStatistics Statistics);

public interface ICustomerService
{
    Task<CustomerPage> GetPageAsync(
        CustomerQuery query,
        CancellationToken cancellationToken = default);
    Task<CustomerDetails?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerExportRow>> GetExportAsync(
        CustomerQuery query,
        CancellationToken cancellationToken = default);
    Task<OperationResult> CreateAsync(
        SaveCustomerCommand command,
        CancellationToken cancellationToken = default);
    Task<OperationResult> UpdateAsync(
        SaveCustomerCommand command,
        CancellationToken cancellationToken = default);
    Task<OperationResult> DeleteAsync(
        int id,
        string performedById,
        CancellationToken cancellationToken = default);
}
