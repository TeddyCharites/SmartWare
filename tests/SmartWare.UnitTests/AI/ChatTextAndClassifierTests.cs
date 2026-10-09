using SmartWare.Domain.Constants;
using SmartWare.Infrastructure.AI;

namespace SmartWare.UnitTests.AI;

public sealed class ChatTextAndClassifierTests
{
    [Theory]
    [InlineData("Tồn kho Sữa Tươi", "ton kho sua tuoi")]
    [InlineData("Đơn hàng ĐH-001?", "don hang dh-001")]
    [InlineData("  nhập   xuất  ", "nhap xuat")]
    public void Normalize_RemovesVietnameseAccentsAndPunctuation(string input, string expected)
    {
        Assert.Equal(expected, ChatText.Normalize(input));
    }

    [Fact]
    public void ExtractSearchTerms_DropsStopWordsAndKeepsLongestTerms()
    {
        var terms = ChatText.ExtractSearchTerms("Cho tôi biết tồn kho của sản phẩm SP-0012 hiện tại");

        Assert.Equal(["sp-0012"], terms);
    }

    [Fact]
    public void ExtractSearchTerms_KeepsProductWordsThatLookLikeStopWords()
    {
        Assert.Contains("may", ChatText.ExtractSearchTerms("máy in laser", productLookup: true));
        Assert.Contains("cung", ChatText.ExtractSearchTerms("cung tên", productLookup: true));
        Assert.DoesNotContain("cung", ChatText.ExtractSearchTerms("nhà cung cấp ABC"));
    }

    [Theory]
    [InlineData("Quy trình duyệt phiếu xuất là gì?", nameof(ChatIntent.Knowledge))]
    [InlineData("Tồn kho sữa tươi còn bao nhiêu?", nameof(ChatIntent.StockLookup))]
    [InlineData("Sản phẩm nào sắp hết hàng?", nameof(ChatIntent.LowStock))]
    [InlineData("Doanh thu tháng này thế nào?", nameof(ChatIntent.Report))]
    [InlineData("Nhà cung cấp nào giao nhiều nhất?", nameof(ChatIntent.Supplier))]
    [InlineData("Phân tích nhập xuất 7 ngày", nameof(ChatIntent.MovementAnalysis))]
    [InlineData("Xin chào", nameof(ChatIntent.Overview))]
    public void Classify_RoutesOfflineQuestions(string question, string expected)
    {
        Assert.Equal(Enum.Parse<ChatIntent>(expected), ChatIntentClassifier.Classify(question));
    }

    [Theory]
    [InlineData(nameof(ChatIntent.Report), RoleNames.Employee, false)]
    [InlineData(nameof(ChatIntent.Report), RoleNames.Manager, true)]
    [InlineData(nameof(ChatIntent.Sales), RoleNames.Manager, false)]
    [InlineData(nameof(ChatIntent.Sales), RoleNames.Admin, true)]
    [InlineData(nameof(ChatIntent.StockLookup), RoleNames.Employee, true)]
    [InlineData(nameof(ChatIntent.StockLookup), "Guest", false)]
    public void CanAccess_FollowsRolePermissions(string intent, string role, bool expected)
    {
        Assert.Equal(expected, ChatIntentClassifier.CanAccess(Enum.Parse<ChatIntent>(intent), role));
    }

    [Theory]
    [InlineData("nhập xuất 7 ngày qua", 7)]
    [InlineData("phân tích quý này", 90)]
    [InlineData("phân tích nhập xuất", 30)]
    public void MovementDays_ReadsPeriodFromQuestion(string question, int expected)
    {
        Assert.Equal(expected, ChatIntentClassifier.MovementDays(question));
    }
}
