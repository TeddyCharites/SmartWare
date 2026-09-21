using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SmartWare.Application.Common;
using SmartWare.Application.Warehouse.Imports;
using SmartWare.Domain.Entities;
using SmartWare.Domain.Enums;
using SmartWare.Domain.Services;
using SmartWare.Infrastructure.Auditing;
using SmartWare.Infrastructure.Data;

namespace SmartWare.Infrastructure.InventoryOperations;

internal sealed class ImportReceiptService(ApplicationDbContext dbContext) : IImportReceiptService
{
    public async Task<ImportReceiptPage> GetPageAsync(
        ImportReceiptQuery query,
        CancellationToken cancellationToken = default)
    {
        var source = ApplyFilters(query);
        var totalCount = await source.CountAsync(cancellationToken);
        var pageSize = Math.Clamp(query.PageSize, 5, 100);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Clamp(query.Page, 1, totalPages);

        var items = await source
            .OrderByDescending(receipt => receipt.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(receipt => new ImportReceiptListItem(
                receipt.ImportReceiptId,
                receipt.ReceiptNumber,
                receipt.Supplier.Name,
                receipt.Warehouse.Name,
                dbContext.Users
                    .Where(user => user.Id == receipt.CreatedById)
                    .Select(user => user.FullName)
                    .Single(),
                receipt.Details.Sum(detail => (int?)detail.Quantity) ?? 0,
                receipt.Details.Sum(detail => (decimal?)(detail.Quantity * detail.UnitCost)) ?? 0,
                receipt.Status,
                receipt.CreatedAt))
            .ToListAsync(cancellationToken);

        var statistics = new ImportReceiptStatistics(
            await dbContext.ImportReceipts.CountAsync(cancellationToken),
            await dbContext.ImportReceipts.CountAsync(
                receipt => receipt.Status == ReceiptStatus.Pending,
                cancellationToken),
            await dbContext.ImportReceipts.CountAsync(
                receipt => receipt.Status == ReceiptStatus.Approved,
                cancellationToken),
            await dbContext.ImportReceipts.CountAsync(
                receipt => receipt.Status == ReceiptStatus.Completed,
                cancellationToken),
            await dbContext.ImportReceiptDetails
                .Where(detail => detail.ImportReceipt.Status == ReceiptStatus.Completed)
                .SumAsync(
                    detail => (decimal?)(detail.Quantity * detail.UnitCost),
                    cancellationToken) ?? 0);

        var suppliers = await dbContext.Suppliers
            .AsNoTracking()
            .OrderBy(supplier => supplier.Name)
            .Select(supplier => new ImportLookupItem(supplier.SupplierId, supplier.Code, supplier.Name))
            .ToListAsync(cancellationToken);

        return new ImportReceiptPage(
            new PagedResult<ImportReceiptListItem>(items, page, pageSize, totalCount),
            statistics,
            suppliers);
    }

    public async Task<ImportReceiptDetails?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var receipt = await dbContext.ImportReceipts
            .AsNoTracking()
            .Include(item => item.Supplier)
            .Include(item => item.Warehouse)
            .Include(item => item.Details)
                .ThenInclude(detail => detail.Product)
            .SingleOrDefaultAsync(item => item.ImportReceiptId == id, cancellationToken);

        if (receipt is null)
        {
            return null;
        }

        var userIds = new[] { receipt.CreatedById, receipt.ApprovedById, receipt.CompletedById }
            .Where(userId => !string.IsNullOrWhiteSpace(userId))
            .Distinct()
            .ToArray();
        var userNames = await dbContext.Users
            .AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.FullName, cancellationToken);

        var lines = receipt.Details
            .OrderBy(detail => detail.Product.Name)
            .Select(detail => new ImportReceiptLineDetails(
                detail.ProductId,
                detail.Product.Sku,
                detail.Product.Name,
                detail.Product.UnitOfMeasure,
                detail.Quantity,
                detail.UnitCost,
                detail.Quantity * detail.UnitCost))
            .ToArray();

