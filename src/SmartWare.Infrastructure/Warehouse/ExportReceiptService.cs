using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SmartWare.Application.Common;
using SmartWare.Application.Warehouse.Exports;
using SmartWare.Domain.Entities;
using SmartWare.Domain.Enums;
using SmartWare.Infrastructure.Auditing;
using SmartWare.Infrastructure.Data;

namespace SmartWare.Infrastructure.InventoryOperations;

internal sealed class ExportReceiptService(ApplicationDbContext dbContext) : IExportReceiptService
{
    public async Task<ExportReceiptPage> GetPageAsync(
        ExportReceiptQuery query,
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
            .Select(receipt => new ExportReceiptListItem(
                receipt.ExportReceiptId,
                receipt.ReceiptNumber,
                receipt.Order == null ? null : receipt.Order.OrderNumber,
                receipt.Order == null ? null : receipt.Order.Customer.Name,
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

        var statistics = new ExportReceiptStatistics(
            await dbContext.ExportReceipts.CountAsync(cancellationToken),
            await dbContext.ExportReceipts.CountAsync(
                receipt => receipt.Status == ReceiptStatus.Pending,
                cancellationToken),
            await dbContext.ExportReceipts.CountAsync(
                receipt => receipt.Status == ReceiptStatus.Approved,
                cancellationToken),
            await dbContext.ExportReceipts.CountAsync(
                receipt => receipt.Status == ReceiptStatus.Completed,
                cancellationToken),
            await dbContext.ExportReceiptDetails
                .Where(detail => detail.ExportReceipt.Status == ReceiptStatus.Completed)
                .SumAsync(
                    detail => (decimal?)(detail.Quantity * detail.UnitCost),
                    cancellationToken) ?? 0);

        var customers = await dbContext.Customers
            .AsNoTracking()
            .OrderBy(customer => customer.Name)
            .Select(customer => new ExportLookupItem(
                customer.CustomerId,
                customer.Code,
                customer.Name))
            .ToListAsync(cancellationToken);

        return new ExportReceiptPage(
            new PagedResult<ExportReceiptListItem>(items, page, pageSize, totalCount),
            statistics,
            customers);
    }

    public async Task<ExportReceiptDetails?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var receipt = await dbContext.ExportReceipts
            .AsNoTracking()
            .Include(item => item.Order)
                .ThenInclude(order => order!.Customer)
            .Include(item => item.Warehouse)
            .Include(item => item.Details)
                .ThenInclude(detail => detail.Product)
            .SingleOrDefaultAsync(item => item.ExportReceiptId == id, cancellationToken);

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
            .Select(detail => new ExportReceiptLineDetails(
                detail.ProductId,
                detail.Product.Sku,
                detail.Product.Name,
                detail.Product.UnitOfMeasure,
                detail.Quantity,
                detail.UnitCost,
                detail.Quantity * detail.UnitCost))
            .ToArray();

        return new ExportReceiptDetails(
            receipt.ExportReceiptId,
            receipt.ReceiptNumber,
            receipt.OrderId,
            receipt.Order?.OrderNumber,
            receipt.Order?.Customer.Name,
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

    public async Task<ExportReceiptFormOptions> GetFormOptionsAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var warehouses = await dbContext.Warehouses
            .AsNoTracking()
            .Where(warehouse => warehouse.IsActive)
            .OrderBy(warehouse => warehouse.Name)
            .Select(warehouse => new ExportLookupItem(
                warehouse.WarehouseId,
                warehouse.Code,
                warehouse.Name))
            .ToListAsync(cancellationToken);
        var selectedWarehouseId = warehouses.Any(item => item.Id == warehouseId)
            ? warehouseId!.Value
            : warehouses.FirstOrDefault()?.Id ?? 0;

        var products = await dbContext.Products
            .AsNoTracking()
            .Where(product => product.IsActive)
            .OrderBy(product => product.Name)
            .Select(product => new ExportProductLookupItem(
                product.ProductId,
                product.Sku,
                product.Name,
                product.Inventories
                    .Where(inventory => inventory.WarehouseId == selectedWarehouseId)
                    .Sum(inventory => (int?)inventory.CurrentQuantity) ?? 0,
                product.Inventories
                    .Where(inventory => inventory.WarehouseId == selectedWarehouseId)
                    .Sum(inventory => (int?)inventory.ReservedQuantity) ?? 0,
                product.Inventories
                    .Where(inventory => inventory.WarehouseId == selectedWarehouseId)
                    .Sum(inventory => (int?)(inventory.CurrentQuantity - inventory.ReservedQuantity)) ?? 0,
                product.Inventories
                    .Where(inventory => inventory.WarehouseId == selectedWarehouseId)
                    .Select(inventory => inventory.AverageCost)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        var orders = await dbContext.Orders
            .AsNoTracking()
            .Where(order =>
                order.Status == OrderStatus.Pending ||
                order.Status == OrderStatus.Processing)
            .OrderByDescending(order => order.OrderDate)
            .Select(order => new ExportOrderLookupItem(
                order.OrderId,
                order.OrderNumber,
                order.Customer.Name,
                order.Status))
            .ToListAsync(cancellationToken);

        return new ExportReceiptFormOptions(warehouses, products, orders);
    }

    public async Task<OperationResult> CreateAsync(
        CreateExportReceiptCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationError = await ValidateCreateCommandAsync(command, cancellationToken);
        if (validationError is not null)
        {
            return OperationResult.Failure(validationError);
        }

        var productIds = command.Lines.Select(line => line.ProductId).ToArray();
        var costs = await dbContext.Inventories
            .AsNoTracking()
            .Where(inventory =>
                inventory.WarehouseId == command.WarehouseId &&
                productIds.Contains(inventory.ProductId))
            .ToDictionaryAsync(
                inventory => inventory.ProductId,
                inventory => inventory.AverageCost,
                cancellationToken);
        var receipt = new ExportReceipt
        {
            ReceiptNumber = await GenerateReceiptNumberAsync(cancellationToken),
            OrderId = command.OrderId,
            WarehouseId = command.WarehouseId,
            Status = ReceiptStatus.Pending,
            CreatedById = command.CreatedById,
            CreatedAt = DateTimeOffset.UtcNow,
            Details = command.Lines.Select(line => new ExportReceiptDetail
            {
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                UnitCost = costs.GetValueOrDefault(line.ProductId)
            }).ToList()
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.ExportReceipts.Add(receipt);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            AuditTrail.Add(
                dbContext,
                command.CreatedById,
                "Create",
                "ExportReceipt",
                nameof(ExportReceipt),
                receipt.ExportReceiptId.ToString(),
                null,
                ReceiptSnapshot(receipt));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Success(
                $"Đã tạo phiếu xuất {receipt.ReceiptNumber} và chuyển sang chờ duyệt.");
        }
        catch (DbUpdateException)
        {
            return OperationResult.Failure(
                "Không thể tạo phiếu xuất. Vui lòng kiểm tra lại dữ liệu.");
        }
    }

    public async Task<OperationResult> ReviewAsync(
        ReviewExportReceiptCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseRowVersion(command.RowVersion, out var rowVersion))
        {
            return OperationResult.Failure(
                "Phiên bản phiếu xuất không hợp lệ. Vui lòng tải lại trang.");
        }

        if (!command.Approve && string.IsNullOrWhiteSpace(command.RejectionReason))
        {
            return OperationResult.Failure("Vui lòng nhập lý do từ chối.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var receipt = await dbContext.ExportReceipts
            .Include(item => item.Details)
            .Include(item => item.Reservations)
            .Include(item => item.Order)
            .SingleOrDefaultAsync(item => item.ExportReceiptId == command.Id, cancellationToken);
        if (receipt is null)
        {
            return OperationResult.Failure("Không tìm thấy phiếu xuất.");
        }

        if (receipt.Status != ReceiptStatus.Pending)
        {
            return OperationResult.Failure(
                "Chỉ phiếu đang chờ duyệt mới có thể được duyệt hoặc từ chối.");
        }

        dbContext.Entry(receipt).Property(item => item.RowVersion).OriginalValue = rowVersion;
        var oldValues = ReceiptSnapshot(receipt);
        var reviewedAt = DateTimeOffset.UtcNow;

        try
        {
            if (command.Approve)
            {
                var reservationError = await ReserveInventoryAsync(
                    receipt,
                    reviewedAt,
                    cancellationToken);
                if (reservationError is not null)
                {
                    return OperationResult.Failure(reservationError);
                }
            }

            receipt.Status = command.Approve ? ReceiptStatus.Approved : ReceiptStatus.Rejected;
            receipt.ApprovedById = command.ReviewedById;
            receipt.ApprovedAt = reviewedAt;
            receipt.RejectionReason = command.Approve ? null : command.RejectionReason!.Trim();

            AuditTrail.Add(
                dbContext,
                command.ReviewedById,
                command.Approve ? "Approve" : "Reject",
                "ExportReceipt",
                nameof(ExportReceipt),
                receipt.ExportReceiptId.ToString(),
                oldValues,
                ReceiptSnapshot(receipt));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Success(
                command.Approve
                    ? $"Đã duyệt phiếu xuất {receipt.ReceiptNumber} và giữ hàng trong kho."
                    : $"Đã từ chối phiếu xuất {receipt.ReceiptNumber}.");
        }
        catch (OverflowException)
        {
            return OperationResult.Failure("Số lượng giữ tồn vượt giới hạn cho phép.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult.Failure(
                "Tồn kho hoặc phiếu xuất đã được người khác cập nhật. Vui lòng tải lại trang.");
        }
        catch (DbUpdateException)
        {
            return OperationResult.Failure(
                "Không thể cập nhật phiếu xuất. Toàn bộ thay đổi đã được hoàn tác.");
        }
    }

    public async Task<OperationResult> CompleteAsync(
        CompleteExportReceiptCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseRowVersion(command.RowVersion, out var rowVersion))
        {
            return OperationResult.Failure(
                "Phiên bản phiếu xuất không hợp lệ. Vui lòng tải lại trang.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var receipt = await dbContext.ExportReceipts
            .Include(item => item.Details)
            .Include(item => item.Reservations)
            .Include(item => item.Order)
            .SingleOrDefaultAsync(item => item.ExportReceiptId == command.Id, cancellationToken);
        if (receipt is null)
        {
            return OperationResult.Failure("Không tìm thấy phiếu xuất.");
        }

        if (receipt.Status != ReceiptStatus.Approved)
        {
            return OperationResult.Failure("Chỉ phiếu đã duyệt mới có thể hoàn tất.");
        }

        if (receipt.Details.Count == 0)
        {
            return OperationResult.Failure("Phiếu xuất không có dòng hàng hợp lệ.");
        }

        if (receipt.Order is not null && IsTerminalOrder(receipt.Order.Status))
        {
            return OperationResult.Failure(
                "Đơn hàng liên kết đã kết thúc hoặc bị hủy nên không thể xuất kho.");
        }

        dbContext.Entry(receipt).Property(item => item.RowVersion).OriginalValue = rowVersion;
        var completedAt = DateTimeOffset.UtcNow;
        var productIds = receipt.Details.Select(detail => detail.ProductId).ToArray();
        var inventories = await dbContext.Inventories
            .Where(inventory =>
                inventory.WarehouseId == receipt.WarehouseId &&
                productIds.Contains(inventory.ProductId))
            .ToDictionaryAsync(inventory => inventory.ProductId, cancellationToken);
        var reservations = receipt.Reservations
            .Where(reservation => reservation.Status == ReservationStatus.Active)
            .ToDictionary(reservation => reservation.ProductId);

        foreach (var detail in receipt.Details)
        {
            if (!inventories.TryGetValue(detail.ProductId, out var inventory) ||
                !reservations.TryGetValue(detail.ProductId, out var reservation) ||
                reservation.Quantity != detail.Quantity ||
                inventory.CurrentQuantity < detail.Quantity ||
                inventory.ReservedQuantity < detail.Quantity)
            {
                return OperationResult.Failure(
                    "Dữ liệu giữ tồn không còn hợp lệ. Không có thay đổi nào được ghi nhận.");
            }
        }

        var oldValues = ReceiptSnapshot(receipt);

        try
        {
            foreach (var detail in receipt.Details.OrderBy(item => item.ProductId))
            {
                var inventory = inventories[detail.ProductId];
                var reservation = reservations[detail.ProductId];

                detail.UnitCost = inventory.AverageCost;
                inventory.CurrentQuantity = checked(inventory.CurrentQuantity - detail.Quantity);
                inventory.ReservedQuantity = checked(inventory.ReservedQuantity - detail.Quantity);
                reservation.Status = ReservationStatus.Fulfilled;
                reservation.FulfilledAt = completedAt;

                dbContext.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = detail.ProductId,
                    WarehouseId = receipt.WarehouseId,
                    Type = InventoryTransactionType.Out,
                    Quantity = detail.Quantity,
                    UnitCost = detail.UnitCost,
                    OccurredAt = completedAt,
                    ReferenceType = nameof(ExportReceipt),
                    ReferenceId = receipt.ExportReceiptId,
                    PerformedById = command.CompletedById
                });
            }

            receipt.Status = ReceiptStatus.Completed;
            receipt.CompletedById = command.CompletedById;
            receipt.CompletedAt = completedAt;
            if (receipt.Order is not null)
            {
                receipt.Order.Status = OrderStatus.Shipping;
                receipt.Order.UpdatedAt = completedAt;
            }

            AuditTrail.Add(
                dbContext,
                command.CompletedById,
                "Complete",
                "ExportReceipt",
                nameof(ExportReceipt),
                receipt.ExportReceiptId.ToString(),
                oldValues,
                ReceiptSnapshot(receipt));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Success(
                $"Đã hoàn tất phiếu xuất {receipt.ReceiptNumber} và cập nhật tồn kho.");
        }
        catch (OverflowException)
        {
            return OperationResult.Failure("Số lượng tồn kho vượt giới hạn cho phép.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult.Failure(
                "Dữ liệu tồn kho hoặc phiếu xuất đã thay đổi. Vui lòng thử lại.");
        }
        catch (DbUpdateException)
        {
            return OperationResult.Failure(
                "Không thể hoàn tất phiếu xuất. Toàn bộ thay đổi đã được hoàn tác.");
        }
    }

    public async Task<OperationResult> CancelAsync(
        CancelExportReceiptCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseRowVersion(command.RowVersion, out var rowVersion))
        {
            return OperationResult.Failure(
                "Phiên bản phiếu xuất không hợp lệ. Vui lòng tải lại trang.");
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return OperationResult.Failure("Vui lòng nhập lý do hủy phiếu.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var receipt = await dbContext.ExportReceipts
            .Include(item => item.Details)
            .Include(item => item.Reservations)
            .SingleOrDefaultAsync(item => item.ExportReceiptId == command.Id, cancellationToken);
        if (receipt is null)
        {
            return OperationResult.Failure("Không tìm thấy phiếu xuất.");
        }

        if (receipt.Status is not (ReceiptStatus.Pending or ReceiptStatus.Approved))
        {
            return OperationResult.Failure(
                "Chỉ phiếu đang chờ duyệt hoặc đã duyệt (chưa xuất kho) mới có thể hủy.");
        }

        dbContext.Entry(receipt).Property(item => item.RowVersion).OriginalValue = rowVersion;
        var oldValues = ReceiptSnapshot(receipt);
        var cancelledAt = DateTimeOffset.UtcNow;
        var activeReservations = receipt.Reservations
            .Where(reservation => reservation.Status == ReservationStatus.Active)
            .OrderBy(reservation => reservation.ProductId)
            .ToArray();
        var productIds = activeReservations.Select(reservation => reservation.ProductId).ToArray();
        var inventories = await dbContext.Inventories
            .Where(inventory =>
                inventory.WarehouseId == receipt.WarehouseId &&
                productIds.Contains(inventory.ProductId))
            .ToDictionaryAsync(inventory => inventory.ProductId, cancellationToken);

        try
        {
            foreach (var reservation in activeReservations)
            {
                if (!inventories.TryGetValue(reservation.ProductId, out var inventory) ||
                    inventory.ReservedQuantity < reservation.Quantity)
                {
                    return OperationResult.Failure(
                        "Dữ liệu giữ tồn không còn hợp lệ. Không có thay đổi nào được ghi nhận.");
                }

                inventory.ReservedQuantity = checked(inventory.ReservedQuantity - reservation.Quantity);
                reservation.Status = ReservationStatus.Released;
                reservation.ReleasedAt = cancelledAt;
            }

            receipt.Status = ReceiptStatus.Cancelled;
            receipt.RejectionReason = command.Reason.Trim();

            AuditTrail.Add(
                dbContext,
                command.CancelledById,
                "Cancel",
                "ExportReceipt",
                nameof(ExportReceipt),
                receipt.ExportReceiptId.ToString(),
                oldValues,
                ReceiptSnapshot(receipt));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Success(
                activeReservations.Length == 0
                    ? $"Đã hủy phiếu xuất {receipt.ReceiptNumber}."
                    : $"Đã hủy phiếu xuất {receipt.ReceiptNumber} và trả lại hàng đang giữ về tồn khả dụng.");
        }
        catch (OverflowException)
        {
            return OperationResult.Failure("Số lượng giữ tồn vượt giới hạn cho phép.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult.Failure(
                "Tồn kho hoặc phiếu xuất đã được người khác cập nhật. Vui lòng tải lại trang.");
        }
        catch (DbUpdateException)
        {
            return OperationResult.Failure(
                "Không thể hủy phiếu xuất. Toàn bộ thay đổi đã được hoàn tác.");
        }
    }

    private IQueryable<ExportReceipt> ApplyFilters(ExportReceiptQuery query)
    {
        var source = dbContext.ExportReceipts.AsNoTracking();
        var search = query.Search?.Trim();

        if (!string.IsNullOrWhiteSpace(search))
        {
            source = source.Where(receipt =>
                receipt.ReceiptNumber.Contains(search) ||
                (receipt.Order != null &&
                    (receipt.Order.OrderNumber.Contains(search) ||
                     receipt.Order.Customer.Name.Contains(search))) ||
                dbContext.Users.Any(user =>
                    user.Id == receipt.CreatedById && user.FullName.Contains(search)));
        }

        if (query.CustomerId.HasValue)
        {
            source = source.Where(receipt =>
                receipt.Order != null &&
                receipt.Order.CustomerId == query.CustomerId.Value);
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
        CreateExportReceiptCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Lines.Count == 0)
        {
            return "Phiếu xuất phải có ít nhất một dòng hàng.";
        }

        if (command.Lines.Count > 100)
        {
            return "Mỗi phiếu xuất không được vượt quá 100 dòng hàng.";
        }

        if (command.Lines.Any(line => line.ProductId <= 0 || line.Quantity <= 0))
        {
            return "Sản phẩm và số lượng của mỗi dòng phải hợp lệ.";
        }

        if (command.Lines.Select(line => line.ProductId).Distinct().Count() != command.Lines.Count)
        {
            return "Mỗi sản phẩm chỉ được xuất hiện một lần trong phiếu xuất.";
        }

        if (!await dbContext.Warehouses.AnyAsync(
                warehouse => warehouse.WarehouseId == command.WarehouseId && warehouse.IsActive,
                cancellationToken))
        {
            return "Kho xuất không tồn tại hoặc đã ngừng hoạt động.";
        }

        var productIds = command.Lines.Select(line => line.ProductId).ToArray();
        var validProductCount = await dbContext.Products.CountAsync(
            product => productIds.Contains(product.ProductId) && product.IsActive,
            cancellationToken);
        if (validProductCount != productIds.Length)
        {
            return "Có sản phẩm không tồn tại hoặc đã ngừng kinh doanh.";
        }

        if (command.OrderId.HasValue && !await dbContext.Orders.AnyAsync(
                order =>
                    order.OrderId == command.OrderId.Value &&
                    (order.Status == OrderStatus.Pending || order.Status == OrderStatus.Processing),
                cancellationToken))
        {
            return "Đơn hàng không tồn tại hoặc không còn ở trạng thái có thể xuất kho.";
        }

        return null;
    }

    private async Task<string?> ReserveInventoryAsync(
        ExportReceipt receipt,
        DateTimeOffset reservedAt,
        CancellationToken cancellationToken)
    {
        if (receipt.Reservations.Count != 0)
        {
            return "Phiếu xuất đã có dữ liệu giữ tồn và không thể duyệt lại.";
        }

        if (receipt.Order is not null && receipt.Order.Status is not
            (OrderStatus.Pending or OrderStatus.Processing))
        {
            return "Đơn hàng liên kết không còn ở trạng thái có thể giữ hàng.";
        }

        var productIds = receipt.Details.Select(detail => detail.ProductId).ToArray();
        var inventories = await dbContext.Inventories
            .Where(inventory =>
                inventory.WarehouseId == receipt.WarehouseId &&
                productIds.Contains(inventory.ProductId))
            .ToDictionaryAsync(inventory => inventory.ProductId, cancellationToken);

        foreach (var detail in receipt.Details.OrderBy(item => item.ProductId))
        {
            if (!inventories.TryGetValue(detail.ProductId, out var inventory) ||
                inventory.AvailableQuantity < detail.Quantity)
            {
                return $"Không đủ hàng khả dụng để duyệt sản phẩm mã {detail.ProductId}.";
            }
        }

        foreach (var detail in receipt.Details.OrderBy(item => item.ProductId))
        {
            var inventory = inventories[detail.ProductId];
            inventory.ReservedQuantity = checked(inventory.ReservedQuantity + detail.Quantity);
            receipt.Reservations.Add(new StockReservation
            {
                ProductId = detail.ProductId,
                WarehouseId = receipt.WarehouseId,
                Quantity = detail.Quantity,
                Status = ReservationStatus.Active,
                CreatedAt = reservedAt
            });
        }

        return null;
    }

    private async Task<string> GenerateReceiptNumberAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(3));
            var number = $"PX-{DateTime.UtcNow:yyyyMMdd}-{suffix}";
            if (!await dbContext.ExportReceipts.AnyAsync(
                    receipt => receipt.ReceiptNumber == number,
                    cancellationToken))
            {
                return number;
            }
        }

        throw new InvalidOperationException("Không thể tạo mã phiếu xuất duy nhất.");
    }

    private static bool IsTerminalOrder(OrderStatus status) => status is
        OrderStatus.Completed or
        OrderStatus.Cancelled or
        OrderStatus.DeliveryFailed or
        OrderStatus.Returned;

    private static object ReceiptSnapshot(ExportReceipt receipt) => new
    {
        receipt.ReceiptNumber,
        receipt.OrderId,
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
        }).ToArray(),
        Reservations = receipt.Reservations.Select(reservation => new
        {
            reservation.ProductId,
            reservation.Quantity,
            reservation.Status
        }).ToArray()
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
