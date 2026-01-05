using System.Text;

namespace SprintReportGenerator.Reporting;

public static class MarkdownHelper
{
    public static string EscapeTableCell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("|", "&#124;")
            .Replace("\n", " ")
            .Replace("\r", string.Empty)
            .Trim();
    }

    public static StringBuilder AppendHeader(StringBuilder sb, string text, int level = 2)
    {
        sb.AppendLine($"{new string('#', level)} {text}");
        sb.AppendLine();
        return sb;
    }

    public static string BuildWorkItemLink(int? id, string? workItemUrlBase, string? label = null, bool escapeForTable = false)
    {
        var text = label ?? (id?.ToString() ?? "—");
        if (escapeForTable)
        {
            text = EscapeTableCell(text);
        }

        if (!id.HasValue || string.IsNullOrWhiteSpace(workItemUrlBase))
        {
            return text;
        }

        return $"[{text}]({workItemUrlBase}{id.Value})";
    }
}

