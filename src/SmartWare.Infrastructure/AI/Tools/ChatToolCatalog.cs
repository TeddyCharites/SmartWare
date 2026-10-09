using SmartWare.Domain.Constants;

namespace SmartWare.Infrastructure.AI.Tools;

internal sealed record ChatToolDefinition(
    string Name,
    string Description,
    object? Parameters,
    IReadOnlyList<string> AllowedRoles);

/// <summary>
/// The read-only tools Gemini may call. A role only ever sees the tools it is allowed to use,
/// and <see cref="WarehouseChatTools"/> re-checks the role before executing any call.
/// </summary>
internal static class ChatToolCatalog
{
    public const string LookupStock = "lookup_stock";
    public const string ListLowStock = "list_low_stock";
    public const string AnalyzeMovements = "analyze_stock_movements";
    public const string LookupSuppliers = "lookup_suppliers";
    public const string ListReceipts = "list_receipts";
    public const string InventoryOverview = "get_inventory_overview";
    public const string SearchKnowledge = "search_knowledge_base";
    public const string BusinessReport = "get_business_report";
    public const string SuggestReplenishment = "suggest_replenishment";
    public const string LookupOrders = "lookup_orders";
    public const string DraftImportReceipt = "draft_import_receipt";
    public const string DraftExportReceipt = "draft_export_receipt";

    private static readonly string[] AllRoles = [RoleNames.Admin, RoleNames.Manager, RoleNames.Employee];
    private static readonly string[] ManagementRoles = [RoleNames.Admin, RoleNames.Manager];
    private static readonly string[] AdminRole = [RoleNames.Admin];
    // Same roles as the CreateReceipts policy: managers approve receipts, they do not create them.
    private static readonly string[] ReceiptCreatorRoles = [RoleNames.Admin, RoleNames.Employee];

    private static object DraftLinesParameter(bool withUnitCost) => new
    {
        type = "array",
        description = "Các dòng hàng, tối đa 30.",
        items = new
        {
            type = "object",
            properties = withUnitCost
                ? (object)new
                {
                    product = new { type = "string", description = "Mã SKU hoặc tên sản phẩm." },
                    quantity = new { type = "integer", description = "Số lượng, lớn hơn 0." },
                    unit_cost = new { type = "number", description = "Không bắt buộc. Đơn giá nhập (đ). Bỏ trống để lấy theo lần nhập gần nhất." }
                }
                : new
                {
                    product = new { type = "string", description = "Mã SKU hoặc tên sản phẩm." },
                    quantity = new { type = "integer", description = "Số lượng, lớn hơn 0." }
                },
            required = new[] { "product", "quantity" }
        }
    };

    private static object DateParameter(string description) => new
    {
        type = "string",
        description = description + " Định dạng YYYY-MM-DD."
    };

