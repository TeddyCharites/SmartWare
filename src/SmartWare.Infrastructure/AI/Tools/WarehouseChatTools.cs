using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartWare.Application.AI.Chatbot;
using SmartWare.Application.AI.Rag;
using SmartWare.Application.Reports;
using SmartWare.Domain.Constants;
using SmartWare.Domain.Entities;
using SmartWare.Domain.Enums;
using SmartWare.Infrastructure.Data;

namespace SmartWare.Infrastructure.AI.Tools;

/// <summary>
/// Result of one read-only tool call: the text sent back to Gemini as the function response,
/// the data sources shown to the user, and an optional pre-formatted answer used when Gemini
/// is unavailable.
/// </summary>
internal sealed record ChatToolResult(
    string Content,
    IReadOnlyList<string> Sources,
    bool UsedRag = false,
    string? FallbackAnswer = null,
    ChatReceiptDraft? Draft = null)
{
    public static ChatToolResult Empty(string source) => new(string.Empty, [source]);
}

internal interface IWarehouseChatTools
{
    Task<ChatToolResult> ExecuteAsync(
        string toolName,
        JsonElement arguments,
        string role,
        CancellationToken cancellationToken = default);

    Task<bool> HasMatchingProductAsync(string text, CancellationToken cancellationToken = default);
}

