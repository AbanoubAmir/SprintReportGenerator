using System.Linq;
using System.Text;
using SprintReportGenerator.Models;
using SprintReportGenerator.Reporting;

namespace SprintReportGenerator.Reporting.Sections;

public class CurrentStateSection : IReportSection
{
    public string Title => "Current State Analysis";

    public string Render(AnalysisResult analysis, ReportContext context)
    {
        var sb = new StringBuilder();
        MarkdownHelper.AppendHeader(sb, Title);

        sb.AppendLine("### 1. Breakdown by State");
        sb.AppendLine();
        sb.AppendLine("| State | Count | Percentage |");
        sb.AppendLine("|-------|-------|------------|");
        foreach (var state in analysis.StateBreakdown.OrderByDescending(s => s.Value))
        {
            var percentage = (double)state.Value / analysis.TotalItems * 100;
            var escapedState = MarkdownHelper.EscapeTableCell(state.Key);
            sb.AppendLine($"| {escapedState} | {state.Value} | {percentage:F2}% |");
        }
        sb.AppendLine();

        sb.AppendLine("### 2. Breakdown by Work Item Type");
        sb.AppendLine();
        sb.AppendLine("| Type | Count | Percentage | Completion Rate |");
        sb.AppendLine("|------|-------|------------|----------------|");
        foreach (var type in analysis.TypeBreakdown.OrderByDescending(t => t.Value))
        {
            var percentage = (double)type.Value / analysis.TotalItems * 100;
            var completed = analysis.CompletedByType.GetValueOrDefault(type.Key, 0);
            var completionRate = type.Value > 0 ? (double)completed / type.Value * 100 : 0;
            var escapedType = MarkdownHelper.EscapeTableCell(type.Key);
            sb.AppendLine($"| {escapedType} | {type.Value} | {percentage:F2}% | {completionRate:F2}% |");
        }
        sb.AppendLine();

        sb.AppendLine("### 3. Breakdown by Priority");
        sb.AppendLine();
        sb.AppendLine("| Priority | Count | Percentage | Completion Rate |");
        sb.AppendLine("|----------|-------|------------|----------------|");
        foreach (var priority in analysis.PriorityBreakdown.OrderBy(p => p.Key))
        {
            var percentage = (double)priority.Value / analysis.TotalItems * 100;
            var completed = analysis.CompletedByPriority.GetValueOrDefault(priority.Key, 0);
            var completionRate = priority.Value > 0 ? (double)completed / priority.Value * 100 : 0;
            sb.AppendLine($"| Priority {priority.Key} | {priority.Value} | {percentage:F2}% | {completionRate:F2}% |");
        }
        sb.AppendLine();

        sb.AppendLine("### 4. Breakdown by Assigned To");
        sb.AppendLine();
        sb.AppendLine("| Assignee | Count | Percentage | Completion Rate |");
        sb.AppendLine("|----------|-------|------------|----------------|");
        foreach (var assignee in analysis.AssigneeBreakdown.OrderByDescending(a => a.Value))
        {
            var percentage = (double)assignee.Value / analysis.TotalItems * 100;
            var completed = analysis.CompletedByAssignee.GetValueOrDefault(assignee.Key, 0);
            var completionRate = assignee.Value > 0 ? (double)completed / assignee.Value * 100 : 0;
            var escapedAssignee = MarkdownHelper.EscapeTableCell(assignee.Key);
            sb.AppendLine($"| {escapedAssignee} | {assignee.Value} | {percentage:F2}% | {completionRate:F2}% |");
        }
        sb.AppendLine();

        sb.AppendLine("### 5. Risk Analysis");
        sb.AppendLine();
        sb.AppendLine($"- **Unassigned Work Items:** {analysis.UnassignedItems.Count}");
        sb.AppendLine($"- **Blocked Items:** {analysis.BlockedItems.Count}");
        sb.AppendLine();
        if (analysis.BlockedItems.Any())
        {
            sb.AppendLine("**Blocked Items Details:**");
            sb.AppendLine();
            foreach (var item in analysis.BlockedItems.Take(10))
            {
                var reason = string.IsNullOrWhiteSpace(item.Reason) ? "Reason not provided" : item.Reason;
                sb.AppendLine($"- [{item.Id}] {item.Title} — Reason: {reason}");
            }
            if (analysis.BlockedItems.Count > 10)
            {
                sb.AppendLine($"- *... and {analysis.BlockedItems.Count - 10} more*");
            }
        }
        sb.AppendLine();

        return sb.ToString();
    }
}

