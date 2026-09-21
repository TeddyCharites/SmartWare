using Microsoft.EntityFrameworkCore;
using SmartWare.Application.Catalog.Suppliers;
using SmartWare.Application.Common;
using SmartWare.Domain.Entities;
using SmartWare.Domain.Enums;
using SmartWare.Infrastructure.Auditing;
using SmartWare.Infrastructure.Data;

namespace SmartWare.Infrastructure.Catalog;

internal sealed class SupplierService(ApplicationDbContext dbContext) : ISupplierService
{
    public async Task<SupplierPage> GetPageAsync(
        SupplierQuery query,
        CancellationToken cancellationToken = default)
    {
        var source = ApplyFilters(query);

        var totalCount = await source.CountAsync(cancellationToken);
        var pageSize = Math.Clamp(query.PageSize, 5, 100);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Clamp(query.Page, 1, totalPages);

        var items = await source
            .OrderBy(supplier => supplier.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(supplier => new SupplierListItem(
                supplier.SupplierId,
                supplier.Code,
                supplier.Name,
                supplier.ContactName,
                supplier.Phone,
                supplier.Email,
                supplier.Address,
                supplier.IsActive,
                supplier.Products.Count,
                supplier.ImportReceipts
                    .Where(receipt => receipt.Status == ReceiptStatus.Completed)
                    .SelectMany(receipt => receipt.Details)
                    .Sum(detail => (decimal?)(detail.Quantity * detail.UnitCost)) ?? 0,
                supplier.CreatedAt))
            .ToListAsync(cancellationToken);

        var monthStart = new DateTimeOffset(
            DateTimeOffset.UtcNow.Year,
            DateTimeOffset.UtcNow.Month,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);

        var statistics = new SupplierStatistics(
            await dbContext.Suppliers.CountAsync(cancellationToken),
            await dbContext.Suppliers.CountAsync(item => item.IsActive, cancellationToken),
            await dbContext.Products.CountAsync(cancellationToken),
            await dbContext.ImportReceiptDetails
                .Where(detail => detail.ImportReceipt.Status == ReceiptStatus.Completed)
                .SumAsync(detail => (decimal?)(detail.Quantity * detail.UnitCost), cancellationToken) ?? 0,
            await dbContext.Suppliers.CountAsync(item => item.CreatedAt >= monthStart, cancellationToken));

        return new SupplierPage(
            new PagedResult<SupplierListItem>(items, page, pageSize, totalCount),
            statistics);
    }

    public Task<SupplierDetails?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        dbContext.Suppliers
            .AsNoTracking()
            .Where(supplier => supplier.SupplierId == id)
            .Select(supplier => new SupplierDetails(
                supplier.SupplierId,
                supplier.Code,
                supplier.Name,
                supplier.ContactName,
                supplier.Phone,
                supplier.Email,
                supplier.Address,
                supplier.IsActive,
                supplier.Products.Count,
                supplier.ImportReceipts.Count,
                supplier.ImportReceipts
                    .Where(receipt => receipt.Status == ReceiptStatus.Completed)
                    .SelectMany(receipt => receipt.Details)
                    .Sum(detail => (decimal?)(detail.Quantity * detail.UnitCost)) ?? 0,
                supplier.CreatedAt,
                supplier.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<SupplierExportRow>> GetExportAsync(
        SupplierQuery query,
        CancellationToken cancellationToken = default) =>
        await ApplyFilters(query)
            .OrderBy(supplier => supplier.Name)
            .Select(supplier => new SupplierExportRow(
                supplier.Code,
                supplier.Name,
                supplier.ContactName,
                supplier.Phone,
                supplier.Email,
                supplier.Address,
                supplier.Products.Count,
                supplier.ImportReceipts
                    .Where(receipt => receipt.Status == ReceiptStatus.Completed)
                    .SelectMany(receipt => receipt.Details)
                    .Sum(detail => (decimal?)(detail.Quantity * detail.UnitCost)) ?? 0,
                supplier.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<OperationResult> CreateAsync(
        SaveSupplierCommand command,
        CancellationToken cancellationToken = default)
    {
        var code = NormalizeCode(command.Code);
        if (await dbContext.Suppliers.AnyAsync(supplier => supplier.Code == code, cancellationToken))
        {
            return OperationResult.Failure("Mã nhà cung cấp đã tồn tại.");
        }

        var supplier = new Supplier
        {
            Code = code,
            Name = command.Name.Trim(),
            ContactName = NormalizeOptional(command.ContactName),
            Phone = NormalizeOptional(command.Phone),
            Email = NormalizeOptional(command.Email),
            Address = NormalizeOptional(command.Address),
            IsActive = command.IsActive,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.Suppliers.Add(supplier);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            AuditTrail.Add(
                dbContext,
                command.PerformedById,
                "Create",
                "Supplier",
                nameof(Supplier),
                supplier.SupplierId.ToString(),
                null,
                Snapshot(supplier));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Success("Đã thêm nhà cung cấp thành công.");
        }
        catch (DbUpdateException)
        {
            return OperationResult.Failure("Không thể lưu nhà cung cấp. Vui lòng kiểm tra mã không bị trùng.");
        }
    }

    public async Task<OperationResult> UpdateAsync(
        SaveSupplierCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!command.Id.HasValue)
        {
            return OperationResult.Failure("Nhà cung cấp không hợp lệ.");
        }

        var supplier = await dbContext.Suppliers.FindAsync([command.Id.Value], cancellationToken);
        if (supplier is null)
        {
            return OperationResult.Failure("Không tìm thấy nhà cung cấp.");
        }

        var code = NormalizeCode(command.Code);
        if (await dbContext.Suppliers.AnyAsync(
                item => item.SupplierId != supplier.SupplierId && item.Code == code,
                cancellationToken))
        {
            return OperationResult.Failure("Mã nhà cung cấp đã tồn tại.");
        }

        var oldValues = Snapshot(supplier);
        supplier.Code = code;
        supplier.Name = command.Name.Trim();
        supplier.ContactName = NormalizeOptional(command.ContactName);
        supplier.Phone = NormalizeOptional(command.Phone);
        supplier.Email = NormalizeOptional(command.Email);
        supplier.Address = NormalizeOptional(command.Address);
        supplier.IsActive = command.IsActive;
        supplier.UpdatedAt = DateTimeOffset.UtcNow;

        AuditTrail.Add(
            dbContext,
            command.PerformedById,
            "Update",
            "Supplier",
            nameof(Supplier),
            supplier.SupplierId.ToString(),
            oldValues,
            Snapshot(supplier));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return OperationResult.Success("Đã cập nhật nhà cung cấp thành công.");
        }
        catch (DbUpdateException)
        {
            return OperationResult.Failure("Không thể cập nhật nhà cung cấp. Vui lòng kiểm tra dữ liệu.");
        }
    }

    public async Task<OperationResult> DeleteAsync(
        int id,
        string performedById,
        CancellationToken cancellationToken = default)
    {
        var supplier = await dbContext.Suppliers.FindAsync([id], cancellationToken);
        if (supplier is null)
        {
            return OperationResult.Failure("Không tìm thấy nhà cung cấp.");
        }

        var oldValues = Snapshot(supplier);
        var isReferenced = await dbContext.Products.AnyAsync(
                product => product.SupplierId == id,
                cancellationToken) ||
            await dbContext.ImportReceipts.AnyAsync(
                receipt => receipt.SupplierId == id,
                cancellationToken);

        string message;
        if (isReferenced)
        {
            supplier.IsActive = false;
            supplier.UpdatedAt = DateTimeOffset.UtcNow;
            message = "Nhà cung cấp đang được sử dụng nên đã chuyển sang ngừng hoạt động.";
        }
        else
        {
            dbContext.Suppliers.Remove(supplier);
            message = "Đã xóa nhà cung cấp thành công.";
        }

        AuditTrail.Add(
            dbContext,
            performedById,
            isReferenced ? "Deactivate" : "Delete",
            "Supplier",
            nameof(Supplier),
            id.ToString(),
            oldValues,
            isReferenced ? Snapshot(supplier) : null);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return OperationResult.Success(message);
        }
        catch (DbUpdateException)
        {
            return OperationResult.Failure("Không thể xóa nhà cung cấp vì dữ liệu đang được sử dụng.");
        }
    }

    private static object Snapshot(Supplier supplier) => new
    {
        supplier.Code,
        supplier.Name,
        supplier.ContactName,
        supplier.Phone,
        supplier.Email,
        supplier.Address,
        supplier.IsActive
    };

    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private IQueryable<Supplier> ApplyFilters(SupplierQuery query)
    {
        var source = dbContext.Suppliers.AsNoTracking();
        var search = query.Search?.Trim();

        if (!string.IsNullOrWhiteSpace(search))
        {
            source = source.Where(supplier =>
                supplier.Code.Contains(search) ||
                supplier.Name.Contains(search) ||
                (supplier.Phone != null && supplier.Phone.Contains(search)) ||
                (supplier.Address != null && supplier.Address.Contains(search)));
        }

        if (query.IsActive.HasValue)
        {
            source = source.Where(supplier => supplier.IsActive == query.IsActive.Value);
        }

        return source;
    }
}