internal sealed class WarehouseChatTools(
    ApplicationDbContext dbContext,
    IReportService reportService,
    IKnowledgeService knowledgeService,
    IOptions<GeminiOptions> options) : IWarehouseChatTools
{
    private const string SearchCollation = "Latin1_General_100_CI_AI";
    private readonly GeminiOptions _options = options.Value;
    private readonly ReceiptDraftBuilder _draftBuilder = new(dbContext);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public async Task<ChatToolResult> ExecuteAsync(
        string toolName,
        JsonElement arguments,
        string role,
        CancellationToken cancellationToken = default)
    {
        // Defense in depth: Gemini only receives the tools of the current role, but a model
        // could still name another tool, so the role is checked again before any query runs.
        if (!ChatToolCatalog.IsAllowed(toolName, role))
        {
            return new ChatToolResult(
                $"TỪ CHỐI: vai trò {role} không được phép dùng công cụ '{toolName}'. Hãy báo người dùng rằng họ không có quyền xem nhóm dữ liệu này.",
                []);
        }

        return toolName switch
        {
            ChatToolCatalog.LookupStock => await LookupStockAsync(
                ChatToolArguments.GetString(arguments, "keyword") ?? string.Empty,
                cancellationToken),
            ChatToolCatalog.ListLowStock => await ListLowStockAsync(
                ChatToolArguments.GetInt(arguments, "limit", 20, 1, 30),
                cancellationToken),
            ChatToolCatalog.AnalyzeMovements => await AnalyzeMovementsAsync(
                ChatToolArguments.GetDateRange(arguments, Today),
                ChatToolArguments.GetString(arguments, "product_keyword"),
                cancellationToken),
            ChatToolCatalog.LookupSuppliers => await LookupSuppliersAsync(
                ChatToolArguments.GetString(arguments, "keyword"),
                role,
                cancellationToken),
            ChatToolCatalog.ListReceipts => await ListReceiptsAsync(
                ChatToolArguments.GetString(arguments, "receipt_type") ?? "all",
                ChatToolArguments.GetString(arguments, "status") ?? "open",
                role,
                cancellationToken),
            ChatToolCatalog.InventoryOverview => await GetOverviewAsync(cancellationToken),
            ChatToolCatalog.SearchKnowledge => await SearchKnowledgeAsync(
                ChatToolArguments.GetString(arguments, "query", 1000) ?? string.Empty,
                role,
                cancellationToken),
            ChatToolCatalog.BusinessReport => await GetBusinessReportAsync(
                ChatToolArguments.GetDateRange(arguments, Today),
                cancellationToken),
            ChatToolCatalog.SuggestReplenishment => await SuggestReplenishmentAsync(
                ChatToolArguments.GetInt(arguments, "limit", 15, 1, 30),
                cancellationToken),
            ChatToolCatalog.LookupOrders => await LookupOrdersAsync(
                ChatToolArguments.GetString(arguments, "keyword"),
                ChatToolArguments.GetString(arguments, "status"),
                cancellationToken),
            ChatToolCatalog.DraftImportReceipt => DraftResult(await _draftBuilder.BuildImportAsync(
                ChatToolArguments.GetString(arguments, "supplier"),
                ChatToolArguments.GetString(arguments, "warehouse"),
                ChatToolArguments.GetDraftLines(arguments),
                cancellationToken)),
            ChatToolCatalog.DraftExportReceipt => DraftResult(await _draftBuilder.BuildExportAsync(
                ChatToolArguments.GetString(arguments, "warehouse"),
                ChatToolArguments.GetString(arguments, "order_number"),
                ChatToolArguments.GetDraftLines(arguments),
                cancellationToken)),
            _ => new ChatToolResult($"Công cụ '{toolName}' không tồn tại.", [])
        };
    }

    public Task<bool> HasMatchingProductAsync(string text, CancellationToken cancellationToken = default)
    {
        var terms = ChatText.ExtractSearchTerms(text, productLookup: true);
        return terms.Count == 0
            ? Task.FromResult(false)
            : MatchProducts(terms).AnyAsync(cancellationToken);
    }

    internal async Task<ChatToolResult> LookupStockAsync(
        string keyword,
        CancellationToken cancellationToken)
    {
        const string source = "SQL Server: sản phẩm và tồn kho hiện tại";
        var rows = await MatchProducts(ChatText.ExtractSearchTerms(keyword, productLookup: true))
            .OrderBy(product => product.Name)
            .Take(15)
            .Select(product => new
            {
                product.Sku,
                product.Name,
                Category = product.Category.Name,
                Supplier = product.Supplier.Name,
                product.UnitOfMeasure,
                product.MinStock,
                product.MaxStock,
                Current = product.Inventories
                    .Where(item => item.Warehouse.IsActive)
                    .Sum(item => (int?)item.CurrentQuantity) ?? 0,
                Reserved = product.Inventories
                    .Where(item => item.Warehouse.IsActive)
                    .Sum(item => (int?)item.ReservedQuantity) ?? 0,
                Value = product.Inventories
                    .Where(item => item.Warehouse.IsActive)
                    .Sum(item => (decimal?)(item.CurrentQuantity * item.AverageCost)) ?? 0
            })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return ChatToolResult.Empty(source);
        }

        var content = new StringBuilder("TỒN KHO HIỆN TẠI (tối đa 15 sản phẩm):\n");
        var fallbackAnswer = new StringBuilder(
            rows.Count == 1
                ? "Đã tìm thấy sản phẩm:\n\n"
                : $"Đã tìm thấy {rows.Count} sản phẩm phù hợp:\n\n");
        foreach (var item in rows)
        {
            var available = item.Current - item.Reserved;
            var averageCost = item.Current == 0 ? 0 : item.Value / item.Current;
            content.AppendLine(
                $"- {item.Sku} | {item.Name} | danh mục: {item.Category} | NCC: {item.Supplier} | " +
                $"tồn thực tế: {item.Current:N0} {item.UnitOfMeasure} | đã giữ: {item.Reserved:N0} | " +
                $"khả dụng: {available:N0} | min/max: {item.MinStock:N0}/{item.MaxStock:N0} | giá vốn TB: {averageCost:N0} đ");
            fallbackAnswer.AppendLine(
                $"- **{item.Sku} — {item.Name}**: tồn thực tế **{item.Current:N0} {item.UnitOfMeasure}**, " +
                $"đã giữ **{item.Reserved:N0}**, khả dụng **{available:N0}**; " +
                $"mức tối thiểu/tối đa **{item.MinStock:N0}/{item.MaxStock:N0}**; " +
                $"giá vốn bình quân **{averageCost:N0} đ**. Nhà cung cấp: {item.Supplier}.");
        }

        return new ChatToolResult(
            content.ToString(),
            [source],
            FallbackAnswer: fallbackAnswer.ToString().TrimEnd());
    }

    internal async Task<ChatToolResult> ListLowStockAsync(int limit, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Products
            .AsNoTracking()
            .Where(product => product.IsActive)
            .Select(product => new
            {
                product.Sku,
                product.Name,
                product.UnitOfMeasure,
                product.MinStock,
                product.MaxStock,
                Available = product.Inventories
                    .Where(item => item.Warehouse.IsActive)
                    .Sum(item => (int?)(item.CurrentQuantity - item.ReservedQuantity)) ?? 0
            })
            .Where(item => item.Available <= item.MinStock)
            .OrderBy(item => item.Available - item.MinStock)
            .ThenBy(item => item.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var content = new StringBuilder($"SẢN PHẨM CHẠM HOẶC DƯỚI MỨC TỒN TỐI THIỂU (tối đa {limit}):\n");
        if (rows.Count == 0)
        {
            content.AppendLine("- Không có sản phẩm nào đang chạm mức tồn tối thiểu.");
        }

        foreach (var item in rows)
        {
            content.AppendLine(
                $"- {item.Sku} | {item.Name} | khả dụng: {item.Available:N0} {item.UnitOfMeasure} | " +
                $"min/max: {item.MinStock:N0}/{item.MaxStock:N0} | thiếu so với min: {Math.Max(0, item.MinStock - item.Available):N0} | " +
                $"cần nhập để đạt max: {Math.Max(0, item.MaxStock - item.Available):N0}");
        }

        return new ChatToolResult(content.ToString(), ["SQL Server: cảnh báo tồn kho hiện tại"]);
    }

    internal async Task<ChatToolResult> AnalyzeMovementsAsync(
        (DateOnly From, DateOnly To) period,
        string? productKeyword,
        CancellationToken cancellationToken)
    {
        var from = ToUtcBoundary(period.From);
        var toExclusive = ToUtcBoundary(period.To.AddDays(1));
        var transactions = dbContext.InventoryTransactions
            .AsNoTracking()
            .Where(item => item.OccurredAt >= from && item.OccurredAt < toExclusive);

        var filterText = string.Empty;
        if (!string.IsNullOrWhiteSpace(productKeyword))
        {
            var productIds = await MatchProducts(ChatText.ExtractSearchTerms(productKeyword, productLookup: true))
                .Select(product => product.ProductId)
                .Take(50)
                .ToListAsync(cancellationToken);
            if (productIds.Count == 0)
            {
                return new ChatToolResult(
                    $"Không tìm thấy sản phẩm khớp với '{productKeyword}'.",
                    ["SQL Server: sản phẩm"]);
            }

            transactions = transactions.Where(item => productIds.Contains(item.ProductId));
            filterText = $" (lọc theo sản phẩm '{productKeyword}')";
        }

        var totals = await transactions
            .GroupBy(item => item.Type)
            .Select(group => new
            {
                group.Key,
                Quantity = group.Sum(item => item.Quantity),
                Value = group.Sum(item => item.Quantity * item.UnitCost)
            })
            .ToListAsync(cancellationToken);
        var topProducts = await transactions
            .GroupBy(item => new { item.Type, item.Product.Sku, item.Product.Name })
            .Select(group => new
            {
                group.Key.Type,
                group.Key.Sku,
                group.Key.Name,
                Quantity = group.Sum(item => item.Quantity),
                Value = group.Sum(item => item.Quantity * item.UnitCost)
            })
            .OrderByDescending(item => item.Quantity)
            .Take(20)
            .ToListAsync(cancellationToken);

        var inbound = totals.SingleOrDefault(item => item.Key == InventoryTransactionType.In);
        var outbound = totals.SingleOrDefault(item => item.Key == InventoryTransactionType.Out);
        var content = new StringBuilder(
            $"GIAO DỊCH KHO TỪ {period.From:dd/MM/yyyy} ĐẾN {period.To:dd/MM/yyyy}{filterText}:\n");
        content.AppendLine($"- Nhập: {inbound?.Quantity ?? 0:N0} đơn vị; giá trị: {inbound?.Value ?? 0:N0} đ.");
        content.AppendLine($"- Xuất: {outbound?.Quantity ?? 0:N0} đơn vị; giá vốn: {outbound?.Value ?? 0:N0} đ.");
        content.AppendLine($"- Chênh lệch nhập - xuất: {(inbound?.Quantity ?? 0) - (outbound?.Quantity ?? 0):N0} đơn vị.");
        AppendTop("TOP SẢN PHẨM XUẤT NHIỀU:", InventoryTransactionType.Out, "xuất", "giá vốn");
        AppendTop("TOP SẢN PHẨM NHẬP NHIỀU:", InventoryTransactionType.In, "nhập", "giá trị");

        return new ChatToolResult(
            content.ToString(),
            [$"SQL Server: giao dịch IN/OUT {period.From:dd/MM/yyyy}–{period.To:dd/MM/yyyy}"]);

        void AppendTop(string title, InventoryTransactionType type, string verb, string valueLabel)
        {
            content.AppendLine(title);
            foreach (var item in topProducts.Where(item => item.Type == type).Take(8))
            {
                content.AppendLine($"- {item.Sku} | {item.Name} | {verb}: {item.Quantity:N0} | {valueLabel}: {item.Value:N0} đ.");
            }
        }
    }

    internal async Task<ChatToolResult> LookupSuppliersAsync(
        string? keyword,
        string role,
        CancellationToken cancellationToken)
    {
        const string source = "SQL Server: nhà cung cấp và phiếu nhập được phép xem";
        var suppliers = dbContext.Suppliers.AsNoTracking().Where(item => item.IsActive);
        IReadOnlyList<string> terms = keyword is null ? [] : ChatText.ExtractSearchTerms(keyword);
        if (terms.Count > 0)
        {
            var term = terms[0];
            suppliers = suppliers.Where(item =>
                item.Code.Contains(term) ||
                EF.Functions.Collate(item.Name, SearchCollation).Contains(term));
        }

        var from = DateTimeOffset.UtcNow.AddDays(-30);
        var rows = await suppliers
            .OrderBy(item => item.Name)
            .Take(15)
            .Select(item => new
            {
                item.Code,
                item.Name,
                item.ContactName,
                item.Phone,
                ProductCount = item.Products.Count(product => product.IsActive),
                ReceiptCount = item.ImportReceipts.Count(receipt =>
                    receipt.Status == ReceiptStatus.Completed && receipt.CompletedAt >= from),
                ImportQuantity = item.ImportReceipts
                    .Where(receipt => receipt.Status == ReceiptStatus.Completed && receipt.CompletedAt >= from)
                    .SelectMany(receipt => receipt.Details)
                    .Sum(detail => (int?)detail.Quantity) ?? 0,
                ImportValue = item.ImportReceipts
                    .Where(receipt => receipt.Status == ReceiptStatus.Completed && receipt.CompletedAt >= from)
                    .SelectMany(receipt => receipt.Details)
                    .Sum(detail => (decimal?)(detail.Quantity * detail.UnitCost)) ?? 0
            })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return ChatToolResult.Empty(source);
        }

        var canSeeMetrics = role is RoleNames.Admin or RoleNames.Manager;
        var content = new StringBuilder("NHÀ CUNG CẤP (tối đa 15; thống kê 30 ngày):\n");
        foreach (var item in rows)
        {
            content.Append($"- {item.Code} | {item.Name} | sản phẩm đang dùng: {item.ProductCount:N0}");
            if (role == RoleNames.Admin)
            {
                content.Append($" | liên hệ: {item.ContactName ?? "chưa có"} | điện thoại: {item.Phone ?? "chưa có"}");
            }

            if (canSeeMetrics)
            {
                content.Append($" | phiếu nhập hoàn tất: {item.ReceiptCount:N0} | số lượng nhập: {item.ImportQuantity:N0} | giá trị nhập: {item.ImportValue:N0} đ");
            }

            content.AppendLine();
        }

        return new ChatToolResult(content.ToString(), [source]);
    }

    internal async Task<ChatToolResult> ListReceiptsAsync(
        string receiptType,
        string status,
        string role,
        CancellationToken cancellationToken)
    {
        var statuses = ParseReceiptStatuses(status);
        var includeImports = receiptType is not "export";
        var includeExports = receiptType is not "import";
        var content = new StringBuilder($"PHIẾU KHO (trạng thái: {DescribeStatusFilter(status)}; tối đa 10 phiếu mỗi loại, mới nhất trước):\n");

        if (includeImports)
        {
            var imports = dbContext.ImportReceipts.AsNoTracking()
                .Where(receipt => statuses.Contains(receipt.Status));
            var count = await imports.CountAsync(cancellationToken);
            var rows = await imports
                .OrderByDescending(receipt => receipt.CreatedAt)
                .Take(10)
                .Select(receipt => new
                {
                    receipt.ReceiptNumber,
                    receipt.Status,
                    receipt.CreatedAt,
                    Supplier = receipt.Supplier.Name,
                    Warehouse = receipt.Warehouse.Name,
                    Quantity = receipt.Details.Sum(detail => (int?)detail.Quantity) ?? 0,
                    Value = receipt.Details.Sum(detail => (decimal?)(detail.Quantity * detail.UnitCost)) ?? 0
                })
                .ToListAsync(cancellationToken);
            content.AppendLine($"PHIẾU NHẬP: tổng {count:N0} phiếu.");
            foreach (var item in rows)
            {
                content.AppendLine(
                    $"- {item.ReceiptNumber} | {StatusText(item.Status)} | tạo: {item.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm} | " +
                    $"NCC: {item.Supplier} | kho: {item.Warehouse} | số lượng: {item.Quantity:N0} | giá trị: {item.Value:N0} đ");
            }
        }

        if (includeExports)
        {
            var exports = dbContext.ExportReceipts.AsNoTracking()
                .Where(receipt => statuses.Contains(receipt.Status));
            var count = await exports.CountAsync(cancellationToken);
            var rows = await exports
                .OrderByDescending(receipt => receipt.CreatedAt)
                .Take(10)
                .Select(receipt => new
                {
                    receipt.ReceiptNumber,
                    receipt.Status,
                    receipt.CreatedAt,
                    Warehouse = receipt.Warehouse.Name,
                    OrderNumber = receipt.Order != null ? receipt.Order.OrderNumber : null,
                    Customer = receipt.Order != null ? receipt.Order.Customer.Name : null,
                    Quantity = receipt.Details.Sum(detail => (int?)detail.Quantity) ?? 0
                })
                .ToListAsync(cancellationToken);
            content.AppendLine($"PHIẾU XUẤT: tổng {count:N0} phiếu.");
            foreach (var item in rows)
            {
                content.Append(
                    $"- {item.ReceiptNumber} | {StatusText(item.Status)} | tạo: {item.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm} | " +
                    $"kho: {item.Warehouse} | số lượng: {item.Quantity:N0}");
                if (item.OrderNumber is not null)
                {
                    content.Append($" | đơn hàng: {item.OrderNumber}");
                    if (role == RoleNames.Admin)
                    {
                        content.Append($" ({item.Customer})");
                    }
                }

                content.AppendLine();
            }
        }

        return new ChatToolResult(content.ToString(), ["SQL Server: phiếu nhập/xuất kho"]);
    }

    internal async Task<ChatToolResult> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var totals = await dbContext.Inventories
            .AsNoTracking()
            .Where(item => item.Product.IsActive && item.Warehouse.IsActive)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                ProductCount = group.Select(item => item.ProductId).Distinct().Count(),
                Current = group.Sum(item => item.CurrentQuantity),
                Reserved = group.Sum(item => item.ReservedQuantity),
                Value = group.Sum(item => item.CurrentQuantity * item.AverageCost)
            })
            .SingleOrDefaultAsync(cancellationToken);
        var lowStockCount = await dbContext.Products
            .AsNoTracking()
            .CountAsync(product =>
                product.IsActive &&
                (product.Inventories
                    .Where(item => item.Warehouse.IsActive)
                    .Sum(item => (int?)(item.CurrentQuantity - item.ReservedQuantity)) ?? 0) <= product.MinStock,
                cancellationToken);
        var content = $"""
            TỔNG QUAN TỒN KHO HIỆN TẠI:
            - Sản phẩm có tồn: {totals?.ProductCount ?? 0:N0}.
            - Tồn thực tế: {totals?.Current ?? 0:N0} đơn vị.
            - Đã giữ cho phiếu xuất: {totals?.Reserved ?? 0:N0} đơn vị.
            - Tồn khả dụng: {(totals?.Current ?? 0) - (totals?.Reserved ?? 0):N0} đơn vị.
            - Giá trị tồn theo giá vốn bình quân: {totals?.Value ?? 0:N0} đ.
            - Sản phẩm chạm hoặc dưới mức tối thiểu: {lowStockCount:N0}.
            """;
        return new ChatToolResult(content, ["SQL Server: tổng quan tồn kho hiện tại"]);
    }

    internal async Task<ChatToolResult> SearchKnowledgeAsync(
        string query,
        string role,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new ChatToolResult("Thiếu nội dung cần tìm.", []);
        }

        var hits = await knowledgeService.SearchAsync(
            query,
            role,
            Math.Clamp(_options.RagTopK, 1, 10),
            cancellationToken);
        if (hits.Count == 0)
        {
            return new ChatToolResult(
                "Không tìm thấy tài liệu phù hợp trong kho tri thức mà vai trò này được phép xem.",
                [],
                UsedRag: true);
        }

        var content = new StringBuilder("TÀI LIỆU ĐƯỢC TRUY XUẤT BẰNG RAG:\n");
        foreach (var hit in hits)
        {
            content.AppendLine($"[Tài liệu: {hit.DocumentTitle} | Nhóm: {hit.Category} | Điểm: {hit.Score:F3}]");
            content.AppendLine(hit.Content);
            content.AppendLine();
        }

        return new ChatToolResult(
            content.ToString(),
            hits.Select(hit => $"RAG: {hit.DocumentTitle}").Distinct(StringComparer.Ordinal).ToArray(),
            UsedRag: true);
    }

    internal async Task<ChatToolResult> GetBusinessReportAsync(
        (DateOnly From, DateOnly To) period,
        CancellationToken cancellationToken)
    {
        var report = await reportService.GetAsync(
            new ReportQuery(period.From, period.To),
            cancellationToken);
        var content = new StringBuilder($"BÁO CÁO TỪ {period.From:dd/MM/yyyy} ĐẾN {period.To:dd/MM/yyyy} ({report.Period.DayCount} ngày):\n");
        content.AppendLine($"- Số lượng nhập: {report.Kpis.ImportQuantity:N0}; giá trị nhập: {report.Kpis.ImportValue:N0} đ.");
        content.AppendLine($"- Số lượng xuất: {report.Kpis.ExportQuantity:N0}; COGS: {report.Kpis.CostOfGoodsSold:N0} đ.");
        content.AppendLine($"- Doanh thu đơn hoàn tất: {report.Kpis.Revenue:N0} đ; lợi nhuận gộp: {report.Kpis.GrossProfit:N0} đ.");
        content.AppendLine($"- Giá trị tồn hiện tại: {report.Kpis.InventoryValue:N0} đ.");
        content.AppendLine("TOP SẢN PHẨM THEO LƯỢNG XUẤT:");
        foreach (var item in report.Products.OrderByDescending(item => item.ExportQuantity).Take(8))
        {
            content.AppendLine($"- {item.Sku} | {item.ProductName} | xuất: {item.ExportQuantity:N0} | khả dụng: {item.AvailableQuantity:N0} | doanh thu: {item.Revenue:N0} đ.");
        }

        return new ChatToolResult(
            content.ToString(),
            [$"SQL Server: báo cáo kho {period.From:dd/MM/yyyy}–{period.To:dd/MM/yyyy}"]);
    }

    internal async Task<ChatToolResult> SuggestReplenishmentAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        var today = Today;
        var report = await reportService.GetAsync(
            new ReportQuery(today.AddDays(-29), today),
            cancellationToken);
        var risky = report.Forecasts.Where(item => item.RiskLevel != "Thấp").Take(limit).ToList();
        var content = new StringBuilder(
            "GỢI Ý NHẬP HÀNG (dự báo SMA cửa sổ 7 ngày trên lịch sử 30 ngày, cho 30 ngày tới; sắp xếp theo mức rủi ro):\n");
        if (risky.Count == 0)
        {
            content.AppendLine("- Không có sản phẩm nào có rủi ro thiếu hàng ở mức trung bình trở lên.");
        }

        foreach (var item in risky)
        {
            content.AppendLine(
                $"- {item.Sku} | {item.ProductName} | rủi ro: {item.RiskLevel} | khả dụng: {item.AvailableQuantity:N0} | " +
                $"min/max: {item.MinimumStock:N0}/{item.MaximumStock:N0} | nhu cầu TB/ngày: {item.AverageDailyDemand:N1} | " +
                $"dự báo 30 ngày: {(item.HasHistory ? item.ForecastThirtyDays : 0):N0} | " +
                $"số ngày đến khi hết hàng: {FormatDays(item.DaysUntilOutOfStock)} | " +
                $"đề xuất nhập: {item.SuggestedReplenishment:N0} | " +
                $"sai số MAPE: {(item.Mape.HasValue ? $"{item.Mape.Value:N1}%" : "chưa đủ dữ liệu")}");
        }

        return new ChatToolResult(content.ToString(), ["SQL Server: dự báo nhu cầu SMA (lịch sử 30 ngày)"]);
    }

    internal async Task<ChatToolResult> LookupOrdersAsync(
        string? keyword,
        string? status,
        CancellationToken cancellationToken)
    {
        const string source = "SQL Server: đơn hàng và khách hàng (Admin)";
        var orders = dbContext.Orders.AsNoTracking();
        IReadOnlyList<string> terms = keyword is null ? [] : ChatText.ExtractSearchTerms(keyword);
        if (terms.Count > 0)
        {
            var term = terms[0];
            orders = orders.Where(item =>
                item.OrderNumber.Contains(term) ||
                item.Customer.Code.Contains(term) ||
                EF.Functions.Collate(item.Customer.Name, SearchCollation).Contains(term));
        }

        if (Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var orderStatus) &&
            Enum.IsDefined(orderStatus))
        {
            orders = orders.Where(item => item.Status == orderStatus);
        }

        var rows = await orders
            .OrderByDescending(item => item.OrderDate)
            .Take(10)
            .Select(item => new
            {
                item.OrderNumber,
                CustomerCode = item.Customer.Code,
                CustomerName = item.Customer.Name,
                item.Status,
                item.TotalAmount,
                Quantity = item.Details.Sum(detail => detail.Quantity),
                item.OrderDate
            })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return ChatToolResult.Empty(source);
        }

        var content = new StringBuilder("ĐƠN HÀNG ĐƯỢC PHÉP XEM (tối đa 10):\n");
        foreach (var item in rows)
        {
            content.AppendLine($"- {item.OrderNumber} | {item.CustomerCode} - {item.CustomerName} | ngày: {item.OrderDate.ToLocalTime():dd/MM/yyyy} | số lượng: {item.Quantity:N0} | tổng tiền: {item.TotalAmount:N0} đ | trạng thái: {item.Status}.");
        }

        return new ChatToolResult(content.ToString(), [source]);
    }

    internal static ChatToolResult DraftResult(ChatReceiptDraft draft)
    {
        var isImport = draft.Type == ChatDraftTypes.Import;
        var content = new StringBuilder(isImport ? "BẢN NHÁP PHIẾU NHẬP" : "BẢN NHÁP PHIẾU XUẤT");
        content.AppendLine(" (CHƯA được tạo; người dùng phải tự bấm \"Xác nhận tạo phiếu\" trên thẻ xem trước):");
        content.AppendLine($"- Kho: {(draft.WarehouseName.Length > 0 ? draft.WarehouseName : "chưa xác định")}");
        if (isImport)
        {
            content.AppendLine($"- Nhà cung cấp: {draft.SupplierName ?? "chưa xác định"}");
        }
        else if (draft.OrderNumber is not null)
        {
            content.AppendLine($"- Đơn hàng: {draft.OrderNumber}");
        }

        foreach (var line in draft.Lines)
        {
            content.Append($"- {line.Sku} | {line.ProductName} | số lượng: {line.Quantity:N0} {line.UnitOfMeasure}");
            content.AppendLine(isImport
                ? $" | đơn giá: {line.UnitCost:N0} đ | thành tiền: {line.Quantity * line.UnitCost:N0} đ"
                : $" | khả dụng: {line.AvailableQuantity:N0}");
        }

        content.AppendLine($"- {(isImport ? "Tổng giá trị" : "Giá vốn ước tính")}: {draft.TotalValue:N0} đ");
        foreach (var warning in draft.Warnings)
        {
            content.AppendLine($"- Lưu ý: {warning}");
        }

        content.AppendLine(draft.CanConfirm
            ? "TRẠNG THÁI: sẵn sàng để người dùng xác nhận."
            : "TRẠNG THÁI: CHƯA thể xác nhận; hãy giải thích các lưu ý và hỏi người dùng bổ sung thông tin.");
        return new ChatToolResult(
            content.ToString(),
            [isImport ? "Bản nháp phiếu nhập (chưa tạo)" : "Bản nháp phiếu xuất (chưa tạo)"],
            Draft: draft);
    }

    private IQueryable<Product> MatchProducts(IReadOnlyList<string> terms)
    {
        var products = dbContext.Products.AsNoTracking().Where(product => product.IsActive);
        return terms.Count switch
        {
            1 => products.Where(product =>
                product.Sku.Contains(terms[0]) ||
                EF.Functions.Collate(product.Name, SearchCollation).Contains(terms[0])),
            2 => products.Where(product =>
                product.Sku.Contains(terms[0]) ||
                EF.Functions.Collate(product.Name, SearchCollation).Contains(terms[0]) ||
                product.Sku.Contains(terms[1]) ||
                EF.Functions.Collate(product.Name, SearchCollation).Contains(terms[1])),
            >= 3 => products.Where(product =>
                product.Sku.Contains(terms[0]) ||
                EF.Functions.Collate(product.Name, SearchCollation).Contains(terms[0]) ||
                product.Sku.Contains(terms[1]) ||
                EF.Functions.Collate(product.Name, SearchCollation).Contains(terms[1]) ||
                product.Sku.Contains(terms[2]) ||
                EF.Functions.Collate(product.Name, SearchCollation).Contains(terms[2])),
            _ => products
        };
    }

    internal static ReceiptStatus[] ParseReceiptStatuses(string status) => status switch
    {
        "pending" => [ReceiptStatus.Pending],
        "approved" => [ReceiptStatus.Approved],
        "completed" => [ReceiptStatus.Completed],
        "rejected" => [ReceiptStatus.Rejected],
        "cancelled" => [ReceiptStatus.Cancelled],
        "any" => Enum.GetValues<ReceiptStatus>(),
        _ => [ReceiptStatus.Pending, ReceiptStatus.Approved]
    };

    private static string DescribeStatusFilter(string status) => status switch
    {
        "pending" => "chờ duyệt",
        "approved" => "đã duyệt",
        "completed" => "hoàn tất",
        "rejected" => "từ chối",
        "cancelled" => "đã hủy",
        "any" => "tất cả",
        _ => "đang mở (chờ duyệt hoặc đã duyệt)"
    };

    private static string StatusText(ReceiptStatus status) => status switch
    {
        ReceiptStatus.Pending => "chờ duyệt",
        ReceiptStatus.Approved => "đã duyệt, chờ hoàn tất",
        ReceiptStatus.Rejected => "từ chối",
        ReceiptStatus.Completed => "hoàn tất",
        ReceiptStatus.Cancelled => "đã hủy",
        _ => status.ToString()
    };

    private static string FormatDays(double? days) =>
        days.HasValue ? $"{days.Value:N1} ngày" : "không xác định (chưa có nhu cầu)";

    private static DateTimeOffset ToUtcBoundary(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime();
    }
}
