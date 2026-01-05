using System.Linq;
using System.Text;
using SprintReportGenerator.Analysis;
using SprintReportGenerator.Models;
using SprintReportGenerator.Reporting;

namespace SprintReportGenerator.Reporting.Sections;

public class PlanVsActualSection : IReportSection
{
    public string Title => "Original Plan vs Completed Analysis";

    public string Render(AnalysisResult analysis, ReportContext context)
    {
        var sb = new StringBuilder();
        MarkdownHelper.AppendHeader(sb, Title);

        if (analysis.OriginalPlanItems.Count == 0)
        {
            sb.AppendLine("> No original plan items were detected for this sprint.");
            sb.AppendLine();
            return sb.ToString();
        }

        var originalCompleted = analysis.OriginalPlanItems.Count(w => WorkItemStatus.IsCompleted(w.State));
        var originalIncomplete = analysis.OriginalPlanItems.Count - originalCompleted;

        sb.AppendLine("### Original Plan Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Count | Percentage |");
        sb.AppendLine("|--------|-------|------------|");
        sb.AppendLine($"| **Total Items** | {analysis.OriginalPlanItems.Count} | 100.00% |");
        sb.AppendLine($"| **Completed** | {originalCompleted} | {originalCompleted * 100.0 / Math.Max(analysis.OriginalPlanItems.Count, 1):F2}% |");
        sb.AppendLine($"| **Incomplete** | {originalIncomplete} | {originalIncomplete * 100.0 / Math.Max(analysis.OriginalPlanItems.Count, 1):F2}% |");
        sb.AppendLine();

        var originalByType = analysis.OriginalPlanItems.GroupBy(w => w.WorkItemType).ToDictionary(g => g.Key, g => g.ToList());
        sb.AppendLine("#### Original Plan Breakdown by Type");
        sb.AppendLine();
        sb.AppendLine("| Type | Count | Completed | Completion Rate |");
        sb.AppendLine("|------|-------|-----------|----------------|");
        foreach (var type in originalByType.OrderByDescending(t => t.Value.Count))
        {
            var completed = type.Value.Count(w => WorkItemStatus.IsCompleted(w.State));
            var completionRate = type.Value.Count > 0 ? completed * 100.0 / type.Value.Count : 0;
            var escapedType = MarkdownHelper.EscapeTableCell(type.Key);
            sb.AppendLine($"| {escapedType} | {type.Value.Count} | {completed} | {completionRate:F2}% |");
        }
        sb.AppendLine();

        var originalByPriority = analysis.OriginalPlanItems.GroupBy(w => w.Priority).ToDictionary(g => g.Key, g => g.ToList());
        sb.AppendLine("#### Original Plan Breakdown by Priority");
        sb.AppendLine();
        sb.AppendLine("| Priority | Count | Completed | Completion Rate |");
        sb.AppendLine("|----------|-------|-----------|----------------|");
        foreach (var priority in originalByPriority.OrderBy(p => p.Key))
        {
            var completed = priority.Value.Count(w => WorkItemStatus.IsCompleted(w.State));
            var completionRate = priority.Value.Count > 0 ? completed * 100.0 / priority.Value.Count : 0;
            sb.AppendLine($"| Priority {priority.Key} | {priority.Value.Count} | {completed} | {completionRate:F2}% |");
        }
        sb.AppendLine();

        sb.AppendLine($"### Scope Changes (Added During Sprint): {analysis.AddedItems.Count} items");
        sb.AppendLine();

        if (analysis.AddedItems.Count > 0)
        {
            var addedCompleted = analysis.AddedItems.Count(w => WorkItemStatus.IsCompleted(w.State));
            var addedIncomplete = analysis.AddedItems.Count - addedCompleted;
            sb.AppendLine("| Metric | Count | Percentage |");
            sb.AppendLine("|--------|-------|------------|");
            sb.AppendLine($"| **Completed** | {addedCompleted} | {addedCompleted * 100.0 / Math.Max(analysis.AddedItems.Count, 1):F2}% |");
            sb.AppendLine($"| **Incomplete** | {addedIncomplete} | {addedIncomplete * 100.0 / Math.Max(analysis.AddedItems.Count, 1):F2}% |");
            sb.AppendLine();

            var addedByType = analysis.AddedItems.GroupBy(w => w.WorkItemType).ToDictionary(g => g.Key, g => g.ToList());
            sb.AppendLine("#### Added Items Breakdown by Type");
            sb.AppendLine();
            sb.AppendLine("| Type | Count | Completed | Completion Rate |");
            sb.AppendLine("|------|-------|-----------|----------------|");
            foreach (var type in addedByType.OrderByDescending(t => t.Value.Count))
            {
                var completed = type.Value.Count(w => WorkItemStatus.IsCompleted(w.State));
                var completionRate = type.Value.Count > 0 ? completed * 100.0 / type.Value.Count : 0;
                var escapedType = MarkdownHelper.EscapeTableCell(type.Key);
                sb.AppendLine($"| {escapedType} | {type.Value.Count} | {completed} | {completionRate:F2}% |");
            }
            sb.AppendLine();
        }

        var progressVsOriginal = analysis.OriginalPlanItems.Count > 0
            ? originalCompleted * 100.0 / analysis.OriginalPlanItems.Count
            : 0;

        sb.AppendLine("### Progress Calculation");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| **Completed Work Items** | {analysis.CompletedCount} |");
        sb.AppendLine($"| **Original Plan** | {analysis.OriginalPlanItems.Count} |");
        sb.AppendLine($"| **Progress vs Original Plan** | **{progressVsOriginal:F1}%** ({analysis.CompletedCount} ÷ {analysis.OriginalPlanItems.Count}) |");
        sb.AppendLine();
        sb.AppendLine($"> The team completed **{progressVsOriginal:F1}%** of the originally planned work.");
        if (analysis.AddedItems.Count > 0)
        {
            sb.AppendLine($"> However, **{analysis.AddedItems.Count}** additional items were added during the sprint,");
            sb.AppendLine($"> bringing the total to **{analysis.TotalItems}** items with **{analysis.CompletedPercentage:F2}%** completion.");
        }
        sb.AppendLine();

        return sb.ToString();
    }
}

