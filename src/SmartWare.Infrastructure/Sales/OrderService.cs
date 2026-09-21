using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SmartWare.Application.Common;
using SmartWare.Application.Sales.Orders;
using SmartWare.Domain.Entities;
using SmartWare.Domain.Enums;
using SmartWare.Domain.Services;
using SmartWare.Infrastructure.Auditing;
using SmartWare.Infrastructure.Data;

namespace SmartWare.Infrastructure.Sales;

internal sealed class OrderService(ApplicationDbContext dbContext) : IOrderService
{
    public async Task<OrderPage> GetPageAsync(
        OrderQuery query,
        CancellationToken cancellationToken = default)
    {
        var source = ApplyFilters(query);
        var totalCount = await source.CountAsync(cancellationToken);
        var pageSize = Math.Clamp(query.PageSize, 5, 100);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Clamp(query.Page, 1, totalPages);

        var items = await source
            .OrderByDescending(order => order.OrderDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(order => new OrderListItem(
                order.OrderId,
                order.OrderNumber,
                order.Customer.Name,
                order.Details.Sum(detail => (int?)detail.Quantity) ?? 0,
                order.TotalAmount,
                order.Status,
                order.ExportReceipts
                    .OrderByDescending(receipt => receipt.CreatedAt)
                    .Select(receipt => receipt.ReceiptNumber)
                    .FirstOrDefault(),
                order.OrderDate,
                dbContext.Users
                    .Where(user => user.Id == order.CreatedById)
                    .Select(user => user.FullName)
                    .Single()))
            .ToListAsync(cancellationToken);

        var statistics = new OrderStatistics(
            await dbContext.Orders.CountAsync(cancellationToken),
            await dbContext.Orders.CountAsync(order => order.Status == OrderStatus.Pending, cancellationToken),
            await dbContext.Orders.CountAsync(order => order.Status == OrderStatus.Processing, cancellationToken),
            await dbContext.Orders.CountAsync(order => order.Status == OrderStatus.Shipping, cancellationToken),
            await dbContext.Orders.CountAsync(order => order.Status == OrderStatus.Completed, cancellationToken),
            await dbContext.Orders
                .Where(order => order.Status == OrderStatus.Completed)
                .SumAsync(order => (decimal?)order.TotalAmount, cancellationToken) ?? 0);

        return new OrderPage(
            new PagedResult<OrderListItem>(items, page, pageSize, totalCount),
            statistics);
    }

    public async Task<OrderDetails?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var order = await dbContext.Orders
            .AsNoTracking()
            .Include(item => item.Customer)
            .Include(item => item.Details)
                .ThenInclude(detail => detail.Product)
            .Include(item => item.ExportReceipts)
            .SingleOrDefaultAsync(item => item.OrderId == id, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var createdByName = await dbContext.Users
            .Where(user => user.Id == order.CreatedById)
            .Select(user => user.FullName)
            .SingleAsync(cancellationToken);
        var lines = order.Details
            .OrderBy(detail => detail.Product.Name)
            .Select(detail => new OrderLineDetails(
                detail.ProductId,
                detail.Product.Sku,
                detail.Product.Name,
                detail.Product.UnitOfMeasure,
                detail.Quantity,
                detail.UnitPrice,
                detail.Quantity * detail.UnitPrice))
            .ToArray();
        var receipts = order.ExportReceipts
            .OrderByDescending(receipt => receipt.CreatedAt)
            .Select(receipt => new OrderReceiptLink(
                receipt.ExportReceiptId,
                receipt.ReceiptNumber,
                receipt.Status,
                receipt.CreatedAt))
            .ToArray();

        return new OrderDetails(
            order.OrderId,
            order.OrderNumber,
            order.CustomerId,
            order.Customer.Code,
            order.Customer.Name,
            order.Customer.Phone,
            order.Customer.Address,
            order.Status,
            order.TotalAmount,
            lines.Sum(line => line.Quantity),
            order.OrderDate,
            order.CompletedAt,
            createdByName,
            order.CreatedAt,
            order.UpdatedAt,
            Convert.ToBase64String(order.RowVersion),
            lines,
            receipts);
    }

    public async Task<OrderFormOptions> GetFormOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var customers = await dbContext.Customers
            .AsNoTracking()
            .Where(customer => customer.IsActive)
            .OrderBy(customer => customer.Name)
            .Select(customer => new OrderLookupItem(customer.CustomerId, customer.Code, customer.Name))
            .ToListAsync(cancellationToken);
        var products = await dbContext.Products
            .AsNoTracking()
            .Where(product => product.IsActive)
            .OrderBy(product => product.Name)
            .Select(product => new OrderProductLookupItem(
                product.ProductId,
                product.Sku,
                product.Name,
                product.UnitOfMeasure,
                product.SellingPrice,
                product.Inventories
                    .Where(inventory => inventory.Warehouse.IsActive)
                    .Sum(inventory => (int?)(inventory.CurrentQuantity - inventory.ReservedQuantity)) ?? 0))
            .ToListAsync(cancellationToken);
        return new OrderFormOptions(customers, products);
    }

