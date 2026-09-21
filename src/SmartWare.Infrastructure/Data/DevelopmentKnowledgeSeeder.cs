using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartWare.Application.AI.Rag;
using SmartWare.Domain.Constants;
using SmartWare.Infrastructure.Identity;

namespace SmartWare.Infrastructure.Data;

public static class DevelopmentKnowledgeSeeder
{
    public static async Task SeedAsync(
        IServiceProvider services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        if (!isDevelopment || !configuration.GetValue("DevelopmentSeedData:Enabled", true))
        {
            return;
        }

        using var scope = services.CreateScope();
        var knowledgeService = scope.ServiceProvider.GetRequiredService<IKnowledgeService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DevelopmentKnowledgeSeeder");
        var existing = await knowledgeService.GetDocumentsAsync();
        if (existing.Any(document => document.Title.StartsWith("[Demo]", StringComparison.Ordinal)))
        {
            logger.LogInformation("Tài liệu RAG mẫu đã tồn tại; bỏ qua bước seed.");
            return;
        }

        var email = configuration["DevelopmentAdmin:Email"];
        var admin = string.IsNullOrWhiteSpace(email)
            ? null
            : await userManager.FindByEmailAsync(email);
        if (admin is null)
        {
            logger.LogWarning("Không thể seed tài liệu RAG vì chưa có DevelopmentAdmin.");
            return;
        }

        var documents = new[]
        {
            new CreateKnowledgeDocumentCommand(
                "[Demo] Quy trình nhập kho chuẩn",
                "Quy trình kho",
                "1.0",
                "demo-quy-trinh-nhap-kho.md",
                """
                QUY TRÌNH NHẬP KHO CHUẨN

                Nhân viên tạo phiếu nhập với nhà cung cấp, kho nhận hàng và danh sách sản phẩm. Số lượng phải lớn hơn không, đơn giá nhập phải lớn hơn không và mỗi sản phẩm chỉ xuất hiện một lần trong phiếu.

                Phiếu mới ở trạng thái Chờ duyệt. Người tạo không tự ý cộng tồn kho. Quản lý kiểm tra chứng từ, số lượng, đơn giá và có thể duyệt hoặc từ chối kèm lý do. Sau khi được duyệt, Admin hoặc Nhân viên được phân công mới hoàn tất phiếu.

                Khi hoàn tất, hệ thống cộng số lượng vào tồn thực tế, tính lại giá vốn bình quân gia quyền và tạo giao dịch IN. Phiếu đã hoàn tất không được sửa hoặc hoàn tất lần nữa. Hàng nhận sai hoặc thiếu phải được ghi nhận và xử lý trước khi hoàn tất.
                """,
                RoleNames.All,
                admin.Id),
            new CreateKnowledgeDocumentCommand(
                "[Demo] Quy trình xuất kho và giữ hàng",
                "Quy trình kho",
                "1.0",
                "demo-quy-trinh-xuat-kho.md",
                """
                QUY TRÌNH XUẤT KHO VÀ GIỮ HÀNG

                Phiếu xuất có thể liên kết với đơn hàng hoặc là phiếu xuất trực tiếp. Nhân viên phải kiểm tra đúng kho, sản phẩm và số lượng trước khi gửi duyệt. Không được xuất vượt quá tồn khả dụng, được tính bằng tồn thực tế trừ số lượng đã giữ.

                Khi Quản lý duyệt phiếu, hệ thống tạo giữ hàng cho từng sản phẩm và tăng số lượng ReservedQuantity. Giữ hàng giúp các phiếu khác không sử dụng cùng một lượng tồn. Khi phiếu bị từ chối hoặc hủy, hệ thống phải giải phóng lượng giữ.

                Khi hoàn tất xuất kho, hệ thống trừ tồn thực tế, giải phóng lượng giữ, đóng băng giá vốn xuất và tạo giao dịch OUT. Người dùng không được sửa số lượng của phiếu đã hoàn tất.
                """,
                RoleNames.All,
                admin.Id),
            new CreateKnowledgeDocumentCommand(
                "[Demo] Hướng dẫn kiểm kê và xử lý chênh lệch",
                "Kiểm kê",
                "1.0",
                "demo-kiem-ke.md",
                """
                HƯỚNG DẪN KIỂM KÊ

                Kiểm kê định kỳ được thực hiện vào cuối tháng hoặc khi có dấu hiệu sai lệch. Trước khi đếm, kho cần tạm dừng dịch chuyển hàng tại khu vực kiểm kê và in danh sách sản phẩm theo vị trí.

                Hai người thực hiện đếm độc lập. Kết quả thực tế được đối chiếu với tồn hệ thống, tồn đã giữ và các phiếu đang chờ hoàn tất. Chênh lệch phải có biên bản nêu mã sản phẩm, số lượng hệ thống, số lượng thực tế, nguyên nhân dự kiến và người xác nhận.

                Chatbot chỉ hỗ trợ tra cứu và giải thích. Chatbot không được tự tạo điều chỉnh tồn. Mọi điều chỉnh sau kiểm kê phải do người có thẩm quyền thực hiện qua quy trình nghiệp vụ và được lưu vết kiểm toán.
                """,
                RoleNames.All,
                admin.Id),
            new CreateKnowledgeDocumentCommand(
                "[Demo] Quy ước đọc báo cáo quản trị kho",
                "Báo cáo quản trị",
                "1.0",
                "demo-bao-cao-quan-tri.md",
                """
                QUY ƯỚC ĐỌC BÁO CÁO QUẢN TRỊ KHO

                Giá trị tồn kho được tính bằng số lượng tồn thực tế nhân giá vốn bình quân. Tồn khả dụng bằng tồn thực tế trừ lượng đã giữ cho các phiếu xuất đã duyệt. Doanh thu chỉ ghi nhận từ đơn hàng hoàn tất trong kỳ.

                Giá vốn hàng bán lấy từ giao dịch OUT đã hoàn tất. Lợi nhuận gộp bằng doanh thu trừ giá vốn hàng bán và chưa bao gồm chi phí vận hành, thuế hoặc chiết khấu ngoài đơn hàng.

                Dự báo nhu cầu sử dụng trung bình trượt từ lịch sử xuất kho. Đây là chỉ báo hỗ trợ ra quyết định, không phải lệnh mua hàng tự động. Quản lý cần xem thêm thời gian giao hàng, đơn hàng đang chờ và biến động mùa vụ trước khi bổ sung tồn.
                """,
                [RoleNames.Admin, RoleNames.Manager],
                admin.Id)
        };

        foreach (var document in documents)
        {
            var result = await knowledgeService.CreateAsync(document);
            if (!result.Succeeded)
            {
                logger.LogWarning("Không thể seed tài liệu {Title}: {Message}", document.Title, result.Message);
            }
        }

        logger.LogInformation("Đã seed {Count} tài liệu RAG mẫu.", documents.Length);
    }
}
