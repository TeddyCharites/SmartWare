using SmartWare.Domain.Constants;

namespace SmartWare.Infrastructure.AI;

internal enum ChatIntent
{
    Overview,
    StockLookup,
    LowStock,
    MovementAnalysis,
    Supplier,
    Report,
    Sales,
    Knowledge
}

/// <summary>
/// Keyword-based intent detection. Since the function-calling upgrade this is only used
/// for the offline fallback when Gemini is unavailable or not configured.
/// </summary>
internal static class ChatIntentClassifier
{
    public static ChatIntent Classify(string question)
    {
        var normalized = ChatText.Normalize(question);
        if (ChatText.ContainsAny(normalized, "sap het", "ton thap", "het hang", "can nhap", "bo sung hang"))
        {
            return ChatIntent.LowStock;
        }

        if (ChatText.ContainsAny(normalized, "nha cung cap", "ncc", "supplier"))
        {
            return ChatIntent.Supplier;
        }

        if (ChatText.ContainsAny(normalized, "khach hang", "don hang", "doanh so khach"))
        {
            return ChatIntent.Sales;
        }

        if (ChatText.ContainsAny(normalized, "bao cao", "doanh thu", "loi nhuan", "cogs", "gia tri ton", "du bao"))
        {
            return ChatIntent.Report;
        }

        if (ChatText.ContainsAny(normalized, "nhap xuat", "nhap/xuat", "giao dich", "xu huong", "phan tich nhap", "phan tich xuat"))
        {
            return ChatIntent.MovementAnalysis;
        }

        if (ChatText.ContainsAny(normalized, "ton kho", "con bao nhieu", "ma san pham", "sku", "hang hoa", "san pham"))
        {
            return ChatIntent.StockLookup;
        }

        return HasKnowledgeCue(question) ? ChatIntent.Knowledge : ChatIntent.Overview;
    }

    public static bool HasKnowledgeCue(string question) =>
        ChatText.ContainsAny(
            ChatText.Normalize(question),
            "quy trinh", "chinh sach", "huong dan", "quy dinh", "xu ly",
            "kiem ke", "bao quan", "nguyen tac", "phe duyet", "duyet phieu",
            "trach nhiem", "phai lam gi", "can lam gi");

    public static bool CanAccess(ChatIntent intent, string role) => intent switch
    {
        ChatIntent.Report => role is RoleNames.Admin or RoleNames.Manager,
        ChatIntent.Sales => role == RoleNames.Admin,
        _ => RoleNames.All.Contains(role)
    };

    public static int MovementDays(string question)
    {
        var normalized = ChatText.Normalize(question);
        return normalized.Contains("7 ngay", StringComparison.Ordinal) ? 7 :
            normalized.Contains("quy", StringComparison.Ordinal) || normalized.Contains("90", StringComparison.Ordinal) ? 90 : 30;
    }
}
