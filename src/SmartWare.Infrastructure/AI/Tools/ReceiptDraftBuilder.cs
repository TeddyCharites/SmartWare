using Microsoft.EntityFrameworkCore;
using SmartWare.Application.AI.Chatbot;
using SmartWare.Domain.Entities;
using SmartWare.Domain.Enums;
using SmartWare.Infrastructure.Data;

namespace SmartWare.Infrastructure.AI.Tools;

/// <summary>
/// Turns what the user asked for ("nhập 50 RAM DDR5 và 20 SSD") into a receipt draft.
/// Read-only: it resolves products, supplier, warehouse, prices and stock, and reports anything
/// ambiguous as a warning. Nothing is created here; the draft is created only after the user
/// confirms it, through the regular receipt services and their validation.
/// </summary>
internal sealed class ReceiptDraftBuilder(ApplicationDbContext dbContext)
{
    private const string SearchCollation = "Latin1_General_100_CI_AI";

    public async Task<ChatReceiptDraft> BuildImportAsync(
        string? supplierKeyword,
        string? warehouseKeyword,
        IReadOnlyList<DraftLineRequest> requests,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var blocking = false;
        var products = await ResolveProductsAsync(requests, warnings, cancellationToken);
        blocking |= products.Blocking;
        var warehouse = await ResolveWarehouseAsync(warehouseKeyword, products.Lines, forExport: false, warnings, cancellationToken);
        blocking |= warehouse is null;

        // Every product of an import receipt must belong to the receipt's supplier.
        (int Id, string Name)? supplier = null;
        if (!string.IsNullOrWhiteSpace(supplierKeyword))
        {
            supplier = await ResolveSupplierAsync(supplierKeyword, warnings, cancellationToken);
            blocking |= supplier is null;
        }
        else
        {
            var suppliers = products.Lines.Select(line => (line.Product.SupplierId, line.Product.SupplierName)).Distinct().ToList();
            if (suppliers.Count == 1)
            {
                supplier = (suppliers[0].SupplierId, suppliers[0].SupplierName);
            }
            else if (suppliers.Count > 1)
            {
                warnings.Add($"Các sản phẩm thuộc {suppliers.Count} nhà cung cấp khác nhau ({string.Join(", ", suppliers.Select(item => item.SupplierName))}). Mỗi phiếu nhập chỉ có một nhà cung cấp, hãy tách thành nhiều phiếu.");
                blocking = true;
            }
        }

        var lines = new List<ChatReceiptDraftLine>();
        foreach (var line in products.Lines)
        {
            if (supplier is { } chosen && line.Product.SupplierId != chosen.Id)
            {
                warnings.Add($"{line.Product.Sku} thuộc nhà cung cấp {line.Product.SupplierName}, không thể nhập chung phiếu với {chosen.Name}.");
                blocking = true;
                continue;
            }

            var unitCost = line.RequestedUnitCost ?? await LastImportCostAsync(line.Product.Id, cancellationToken);
            if (unitCost <= 0)
            {
                warnings.Add($"{line.Product.Sku} chưa có đơn giá nhập. Hãy nêu đơn giá hoặc mở phiếu trong form để nhập.");
                blocking = true;
            }
            else if (line.RequestedUnitCost is null)
            {
                warnings.Add($"Đơn giá {line.Product.Sku} được lấy theo lần nhập gần nhất ({unitCost:N0} đ); hãy kiểm tra lại.");
            }

            lines.Add(new ChatReceiptDraftLine(
                line.Product.Id,
                line.Product.Sku,
                line.Product.Name,
                line.Product.UnitOfMeasure,
                line.Quantity,
                unitCost,
                null));
        }

        blocking |= lines.Count == 0;
        return new ChatReceiptDraft(
            Guid.Empty,
            ChatDraftTypes.Import,
            warehouse?.Id ?? 0,
            warehouse?.Name ?? string.Empty,
            supplier?.Id,
            supplier?.Name,
            null,
            null,
            lines,
            lines.Sum(line => line.Quantity * line.UnitCost),
            warnings,
            !blocking);
    }

