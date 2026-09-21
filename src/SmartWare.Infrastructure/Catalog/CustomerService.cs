using Microsoft.EntityFrameworkCore;
using SmartWare.Application.Catalog.Customers;
using SmartWare.Application.Common;
using SmartWare.Domain.Entities;
using SmartWare.Domain.Enums;
using SmartWare.Infrastructure.Auditing;
using SmartWare.Infrastructure.Data;

namespace SmartWare.Infrastructure.Catalog;

internal sealed class CustomerService(ApplicationDbContext dbContext) : ICustomerService
{
    public async Task<CustomerPage> GetPageAsync(
        CustomerQuery query,
        CancellationToken cancellationToken = default)
    {
        var source = ApplyFilters(query);
        var totalCount = await source.CountAsync(cancellationToken);
        var pageSize = Math.Clamp(query.PageSize, 5, 100);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Clamp(query.Page, 1, totalPages);

        var items = await source
            .OrderBy(customer => customer.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(customer => new CustomerListItem(
                customer.CustomerId,
                customer.Code,
                customer.Name,
                customer.Phone,
                customer.Email,
                customer.Address,
                customer.IsActive,
                customer.Orders.Count,
                customer.Orders
                    .Where(order => order.Status == OrderStatus.Completed)
                    .Sum(order => (decimal?)order.TotalAmount) ?? 0,
                customer.Orders.Max(order => (DateTimeOffset?)order.OrderDate),
                customer.CreatedAt))
            .ToListAsync(cancellationToken);

        var monthStart = new DateTimeOffset(
            DateTimeOffset.UtcNow.Year,
            DateTimeOffset.UtcNow.Month,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);
        var statistics = new CustomerStatistics(
            await dbContext.Customers.CountAsync(cancellationToken),
            await dbContext.Customers.CountAsync(customer => customer.IsActive, cancellationToken),
            await dbContext.Customers.CountAsync(customer => customer.Orders.Any(), cancellationToken),
            await dbContext.Customers.CountAsync(customer => customer.CreatedAt >= monthStart, cancellationToken),
            await dbContext.Orders.CountAsync(cancellationToken),
            await dbContext.Orders
                .Where(order => order.Status == OrderStatus.Completed)
                .SumAsync(order => (decimal?)order.TotalAmount, cancellationToken) ?? 0);

        return new CustomerPage(
            new PagedResult<CustomerListItem>(items, page, pageSize, totalCount),
            statistics);
    }

    public Task<CustomerDetails?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        dbContext.Customers
            .AsNoTracking()
            .Where(customer => customer.CustomerId == id)
            .Select(customer => new CustomerDetails(
                customer.CustomerId,
                customer.Code,
                customer.Name,
                customer.Phone,
                customer.Email,
                customer.Address,
                customer.IsActive,
                customer.Orders.Count,
                customer.Orders.Count(order => order.Status == OrderStatus.Completed),
                customer.Orders
                    .Where(order => order.Status == OrderStatus.Completed)
                    .Sum(order => (decimal?)order.TotalAmount) ?? 0,
                customer.Orders.Max(order => (DateTimeOffset?)order.OrderDate),
                customer.CreatedAt,
                customer.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CustomerExportRow>> GetExportAsync(
        CustomerQuery query,
        CancellationToken cancellationToken = default) =>
        await ApplyFilters(query)
            .OrderBy(customer => customer.Name)
            .Select(customer => new CustomerExportRow(
                customer.Code,
                customer.Name,
                customer.Phone,
                customer.Email,
                customer.Address,
                customer.Orders.Count,
                customer.Orders
                    .Where(order => order.Status == OrderStatus.Completed)
                    .Sum(order => (decimal?)order.TotalAmount) ?? 0,
                customer.Orders.Max(order => (DateTimeOffset?)order.OrderDate),
                customer.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<OperationResult> CreateAsync(
        SaveCustomerCommand command,
        CancellationToken cancellationToken = default)
    {
        var code = NormalizeCode(command.Code);
        if (await dbContext.Customers.AnyAsync(customer => customer.Code == code, cancellationToken))
        {
            return OperationResult.Failure("Mã khách hàng đã tồn tại.");
        }

        var customer = new Customer
        {
            Code = code,
            Name = command.Name.Trim(),
            Phone = NormalizeOptional(command.Phone),
            Email = NormalizeOptional(command.Email),
            Address = NormalizeOptional(command.Address),
            IsActive = command.IsActive,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.Customers.Add(customer);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            AuditTrail.Add(
                dbContext,
                command.PerformedById,
                "Create",
                "Customer",
                nameof(Customer),
                customer.CustomerId.ToString(),
                null,
                Snapshot(customer));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Success("Đã thêm khách hàng thành công.");
        }
        catch (DbUpdateException)
        {
            return OperationResult.Failure(
                "Không thể lưu khách hàng. Vui lòng kiểm tra mã không bị trùng.");
        }
    }

    public async Task<OperationResult> UpdateAsync(
        SaveCustomerCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!command.Id.HasValue)
        {
            return OperationResult.Failure("Khách hàng không hợp lệ.");
        }

        var customer = await dbContext.Customers.FindAsync([command.Id.Value], cancellationToken);
        if (customer is null)
        {
            return OperationResult.Failure("Không tìm thấy khách hàng.");
        }

        var code = NormalizeCode(command.Code);
        if (await dbContext.Customers.AnyAsync(
                item => item.CustomerId != customer.CustomerId && item.Code == code,
                cancellationToken))
        {
            return OperationResult.Failure("Mã khách hàng đã tồn tại.");
        }

        var oldValues = Snapshot(customer);
        customer.Code = code;
        customer.Name = command.Name.Trim();
        customer.Phone = NormalizeOptional(command.Phone);
        customer.Email = NormalizeOptional(command.Email);
        customer.Address = NormalizeOptional(command.Address);
        customer.IsActive = command.IsActive;
        customer.UpdatedAt = DateTimeOffset.UtcNow;

        AuditTrail.Add(
            dbContext,
            command.PerformedById,
            "Update",
            "Customer",
            nameof(Customer),
            customer.CustomerId.ToString(),
            oldValues,
            Snapshot(customer));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return OperationResult.Success("Đã cập nhật khách hàng thành công.");
        }
        catch (DbUpdateException)
        {
            return OperationResult.Failure("Không thể cập nhật khách hàng. Vui lòng kiểm tra dữ liệu.");
        }
    }

    public async Task<OperationResult> DeleteAsync(
        int id,
        string performedById,
        CancellationToken cancellationToken = default)
    {
        var customer = await dbContext.Customers.FindAsync([id], cancellationToken);
        if (customer is null)
        {
            return OperationResult.Failure("Không tìm thấy khách hàng.");
        }

        var oldValues = Snapshot(customer);
        var isReferenced = await dbContext.Orders.AnyAsync(
            order => order.CustomerId == id,
            cancellationToken);
        string message;
        if (isReferenced)
        {
            customer.IsActive = false;
            customer.UpdatedAt = DateTimeOffset.UtcNow;
            message = "Khách hàng đã có đơn nên được chuyển sang ngừng hoạt động.";
        }
        else
        {
            dbContext.Customers.Remove(customer);
            message = "Đã xóa khách hàng thành công.";
        }

        AuditTrail.Add(
            dbContext,
            performedById,
            isReferenced ? "Deactivate" : "Delete",
            "Customer",
            nameof(Customer),
            id.ToString(),
            oldValues,
            isReferenced ? Snapshot(customer) : null);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return OperationResult.Success(message);
        }
        catch (DbUpdateException)
        {
            return OperationResult.Failure(
                "Không thể xóa khách hàng vì dữ liệu đang được sử dụng.");
        }
    }

    private IQueryable<Customer> ApplyFilters(CustomerQuery query)
    {
        var source = dbContext.Customers.AsNoTracking();
        var search = query.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            source = source.Where(customer =>
                customer.Code.Contains(search) ||
                customer.Name.Contains(search) ||
                (customer.Phone != null && customer.Phone.Contains(search)) ||
                (customer.Email != null && customer.Email.Contains(search)) ||
                (customer.Address != null && customer.Address.Contains(search)));
        }

        if (query.IsActive.HasValue)
        {
            source = source.Where(customer => customer.IsActive == query.IsActive.Value);
        }

        return source;
    }

    private static object Snapshot(Customer customer) => new
    {
        customer.Code,
        customer.Name,
        customer.Phone,
        customer.Email,
        customer.Address,
        customer.IsActive
    };

    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
