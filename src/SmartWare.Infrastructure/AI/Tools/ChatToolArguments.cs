using System.Globalization;
using System.Text.Json;

namespace SmartWare.Infrastructure.AI.Tools;

internal sealed record DraftLineRequest(string Product, int Quantity, decimal? UnitCost);

/// <summary>
/// Defensive parsing of the arguments Gemini sends with a function call. Values are never
/// trusted: strings are trimmed and bounded, numbers clamped and date ranges normalized.
/// </summary>
internal static class ChatToolArguments
{
    public const int MaxRangeDays = 366;
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy"];

    public static string? GetString(JsonElement args, string name, int maxLength = 200)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length <= maxLength ? text : text[..maxLength];
    }

    public static int GetInt(JsonElement args, string name, int defaultValue, int min, int max)
    {
        if (args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var number))
        {
            return (int)Math.Clamp(Math.Round(number), min, max);
        }

        return defaultValue;
    }

    /// <summary>
    /// Reads from_date/to_date. Missing values default to the last <paramref name="defaultDays"/>
    /// days; future dates are capped at today, reversed ranges are swapped and the span is
    /// limited to <see cref="MaxRangeDays"/> days.
    /// </summary>
    public static (DateOnly From, DateOnly To) GetDateRange(
        JsonElement args,
        DateOnly today,
        int defaultDays = 30)
    {
        var to = ParseDate(GetString(args, "to_date")) ?? today;
        var from = ParseDate(GetString(args, "from_date")) ?? to.AddDays(-(defaultDays - 1));

        if (from > to)
        {
            (from, to) = (to, from);
        }

        if (to > today)
        {
            to = today;
        }

        if (from > to)
        {
            from = to;
        }

        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
        {
            from = to.AddDays(-(MaxRangeDays - 1));
        }

        return (from, to);
    }

    /// <summary>
    /// Reads the "lines" array of a draft tool. Entries without a product or with a non-positive
    /// quantity are kept with quantity 0 so the draft can report them instead of silently dropping them.
    /// </summary>
    public static IReadOnlyList<DraftLineRequest> GetDraftLines(JsonElement args, int maxLines = 30)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("lines", out var lines) ||
            lines.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<DraftLineRequest>();
        foreach (var line in lines.EnumerateArray().Take(maxLines))
        {
            var product = GetString(line, "product") ?? string.Empty;
            var quantity = GetInt(line, "quantity", 0, 0, 1_000_000);
            decimal? unitCost = line.ValueKind == JsonValueKind.Object &&
                                line.TryGetProperty("unit_cost", out var cost) &&
                                cost.ValueKind == JsonValueKind.Number &&
                                cost.TryGetDecimal(out var value) &&
                                value > 0
                ? Math.Round(value, 2)
                : null;
            result.Add(new DraftLineRequest(product, quantity, unitCost));
        }

        return result;
    }

    private static DateOnly? ParseDate(string? value) =>
        value is not null && DateOnly.TryParseExact(
            value,
            DateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
}