    public async Task<ChatReceiptDraft> BuildExportAsync(
        string? warehouseKeyword,
        string? orderNumber,
        IReadOnlyList<DraftLineRequest> requests,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var blocking = false;
        var products = await ResolveProductsAsync(requests, warnings, cancellationToken);
        blocking |= products.Blocking;
        var warehouse = await ResolveWarehouseAsync(warehouseKeyword, products.Lines, forExport: true, warnings, cancellationToken);
        blocking |= warehouse is null;

        (int Id, string Number)? order = null;
        if (!string.IsNullOrWhiteSpace(orderNumber))
        {
            var number = orderNumber.Trim();
            var match = await dbContext.Orders
                .AsNoTracking()
                .Where(item => item.OrderNumber == number &&
                               (item.Status == OrderStatus.Pending || item.Status == OrderStatus.Processing))
                .Select(item => new { item.OrderId, item.OrderNumber })
                .FirstOrDefaultAsync(cancellationToken);
            if (match is null)
            {
                warnings.Add($"Không tìm thấy đơn hàng {number} đang chờ xử lý hoặc đang xử lý.");
                blocking = true;
            }
            else
            {
                order = (match.OrderId, match.OrderNumber);
            }
        }

        var productIds = products.Lines.Select(line => line.Product.Id).ToArray();
        var warehouseId = warehouse?.Id ?? 0;
        var stock = await dbContext.Inventories
            .AsNoTracking()
            .Where(item => item.WarehouseId == warehouseId && productIds.Contains(item.ProductId))
            .Select(item => new { item.ProductId, Available = item.CurrentQuantity - item.ReservedQuantity, item.AverageCost })
            .ToDictionaryAsync(item => item.ProductId, cancellationToken);

        var lines = new List<ChatReceiptDraftLine>();
        foreach (var line in products.Lines)
        {
            var available = stock.TryGetValue(line.Product.Id, out var item) ? item.Available : 0;
            if (line.Quantity > available)
            {
                warnings.Add($"{line.Product.Sku} chỉ còn {available:N0} {line.Product.UnitOfMeasure} khả dụng, không đủ để xuất {line.Quantity:N0}.");
                blocking = true;
            }

            lines.Add(new ChatReceiptDraftLine(
                line.Product.Id,
                line.Product.Sku,
                line.Product.Name,
                line.Product.UnitOfMeasure,
                line.Quantity,
                item?.AverageCost ?? 0,
                available));
        }

        blocking |= lines.Count == 0;
        return new ChatReceiptDraft(
            Guid.Empty,
            ChatDraftTypes.Export,
            warehouse?.Id ?? 0,
            warehouse?.Name ?? string.Empty,
            null,
            null,
            order?.Id,
            order?.Number,
            lines,
            lines.Sum(line => line.Quantity * line.UnitCost),
            warnings,
            !blocking);
    }

