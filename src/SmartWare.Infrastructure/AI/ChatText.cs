using System.Globalization;
using System.Text;

namespace SmartWare.Infrastructure.AI;

/// <summary>
/// Shared Vietnamese text helpers for the chatbot: accent folding and keyword extraction.
/// </summary>
internal static class ChatText
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "cho", "toi", "biet", "thong", "tin", "tim", "kiem", "tra", "cuu", "ve",
        "san", "pham", "hang", "hoa", "ton", "kho", "ma", "sku", "con", "bao", "nhieu",
        "nha", "cung", "cap", "ncc", "khach", "don", "giai", "thich", "hay", "cua",
        "nao", "gi", "hien", "tai", "duoc", "khong", "va", "theo", "xem", "gan",
        "day", "moi", "nhat", "danh", "sach", "tong", "quan", "tinh", "hinh", "hom",
        "ngay", "du", "lieu", "phan", "tich", "la"
    };

    public static string Normalize(string value)
    {
        var decomposed = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(character == 'đ' ? 'd' :
                char.IsLetterOrDigit(character) || character is '-' or '/' or '.' or '_' ? character : ' ');
        }

        return string.Join(' ', builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    public static bool ContainsAny(string normalizedValue, params string[] candidates) =>
        candidates.Any(candidate => normalizedValue.Contains(candidate, StringComparison.Ordinal));

    /// <summary>
    /// Returns up to three of the longest meaningful terms, used for SKU/name/code lookups.
    /// </summary>
    public static IReadOnlyList<string> ExtractSearchTerms(
        string text,
        bool productLookup = false)
    {
        return Normalize(text)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            // "cung" is a stop word in "nhà cung cấp" but can be part of a product name.
            .Where(term => term.Length >= 2 &&
                           (!StopWords.Contains(term) || (productLookup && term == "cung")))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(term => term.Length)
            .Take(3)
            .ToList();
    }
}
