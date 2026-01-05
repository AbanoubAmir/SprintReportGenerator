using System.Linq;
using System.Text;
using SprintReportGenerator.Analysis;
using SprintReportGenerator.Models;
using SprintReportGenerator.Reporting;

namespace SprintReportGenerator.Reporting.Sections;

public class ExecutiveSummarySection : IReportSection
{
    public string Title => "Executive Summary";

    public string Render(AnalysisResult analysis, ReportContext context)
    {
        var sb = new StringBuilder();
        MarkdownHelper.AppendHeader(sb, Title);

        sb.AppendLine("| Metric | Count | Percentage |");
        sb.AppendLine("|--------|-------|------------|");
        sb.AppendLine($"| **Total Work Items** | {analysis.TotalItems} | 100.00% |");
        sb.AppendLine($"| **Completed** | {analysis.CompletedCount} | {analysis.CompletedPercentage:F2}% |");
        sb.AppendLine($"| **In Progress** | {analysis.InProgressCount} | {analysis.InProgressPercentage:F2}% |");
        sb.AppendLine($"| **Not Started** | {analysis.NotStartedCount} | {analysis.NotStartedPercentage:F2}% |");
        sb.AppendLine($"| **Blocked** | {analysis.BlockedCount} | {analysis.BlockedPercentage:F2}% |");
        sb.AppendLine();

        if (analysis.CrossIterationItems.Count > 0)
        {
            var crossIterationPercentage = analysis.TotalItems > 0
                ? (double)analysis.CrossIterationItems.Count / analysis.TotalItems * 100
                : 0;
            sb.AppendLine("### Work Distribution");
            sb.AppendLine();
            sb.AppendLine($"| Category | Count | Percentage |");
            sb.AppendLine($"|----------|-------|------------|");
            sb.AppendLine($"| **Sprint Iteration** | {analysis.SprintIterationItems.Count} | {100 - crossIterationPercentage:F2}% |");
            sb.AppendLine($"| **Cross-Iteration** | {analysis.CrossIterationItems.Count} | {crossIterationPercentage:F2}% |");
            sb.AppendLine();
            sb.AppendLine($"> **Note:** {analysis.CrossIterationItems.Count} work items from other iterations were actively worked on during this sprint period. See the Cross-Iteration Work Analysis section for details.");
            sb.AppendLine();
        }

        if (analysis.OriginalPlanItems.Count > 0)
        {
            var progressVsOriginal = analysis.TotalItems > 0
                ? (double)analysis.CompletedCount / analysis.OriginalPlanItems.Count * 100
                : 0;

            sb.AppendLine("### Progress Calculation");
            sb.AppendLine();
            sb.AppendLine("Based on work item count analysis:");
            sb.AppendLine();
            sb.AppendLine("| Item | Count |");
            sb.AppendLine("|------|-------|");
            sb.AppendLine($"| **Original Plan** | {analysis.OriginalPlanItems.Count} work items |");
            sb.AppendLine($"| **Completed Work Items** | {analysis.CompletedCount} |");
            sb.AppendLine($"| **Progress vs Original Plan** | **{progressVsOriginal:F1}%** ({analysis.CompletedCount} ÷ {analysis.OriginalPlanItems.Count}) |");
            sb.AppendLine();
            sb.AppendLine($"> The team completed **{progressVsOriginal:F1}%** of the originally planned work. However, **{analysis.AddedItems.Count}** additional items were added during the sprint, bringing the total to **{analysis.TotalItems}** items with **{analysis.CompletedPercentage:F2}%** completion of the current scope.");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