        return new ImportReceiptDetails(
            receipt.ImportReceiptId,
            receipt.ReceiptNumber,
            receipt.SupplierId,
            receipt.Supplier.Name,
            receipt.WarehouseId,
            receipt.Warehouse.Name,
            receipt.Status,
            userNames.GetValueOrDefault(receipt.CreatedById, receipt.CreatedById),
            receipt.ApprovedById is null
                ? null
                : userNames.GetValueOrDefault(receipt.ApprovedById, receipt.ApprovedById),
            receipt.CompletedById is null
                ? null
                : userNames.GetValueOrDefault(receipt.CompletedById, receipt.CompletedById),
            receipt.RejectionReason,
            receipt.CreatedAt,
            receipt.ApprovedAt,
            receipt.CompletedAt,
            lines.Sum(line => line.Quantity),
            lines.Sum(line => line.LineTotal),
            Convert.ToBase64String(receipt.RowVersion),
            lines);
    }

    public async Task<ImportReceiptFormOptions> GetFormOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var suppliers = await dbContext.Suppliers
            .AsNoTracking()
            .Where(supplier => supplier.IsActive)
            .OrderBy(supplier => supplier.Name)
            .Select(supplier => new ImportLookupItem(supplier.SupplierId, supplier.Code, supplier.Name))
            .ToListAsync(cancellationToken);
        var warehouses = await dbContext.Warehouses
            .AsNoTracking()
            .Where(warehouse => warehouse.IsActive)
            .OrderBy(warehouse => warehouse.Name)
            .Select(warehouse => new ImportLookupItem(warehouse.WarehouseId, warehouse.Code, warehouse.Name))
            .ToListAsync(cancellationToken);
        var products = await dbContext.Products
            .AsNoTracking()
            .Where(product => product.IsActive && product.Supplier.IsActive)
            .OrderBy(product => product.Name)
            .Select(product => new ImportProductLookupItem(
                product.ProductId,
                product.Sku,
                product.Name,
                product.SupplierId))
            .ToListAsync(cancellationToken);

        return new ImportReceiptFormOptions(suppliers, warehouses, products);
    }

    public async Task<OperationResult> CreateAsync(
        CreateImportReceiptCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationError = await ValidateCreateCommandAsync(command, cancellationToken);
        if (validationError is not null)
        {
            return OperationResult.Failure(validationError);
        }

        var receipt = new ImportReceipt
        {
            ReceiptNumber = await GenerateReceiptNumberAsync(cancellationToken),
            SupplierId = command.SupplierId,
            WarehouseId = command.WarehouseId,
            Status = ReceiptStatus.Pending,
            CreatedById = command.CreatedById,
            CreatedAt = DateTimeOffset.UtcNow,
            Details = command.Lines.Select(line => new ImportReceiptDetail
            {
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                UnitCost = line.UnitCost
            }).ToList()
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.ImportReceipts.Add(receipt);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            AuditTrail.Add(
                dbContext,
                command.CreatedById,
                "Create",
                "ImportReceipt",
                nameof(ImportReceipt),
                receipt.ImportReceiptId.ToString(),
                null,
                ReceiptSnapshot(receipt));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Success($"Đã tạo phiếu nhập {receipt.ReceiptNumber} và chuyển sang chờ duyệt.");
        }
        catch (DbUpdateException)
        {
            return OperationResult.Failure("Không thể tạo phiếu nhập. Vui lòng kiểm tra lại dữ liệu.");
        }
    }

    public async Task<OperationResult> ReviewAsync(
        ReviewImportReceiptCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseRowVersion(command.RowVersion, out var rowVersion))
        {
            return OperationResult.Failure("Phiên bản phiếu nhập không hợp lệ. Vui lòng tải lại trang.");
        }

        if (!command.Approve && string.IsNullOrWhiteSpace(command.RejectionReason))
        {
            return OperationResult.Failure("Vui lòng nhập lý do từ chối.");
        }

        var receipt = await dbContext.ImportReceipts
            .SingleOrDefaultAsync(item => item.ImportReceiptId == command.Id, cancellationToken);
        if (receipt is null)
        {
            return OperationResult.Failure("Không tìm thấy phiếu nhập.");
        }

        if (receipt.Status != ReceiptStatus.Pending)
        {
            return OperationResult.Failure("Chỉ phiếu đang chờ duyệt mới có thể được duyệt hoặc từ chối.");
        }

        dbContext.Entry(receipt).Property(item => item.RowVersion).OriginalValue = rowVersion;
        var oldValues = ReceiptSnapshot(receipt);
        receipt.Status = command.Approve ? ReceiptStatus.Approved : ReceiptStatus.Rejected;
        receipt.ApprovedById = command.ReviewedById;
        receipt.ApprovedAt = DateTimeOffset.UtcNow;
        receipt.RejectionReason = command.Approve ? null : command.RejectionReason!.Trim();

        AuditTrail.Add(
            dbContext,
            command.ReviewedById,
            command.Approve ? "Approve" : "Reject",
            "ImportReceipt",
            nameof(ImportReceipt),
            receipt.ImportReceiptId.ToString(),
            oldValues,
            ReceiptSnapshot(receipt));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return OperationResult.Success(
                command.Approve
                    ? $"Đã duyệt phiếu nhập {receipt.ReceiptNumber}."
                    : $"Đã từ chối phiếu nhập {receipt.ReceiptNumber}.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult.Failure("Phiếu nhập đã được người khác cập nhật. Vui lòng tải lại trang.");
        }
        catch (DbUpdateException)
        {
            return OperationResult.Failure("Không thể cập nhật trạng thái phiếu nhập.");
        }
    }

    public async Task<OperationResult> CompleteAsync(
        CompleteImportReceiptCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseRowVersion(command.RowVersion, out var rowVersion))
        {
            return OperationResult.Failure("Phiên bản phiếu nhập không hợp lệ. Vui lòng tải lại trang.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var receipt = await dbContext.ImportReceipts
            .Include(item => item.Details)
            .SingleOrDefaultAsync(item => item.ImportReceiptId == command.Id, cancellationToken);
        if (receipt is null)
        {
            return OperationResult.Failure("Không tìm thấy phiếu nhập.");
        }

        if (receipt.Status != ReceiptStatus.Approved)
        {
            return OperationResult.Failure("Chỉ phiếu đã duyệt mới có thể hoàn tất.");
        }

        if (receipt.Details.Count == 0)
        {
            return OperationResult.Failure("Phiếu nhập không có dòng hàng hợp lệ.");
        }

        dbContext.Entry(receipt).Property(item => item.RowVersion).OriginalValue = rowVersion;
        var completedAt = DateTimeOffset.UtcNow;

        try
        {
            foreach (var detail in receipt.Details.OrderBy(item => item.ProductId))
            {
                var inventory = await dbContext.Inventories.SingleOrDefaultAsync(
                    item => item.ProductId == detail.ProductId && item.WarehouseId == receipt.WarehouseId,
                    cancellationToken);

                if (inventory is null)
                {
                    inventory = new Inventory
                    {
                        ProductId = detail.ProductId,
                        WarehouseId = receipt.WarehouseId,
                        CurrentQuantity = detail.Quantity,
                        ReservedQuantity = 0,
                        AverageCost = detail.UnitCost
                    };
                    dbContext.Inventories.Add(inventory);
                }
                else
                {
                    inventory.AverageCost = MovingAverageCostCalculator.Calculate(
                        inventory.CurrentQuantity,
                        inventory.AverageCost,
                        detail.Quantity,
                        detail.UnitCost);
                    inventory.CurrentQuantity = checked(inventory.CurrentQuantity + detail.Quantity);
                }

                dbContext.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = detail.ProductId,
                    WarehouseId = receipt.WarehouseId,
                    Type = InventoryTransactionType.In,
                    Quantity = detail.Quantity,
                    UnitCost = detail.UnitCost,
                    OccurredAt = completedAt,
                    ReferenceType = nameof(ImportReceipt),
                    ReferenceId = receipt.ImportReceiptId,
                    PerformedById = command.CompletedById
                });
            }

            var oldValues = ReceiptSnapshot(receipt);
            receipt.Status = ReceiptStatus.Completed;
            receipt.CompletedById = command.CompletedById;
            receipt.CompletedAt = completedAt;

            AuditTrail.Add(
                dbContext,
                command.CompletedById,
                "Complete",
                "ImportReceipt",
                nameof(ImportReceipt),
                receipt.ImportReceiptId.ToString(),
                oldValues,
                ReceiptSnapshot(receipt));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Success(
                $"Đã hoàn tất phiếu nhập {receipt.ReceiptNumber} và cập nhật tồn kho.");
        }
        catch (OverflowException)
        {
            return OperationResult.Failure("Số lượng tồn kho vượt giới hạn cho phép.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult.Failure("Dữ liệu tồn kho hoặc phiếu nhập đã thay đổi. Vui lòng thử lại.");
        }
        catch (DbUpdateException)
        {
            return OperationResult.Failure("Không thể hoàn tất phiếu nhập. Toàn bộ thay đổi đã được hoàn tác.");
        }
    }

    private IQueryable<ImportReceipt> ApplyFilters(ImportReceiptQuery query)
    {
        var source = dbContext.ImportReceipts.AsNoTracking();
        var search = query.Search?.Trim();

        if (!string.IsNullOrWhiteSpace(search))
        {
            source = source.Where(receipt =>
                receipt.ReceiptNumber.Contains(search) ||
                receipt.Supplier.Name.Contains(search) ||
                dbContext.Users.Any(user =>
                    user.Id == receipt.CreatedById && user.FullName.Contains(search)));
        }

        if (query.SupplierId.HasValue)
        {
            source = source.Where(receipt => receipt.SupplierId == query.SupplierId.Value);
        }

        if (query.Status.HasValue)
        {
            source = source.Where(receipt => receipt.Status == query.Status.Value);
        }

        if (query.FromDate.HasValue)
        {
            var from = new DateTimeOffset(
                query.FromDate.Value.ToDateTime(TimeOnly.MinValue),
                TimeSpan.Zero);
            source = source.Where(receipt => receipt.CreatedAt >= from);
        }

        if (query.ToDate.HasValue)
        {
            var toExclusive = new DateTimeOffset(
                query.ToDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue),
                TimeSpan.Zero);
            source = source.Where(receipt => receipt.CreatedAt < toExclusive);
        }

        return source;
    }

    private async Task<string?> ValidateCreateCommandAsync(
        CreateImportReceiptCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Lines.Count == 0)
        {
            return "Phiếu nhập phải có ít nhất một dòng hàng.";
        }

        if (command.Lines.Count > 100)
        {
            return "Mỗi phiếu nhập không được vượt quá 100 dòng hàng.";
        }

        if (command.Lines.Any(line => line.ProductId <= 0 || line.Quantity <= 0 || line.UnitCost <= 0))
        {
            return "Sản phẩm, số lượng và đơn giá của mỗi dòng phải hợp lệ.";
        }

        if (command.Lines.Select(line => line.ProductId).Distinct().Count() != command.Lines.Count)
        {
            return "Mỗi sản phẩm chỉ được xuất hiện một lần trong phiếu nhập.";
        }

        if (!await dbContext.Suppliers.AnyAsync(
                supplier => supplier.SupplierId == command.SupplierId && supplier.IsActive,
                cancellationToken))
        {
            return "Nhà cung cấp không tồn tại hoặc đã ngừng hoạt động.";
        }

        if (!await dbContext.Warehouses.AnyAsync(
                warehouse => warehouse.WarehouseId == command.WarehouseId && warehouse.IsActive,
                cancellationToken))
        {
            return "Kho nhận không tồn tại hoặc đã ngừng hoạt động.";
        }

        var productIds = command.Lines.Select(line => line.ProductId).ToArray();
        var validProductCount = await dbContext.Products.CountAsync(
            product =>
                productIds.Contains(product.ProductId) &&
                product.SupplierId == command.SupplierId &&
                product.IsActive,
            cancellationToken);

        return validProductCount == productIds.Length
            ? null
            : "Có sản phẩm không tồn tại, ngừng kinh doanh hoặc không thuộc nhà cung cấp đã chọn.";
    }

    private async Task<string> GenerateReceiptNumberAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(3));
            var number = $"PN-{DateTime.UtcNow:yyyyMMdd}-{suffix}";
            if (!await dbContext.ImportReceipts.AnyAsync(
                    receipt => receipt.ReceiptNumber == number,
                    cancellationToken))
            {
                return number;
            }
        }

        throw new InvalidOperationException("Không thể tạo mã phiếu nhập duy nhất.");
    }

    private static object ReceiptSnapshot(ImportReceipt receipt) => new
    {
        receipt.ReceiptNumber,
        receipt.SupplierId,
        receipt.WarehouseId,
        receipt.Status,
        receipt.CreatedById,
        receipt.ApprovedById,
        receipt.CompletedById,
        receipt.RejectionReason,
        receipt.CreatedAt,
        receipt.ApprovedAt,
        receipt.CompletedAt,
        Lines = receipt.Details.Select(detail => new
        {
            detail.ProductId,
            detail.Quantity,
            detail.UnitCost
        })
    };

    private static bool TryParseRowVersion(string value, out byte[] rowVersion)
    {
        try
        {
            rowVersion = Convert.FromBase64String(value);
            return rowVersion.Length > 0;
        }
        catch (FormatException)
        {
            rowVersion = [];
            return false;
        }
    }
}