    public async Task<IReadOnlyList<OrderExportRow>> GetExportAsync(
        OrderQuery query,
        CancellationToken cancellationToken = default) =>
        await ApplyFilters(query)
            .OrderByDescending(order => order.OrderDate)
            .Select(order => new OrderExportRow(
                order.OrderNumber,
                order.Customer.Code,
                order.Customer.Name,
                order.OrderDate,
                order.Details.Sum(detail => (int?)detail.Quantity) ?? 0,
                order.TotalAmount,
                order.Status,
                order.ExportReceipts
                    .OrderByDescending(receipt => receipt.CreatedAt)
                    .Select(receipt => receipt.ReceiptNumber)
                    .FirstOrDefault(),
                dbContext.Users
                    .Where(user => user.Id == order.CreatedById)
                    .Select(user => user.FullName)
                    .Single()))
            .ToListAsync(cancellationToken);

    public async Task<OperationResult> CreateAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationError = await ValidateCreateCommandAsync(command, cancellationToken);
        if (validationError is not null)
        {
            return OperationResult.Failure(validationError);
        }

        decimal totalAmount;
        try
        {
            totalAmount = command.Lines.Sum(line => checked(line.Quantity * line.UnitPrice));
        }
        catch (OverflowException)
        {
            return OperationResult.Failure("Tổng tiền đơn hàng vượt giới hạn cho phép.");
        }

        if (totalAmount > 99_999_999_999_999.99m)
        {
            return OperationResult.Failure("Tổng tiền đơn hàng vượt giới hạn cho phép.");
        }