    private async Task<(int Id, string Name)?> ResolveWarehouseAsync(
        string? keyword,
        IReadOnlyList<ResolvedLine> lines,
        bool forExport,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var warehouses = await dbContext.Warehouses
            .AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => new { item.WarehouseId, item.Code, item.Name })
            .ToListAsync(cancellationToken);
        if (warehouses.Count == 0)
        {
            warnings.Add("Hệ thống chưa có kho nào đang hoạt động.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(keyword))
        {
            if (warehouses.Count == 1)
            {
                return (warehouses[0].WarehouseId, warehouses[0].Name);
            }

            // Several warehouses and none named: pick the one that holds these products (most
            // available stock for an export, most of the products for an import) and say so.
            var productIds = lines.Select(line => line.Product.Id).ToArray();
            var scores = await dbContext.Inventories
                .AsNoTracking()
                .Where(item => productIds.Contains(item.ProductId) && item.Warehouse.IsActive)
                .GroupBy(item => item.WarehouseId)
                .Select(group => new
                {
                    WarehouseId = group.Key,
                    Available = group.Sum(item => item.CurrentQuantity - item.ReservedQuantity),
                    Products = group.Count()
                })
                .ToListAsync(cancellationToken);
            var best = scores
                .OrderByDescending(item => forExport ? item.Available : item.Products)
                .ThenByDescending(item => forExport ? item.Products : item.Available)
                .Select(item => warehouses.First(warehouse => warehouse.WarehouseId == item.WarehouseId))
                .FirstOrDefault() ?? warehouses[0];
            warnings.Add($"Đã chọn {best.Name} (hệ thống có {warehouses.Count} kho). Hãy nêu tên kho nếu muốn dùng kho khác.");
            return (best.WarehouseId, best.Name);
        }

        var folded = ChatText.Normalize(keyword);
        var matches = warehouses
            .Where(item => ChatText.Normalize(item.Code) == folded || ChatText.Normalize(item.Name).Contains(folded, StringComparison.Ordinal))
            .ToList();
        if (matches.Count == 1)
        {
            return (matches[0].WarehouseId, matches[0].Name);
        }

        warnings.Add($"Không xác định được kho \"{keyword}\". Các kho hiện có: {string.Join(", ", warehouses.Select(item => item.Name))}.");
        return null;
    }

    private async Task<(int Id, string Name)?> ResolveSupplierAsync(
        string keyword,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var code = keyword.Trim();
        var suppliers = dbContext.Suppliers.AsNoTracking().Where(item => item.IsActive);
        var exact = await suppliers
            .Where(item => item.Code == code)
            .Select(item => new { item.SupplierId, item.Name })
            .ToListAsync(cancellationToken);
        var matches = exact.Count > 0
            ? exact
            : await WhereAllTerms(suppliers, ChatText.ExtractSearchTerms(keyword), (query, term) =>
                    query.Where(item => item.Code.Contains(term) || EF.Functions.Collate(item.Name, SearchCollation).Contains(term)))
                .Select(item => new { item.SupplierId, item.Name })
                .Take(6)
                .ToListAsync(cancellationToken);
        if (matches.Count == 1)
        {
            return (matches[0].SupplierId, matches[0].Name);
        }

        warnings.Add(matches.Count == 0
            ? $"Không tìm thấy nhà cung cấp \"{keyword}\"."
            : $"\"{keyword}\" khớp nhiều nhà cung cấp: {string.Join(", ", matches.Select(item => item.Name))}. Hãy nêu rõ hơn.");
        return null;
    }

    private async Task<ResolvedProducts> ResolveProductsAsync(
        IReadOnlyList<DraftLineRequest> requests,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var blocking = false;
        var resolved = new List<ResolvedLine>();
        if (requests.Count == 0)
        {
            warnings.Add("Chưa có dòng hàng nào. Hãy nêu sản phẩm và số lượng.");
            return new ResolvedProducts(resolved, true);
        }

        foreach (var request in requests)
        {
            if (string.IsNullOrWhiteSpace(request.Product) || request.Quantity <= 0)
            {
                warnings.Add($"Bỏ qua dòng \"{request.Product}\" vì thiếu sản phẩm hoặc số lượng không hợp lệ.");
                blocking = true;
                continue;
            }

            var candidates = await FindProductsAsync(request.Product, cancellationToken);
            if (candidates.Count > 1)
            {
                var folded = ChatText.Normalize(request.Product);
                var exactName = candidates.Where(item => ChatText.Normalize(item.Name) == folded).ToList();
                candidates = exactName.Count == 1 ? exactName : candidates;
            }

            if (candidates.Count != 1)
            {
                warnings.Add(candidates.Count == 0
                    ? $"Không tìm thấy sản phẩm \"{request.Product}\"."
                    : $"\"{request.Product}\" khớp nhiều sản phẩm: {string.Join(", ", candidates.Select(item => $"{item.Name} ({item.Sku})"))}. Hãy nêu rõ mã SKU.");
                blocking = true;
                continue;
            }

            var product = candidates[0];
            var existing = resolved.FindIndex(line => line.Product.Id == product.Id);
            if (existing >= 0)
            {
                // The same product mentioned twice becomes one line, as receipts require.
                var merged = resolved[existing];
                resolved[existing] = merged with
                {
                    Quantity = merged.Quantity + request.Quantity,
                    RequestedUnitCost = merged.RequestedUnitCost ?? request.UnitCost
                };
                warnings.Add($"{product.Sku} được nêu nhiều lần nên đã gộp thành một dòng.");
                continue;
            }

            resolved.Add(new ResolvedLine(product, request.Quantity, request.UnitCost));
        }

        return new ResolvedProducts(resolved, blocking);
    }

    private async Task<List<ProductInfo>> FindProductsAsync(string keyword, CancellationToken cancellationToken)
    {
        var products = dbContext.Products.AsNoTracking().Where(item => item.IsActive);
        var sku = keyword.Trim();
        var exact = await Project(products.Where(item => item.Sku == sku)).ToListAsync(cancellationToken);
        if (exact.Count > 0)
        {
            return exact;
        }

        // Every meaningful word must match (SKU or accent-insensitive name), which keeps
        // "màn hình" from matching every product that merely contains "hình".
        var terms = ChatText.ExtractSearchTerms(keyword, productLookup: true);
        if (terms.Count == 0)
        {
            return [];
        }

        // Order before projecting: EF Core cannot translate ordering on a constructor-projected record.
        var matches = WhereAllTerms(products, terms, (query, term) =>
                query.Where(item => item.Sku.Contains(term) || EF.Functions.Collate(item.Name, SearchCollation).Contains(term)))
            .OrderBy(item => item.Name)
            .Take(6);
        return await Project(matches).ToListAsync(cancellationToken);
    }

    private static IQueryable<T> WhereAllTerms<T>(
        IQueryable<T> query,
        IEnumerable<string> terms,
        Func<IQueryable<T>, string, IQueryable<T>> filter) =>
        terms.Aggregate(query, filter);

    private static IQueryable<ProductInfo> Project(IQueryable<Product> products) =>
        products.Select(item => new ProductInfo(
            item.ProductId,
            item.Sku,
            item.Name,
            item.UnitOfMeasure,
            item.SupplierId,
            item.Supplier.Name));

    private async Task<decimal> LastImportCostAsync(int productId, CancellationToken cancellationToken)
    {
        var lastCost = await dbContext.ImportReceiptDetails
            .AsNoTracking()
            .Where(item => item.ProductId == productId &&
                           item.ImportReceipt.Status != ReceiptStatus.Rejected &&
                           item.ImportReceipt.Status != ReceiptStatus.Cancelled)
            .OrderByDescending(item => item.ImportReceipt.CreatedAt)
            .Select(item => (decimal?)item.UnitCost)
            .FirstOrDefaultAsync(cancellationToken);
        if (lastCost is > 0)
        {
            return lastCost.Value;
        }

        return await dbContext.Inventories
            .AsNoTracking()
            .Where(item => item.ProductId == productId)
            .Select(item => (decimal?)item.AverageCost)
            .MaxAsync(cancellationToken) ?? 0;
    }

    private sealed record ProductInfo(int Id, string Sku, string Name, string UnitOfMeasure, int SupplierId, string SupplierName);

    private sealed record ResolvedLine(ProductInfo Product, int Quantity, decimal? RequestedUnitCost);

    private sealed record ResolvedProducts(List<ResolvedLine> Lines, bool Blocking);
}
