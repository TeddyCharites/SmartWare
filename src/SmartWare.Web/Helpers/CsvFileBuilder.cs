using System.Globalization;
using System.Text;

namespace SmartWare.Web.Helpers;

internal static class CsvFileBuilder
{
    public static byte[] Build(
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<object?>> rows)
    {
        var content = new StringBuilder();
        AppendRow(content, headers.Cast<object?>());

        foreach (var row in rows)
        {
            AppendRow(content, row);
        }

        return Encoding.UTF8.GetBytes('\uFEFF' + content.ToString());
    }

    private static void AppendRow(StringBuilder content, IEnumerable<object?> values)
    {
        content.AppendLine(string.Join(',', values.Select(FormatValue)));
    }

    private static string FormatValue(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        if (value is string &&
            text.Length > 0 &&
            text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            text = "'" + text;
        }

        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