        var order = new Order
        {
            OrderNumber = await GenerateOrderNumberAsync(cancellationToken),
            CustomerId = command.CustomerId,
            Status = OrderStatus.Pending,
            TotalAmount = totalAmount,
            OrderDate = command.OrderDate.ToUniversalTime(),
            CreatedById = command.CreatedById,
            CreatedAt = DateTimeOffset.UtcNow,
            Details = command.Lines.Select(line => new OrderDetail
            {
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice
            }).ToList()
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.Orders.Add(order);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            AuditTrail.Add(
                dbContext,
                command.CreatedById,
                "Create",
                "Order",
                nameof(Order),
                order.OrderId.ToString(),
                null,
                Snapshot(order));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Success($"Đã tạo đơn hàng {order.OrderNumber}.");
        }
        catch (DbUpdateException)
        {
            return OperationResult.Failure("Không thể tạo đơn hàng. Vui lòng kiểm tra lại dữ liệu.");
        }
    }

    public async Task<OperationResult> ChangeStatusAsync(
        ChangeOrderStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseRowVersion(command.RowVersion, out var rowVersion))
        {
            return OperationResult.Failure("Phiên bản đơn hàng không hợp lệ. Vui lòng tải lại trang.");
        }

        var order = await dbContext.Orders
            .Include(item => item.ExportReceipts)
            .Include(item => item.Details)
            .SingleOrDefaultAsync(item => item.OrderId == command.Id, cancellationToken);
        if (order is null)
        {
            return OperationResult.Failure("Không tìm thấy đơn hàng.");
        }

        if (!OrderStatusTransition.CanTransition(order.Status, command.TargetStatus))
        {
            return OperationResult.Failure(
                $"Không thể chuyển đơn hàng từ {order.Status} sang {command.TargetStatus}.");
        }

        if (command.TargetStatus == OrderStatus.Cancelled && order.ExportReceipts.Any(
                receipt => receipt.Status is not (ReceiptStatus.Rejected or ReceiptStatus.Cancelled)))
        {
            return OperationResult.Failure(
                "Không thể hủy đơn khi còn phiếu xuất chưa được từ chối hoặc hủy.");
        }

        dbContext.Entry(order).Property(item => item.RowVersion).OriginalValue = rowVersion;
        var oldValues = Snapshot(order);
        order.Status = command.TargetStatus;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        order.CompletedAt = command.TargetStatus == OrderStatus.Completed
            ? DateTimeOffset.UtcNow
            : order.CompletedAt;

        AuditTrail.Add(
            dbContext,
            command.PerformedById,
            "ChangeStatus",
            "Order",
            nameof(Order),
            order.OrderId.ToString(),
            oldValues,
            Snapshot(order));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return OperationResult.Success(
                $"Đã chuyển trạng thái đơn {order.OrderNumber} sang {command.TargetStatus}.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult.Failure(
                "Đơn hàng đã được người khác cập nhật. Vui lòng tải lại trang.");
        }
        catch (DbUpdateException)
        {
            return OperationResult.Failure("Không thể cập nhật trạng thái đơn hàng.");
        }
    }

    private IQueryable<Order> ApplyFilters(OrderQuery query)
    {
        var source = dbContext.Orders.AsNoTracking();
        var search = query.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            source = source.Where(order =>
                order.OrderNumber.Contains(search) ||
                order.Customer.Code.Contains(search) ||
                order.Customer.Name.Contains(search));
        }

        if (query.Status.HasValue)
        {
            source = source.Where(order => order.Status == query.Status.Value);
        }

        if (query.FromDate.HasValue)
        {
            var from = new DateTimeOffset(
                query.FromDate.Value.ToDateTime(TimeOnly.MinValue),
                TimeSpan.Zero);
            source = source.Where(order => order.OrderDate >= from);
        }

        if (query.ToDate.HasValue)
        {
            var toExclusive = new DateTimeOffset(
                query.ToDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue),
                TimeSpan.Zero);
            source = source.Where(order => order.OrderDate < toExclusive);
        }

        return source;
    }

    private async Task<string?> ValidateCreateCommandAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Lines.Count == 0)
        {
            return "Đơn hàng phải có ít nhất một sản phẩm.";
        }

        if (command.Lines.Count > 100)
        {
            return "Mỗi đơn hàng không được vượt quá 100 dòng sản phẩm.";
        }

        if (command.Lines.Any(line =>
                line.ProductId <= 0 || line.Quantity <= 0 || line.UnitPrice < 0))
        {
            return "Sản phẩm, số lượng hoặc đơn giá không hợp lệ.";
        }

        if (command.Lines.Select(line => line.ProductId).Distinct().Count() != command.Lines.Count)
        {
            return "Mỗi sản phẩm chỉ được xuất hiện một lần trong đơn hàng.";
        }

        if (!await dbContext.Customers.AnyAsync(
                customer => customer.CustomerId == command.CustomerId && customer.IsActive,
                cancellationToken))
        {
            return "Khách hàng không tồn tại hoặc đã ngừng hoạt động.";
        }

        var productIds = command.Lines.Select(line => line.ProductId).ToArray();
        var validProductCount = await dbContext.Products.CountAsync(
            product => productIds.Contains(product.ProductId) && product.IsActive,
            cancellationToken);
        return validProductCount == productIds.Length
            ? null
            : "Có sản phẩm không tồn tại hoặc đã ngừng kinh doanh.";
    }

    private async Task<string> GenerateOrderNumberAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(3));
            var number = $"DH-{DateTime.UtcNow:yyyyMMdd}-{suffix}";
            if (!await dbContext.Orders.AnyAsync(
                    order => order.OrderNumber == number,
                    cancellationToken))
            {
                return number;
            }
        }

        throw new InvalidOperationException("Không thể tạo mã đơn hàng duy nhất.");
    }

    private static object Snapshot(Order order) => new
    {
        order.OrderNumber,
        order.CustomerId,
        order.Status,
        order.TotalAmount,
        order.OrderDate,
        order.CompletedAt,
        Lines = order.Details.Select(detail => new
        {
            detail.ProductId,
            detail.Quantity,
            detail.UnitPrice
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
