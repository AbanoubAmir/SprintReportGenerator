using System.Linq;
using System.Text;
using SprintReportGenerator.Models;
using SprintReportGenerator.Reporting;

namespace SprintReportGenerator.Reporting.Sections;

public class TaskEstimatesSection : IReportSection
{
    public string Title => "Task Estimates vs Completed Analysis";

    public string Render(AnalysisResult analysis, ReportContext context)
    {
        var sb = new StringBuilder();
        MarkdownHelper.AppendHeader(sb, Title, 3);

        if (analysis.TotalOriginalEstimate == 0)
        {
            sb.AppendLine("> ⚠️ **Status:** Not Available  ");
            sb.AppendLine("> No tasks have original estimates recorded.");
            sb.AppendLine();
            return sb.ToString();
        }

        sb.AppendLine("> ✅ **Status:** Available  ");
        sb.AppendLine();

        var tasksWithEstimates = analysis.WorkItems.Count(w => w.OriginalEstimate.HasValue);
        var tasksWithCompleted = analysis.WorkItems.Count(w => w.CompletedWork.HasValue);
        var totalTasks = analysis.WorkItems.Count(w => w.WorkItemType.Equals("Task", StringComparison.OrdinalIgnoreCase));

        sb.AppendLine("#### Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Count | Percentage |");
        sb.AppendLine("|--------|-------|------------|");
        sb.AppendLine($"| **Total Tasks in Sprint** | {totalTasks} | 100.00% |");
        sb.AppendLine($"| **Tasks with Original Estimates** | {tasksWithEstimates} | {tasksWithEstimates * 100.0 / Math.Max(totalTasks, 1):F2}% |");
        sb.AppendLine($"| **Tasks with Completed Work** | {tasksWithCompleted} | {tasksWithCompleted * 100.0 / Math.Max(totalTasks, 1):F2}% |");
        sb.AppendLine();

        sb.AppendLine("#### Estimates Analysis");
        sb.AppendLine();
        sb.AppendLine("| Metric | Hours |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| **Total Original Estimate** | {analysis.TotalOriginalEstimate:F1} |");
        sb.AppendLine($"| **Total Completed Work** | {analysis.TotalCompletedWork:F1} |");
        sb.AppendLine($"| **Total Remaining Work** | {analysis.TotalRemainingWork:F1} |");
        sb.AppendLine();

        var completionByEstimate = analysis.TotalOriginalEstimate > 0
            ? analysis.TotalCompletedWork / analysis.TotalOriginalEstimate * 100
            : 0;
        var variance = analysis.TotalCompletedWork - analysis.TotalOriginalEstimate;
        var variancePercent = analysis.TotalOriginalEstimate > 0
            ? variance / analysis.TotalOriginalEstimate * 100
            : 0;

        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| **Completion by Estimate** | {completionByEstimate:F2}% |");
        sb.AppendLine($"| **Variance** | {variance:F1} hours ({variancePercent:F2}%) |");
        sb.AppendLine($"| **Estimation Status** | {(variance < 0 ? "✅ Under Estimate" : "⚠️ Over Estimate")} |");
        sb.AppendLine();

        return sb.ToString();
    }
}

