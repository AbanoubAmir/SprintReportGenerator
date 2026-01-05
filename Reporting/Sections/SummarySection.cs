using System.Text;
using SprintReportGenerator.Models;
using SprintReportGenerator.Reporting;

namespace SprintReportGenerator.Reporting.Sections;

public class SummarySection : IReportSection
{
    public string Title => "Summary and Insights";

    public string Render(AnalysisResult analysis, ReportContext context)
    {
        var sb = new StringBuilder();
        MarkdownHelper.AppendHeader(sb, Title);

        sb.AppendLine("### Current State");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| **Total Items** | {analysis.TotalItems} |");
        sb.AppendLine($"| **Completed** | {analysis.CompletedCount} items ({analysis.CompletedPercentage:F2}%) |");
        sb.AppendLine($"| **Remaining** | {analysis.TotalItems - analysis.CompletedCount} items |");
        sb.AppendLine();

        sb.AppendLine("### Key Insights");
        sb.AppendLine();
        sb.AppendLine($"1. **Completion Rate:** {analysis.CompletedPercentage:F2}%");
        sb.AppendLine($"2. **In Progress Items:** {analysis.InProgressCount}");
        sb.AppendLine($"3. **Blocked Items:** {analysis.BlockedCount}");
        sb.AppendLine($"4. **Unassigned Items:** {analysis.UnassignedItems.Count}");
        sb.AppendLine();

        return sb.ToString();
    }
}