    public static IReadOnlyList<ChatToolDefinition> All { get; } =
    [
        new(
            LookupStock,
            "Tra cứu sản phẩm và tồn kho hiện tại theo mã SKU hoặc tên: tồn thực tế, đã giữ, khả dụng, min/max, giá vốn bình quân, nhà cung cấp.",
            new
            {
                type = "object",
                properties = new
                {
                    keyword = new
                    {
                        type = "string",
                        description = "Mã SKU hoặc một phần tên sản phẩm, ví dụ 'SP001' hoặc 'sữa tươi'."
                    }
                },
                required = new[] { "keyword" }
            },
            AllRoles),
        new(
            ListLowStock,
            "Liệt kê các sản phẩm có tồn khả dụng chạm hoặc dưới mức tồn tối thiểu (cần bổ sung hàng).",
            new
            {
                type = "object",
                properties = new
                {
                    limit = new { type = "integer", description = "Số sản phẩm tối đa, 1-30. Mặc định 20." }
                }
            },
            AllRoles),
        new(
            AnalyzeMovements,
            "Phân tích giao dịch nhập (IN) và xuất (OUT) kho trong một khoảng thời gian: tổng số lượng, giá trị, top sản phẩm nhập/xuất nhiều. Có thể lọc theo sản phẩm.",
            new
            {
                type = "object",
                properties = new
                {
                    from_date = DateParameter("Ngày bắt đầu."),
                    to_date = DateParameter("Ngày kết thúc."),
                    product_keyword = new
                    {
                        type = "string",
                        description = "Không bắt buộc. Mã SKU hoặc tên sản phẩm cần lọc."
                    }
                }
            },
            AllRoles),
        new(
            LookupSuppliers,
            "Tra cứu nhà cung cấp theo mã hoặc tên và số sản phẩm đang cung cấp. Admin/Manager thấy thêm thống kê nhập 30 ngày.",
            new
            {
                type = "object",
                properties = new
                {
                    keyword = new { type = "string", description = "Không bắt buộc. Mã hoặc tên nhà cung cấp." }
                }
            },
            AllRoles),
        new(
            ListReceipts,
            "Liệt kê phiếu nhập kho và/hoặc phiếu xuất kho gần nhất theo trạng thái, ví dụ các phiếu đang chờ duyệt.",
            new
            {
                type = "object",
                properties = new
                {
                    receipt_type = new
                    {
                        type = "string",
                        @enum = new[] { "import", "export", "all" },
                        description = "import = phiếu nhập, export = phiếu xuất, all = cả hai."
                    },
                    status = new
                    {
                        type = "string",
                        @enum = new[] { "open", "pending", "approved", "completed", "rejected", "cancelled", "any" },
                        description = "open = chờ duyệt hoặc đã duyệt nhưng chưa hoàn tất. Mặc định open."
                    }
                }
            },
            AllRoles),
        new(
            InventoryOverview,
            "Tổng quan toàn kho hiện tại: số sản phẩm có tồn, tổng tồn, đã giữ, khả dụng, giá trị tồn, số mã dưới mức tối thiểu.",
            null,
            AllRoles),
        new(
            SearchKnowledge,
            "Tìm trong kho tri thức nội bộ (quy trình, chính sách, quy định, hướng dẫn xử lý) bằng tìm kiếm ngữ nghĩa. Dùng cho câu hỏi 'làm thế nào', 'quy trình', 'quy định'.",
            new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Câu hỏi hoặc chủ đề cần tìm." }
                },
                required = new[] { "query" }
            },
            AllRoles),
        new(
            BusinessReport,
            "Báo cáo kinh doanh trong một khoảng thời gian: số lượng/giá trị nhập, xuất, doanh thu, giá vốn hàng bán (COGS), lợi nhuận gộp, giá trị tồn, top sản phẩm.",
            new
            {
                type = "object",
                properties = new
                {
                    from_date = DateParameter("Ngày bắt đầu."),
                    to_date = DateParameter("Ngày kết thúc.")
                }
            },
            ManagementRoles),
        new(
            SuggestReplenishment,
            "Gợi ý nhập hàng dựa trên dự báo nhu cầu (trung bình trượt SMA cửa sổ 7 ngày trên lịch sử 30 ngày, dự báo cho 30 ngày tới): sản phẩm có rủi ro hết hàng, số ngày còn đủ bán, số lượng đề xuất nhập và sai số dự báo.",
            new
            {
                type = "object",
                properties = new
                {
                    limit = new { type = "integer", description = "Số sản phẩm tối đa, 1-30. Mặc định 15." }
                }
            },
            ManagementRoles),
        new(
            LookupOrders,
            "Tra cứu đơn hàng bán theo mã đơn, mã hoặc tên khách hàng, có thể lọc theo trạng thái.",
            new
            {
                type = "object",
                properties = new
                {
                    keyword = new { type = "string", description = "Không bắt buộc. Mã đơn, mã hoặc tên khách hàng." },
                    status = new
                    {
                        type = "string",
                        @enum = new[] { "Pending", "Processing", "Shipping", "Completed", "Cancelled", "DeliveryFailed", "Returned" },
                        description = "Không bắt buộc. Trạng thái đơn hàng."
                    }
                }
            },
            AdminRole),
        new(
            DraftImportReceipt,
            "Soạn NHÁP phiếu nhập kho khi người dùng muốn tạo phiếu nhập. Công cụ chỉ chuẩn bị bản nháp để người dùng xem lại; phiếu CHƯA được tạo cho đến khi người dùng tự bấm xác nhận.",
            new
            {
                type = "object",
                properties = new
                {
                    supplier = new { type = "string", description = "Không bắt buộc. Mã hoặc tên nhà cung cấp; bỏ trống để suy ra từ sản phẩm." },
                    warehouse = new { type = "string", description = "Không bắt buộc. Tên hoặc mã kho nhận; bỏ trống để dùng kho mặc định." },
                    lines = DraftLinesParameter(withUnitCost: true)
                },
                required = new[] { "lines" }
            },
            ReceiptCreatorRoles),
        new(
            DraftExportReceipt,
            "Soạn NHÁP phiếu xuất kho khi người dùng muốn tạo phiếu xuất, có kiểm tra tồn khả dụng. Phiếu CHƯA được tạo cho đến khi người dùng tự bấm xác nhận.",
            new
            {
                type = "object",
                properties = new
                {
                    warehouse = new { type = "string", description = "Không bắt buộc. Tên hoặc mã kho xuất; bỏ trống để dùng kho mặc định." },
                    order_number = new { type = "string", description = "Không bắt buộc. Mã đơn hàng bán liên kết (đơn đang chờ hoặc đang xử lý)." },
                    lines = DraftLinesParameter(withUnitCost: false)
                },
                required = new[] { "lines" }
            },
            ReceiptCreatorRoles)
    ];

    public static IReadOnlyList<ChatToolDefinition> ForRole(string role) =>
        All.Where(tool => tool.AllowedRoles.Contains(role)).ToArray();

    public static bool IsAllowed(string toolName, string role) =>
        All.Any(tool => tool.Name == toolName && tool.AllowedRoles.Contains(role));
}
