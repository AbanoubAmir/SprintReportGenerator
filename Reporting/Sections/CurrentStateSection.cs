using System.Linq;
using System.Text;
using System.Collections.Generic;
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

        var memberRoles = BuildMemberRoleMap(context.TeamCapacities);

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

        // Per-role view of assignees (roles sourced from capacity "Activity"; Unspecified means no capacity record)
        sb.AppendLine("### 4. Breakdown by Assigned To (per Role)");
        sb.AppendLine();

        var assigneesByRole = analysis.AssigneeBreakdown
            .GroupBy(kv => memberRoles.GetValueOrDefault(Normalize(kv.Key), "Unspecified"))
            .OrderByDescending(g => g.Sum(x => x.Value));

        foreach (var roleGroup in assigneesByRole)
        {
            sb.AppendLine($"#### Role: {roleGroup.Key}");
            sb.AppendLine();
            sb.AppendLine("| Assignee | Count | Percentage | Completion Rate |");
            sb.AppendLine("|----------|-------|------------|----------------|");

            foreach (var assignee in roleGroup.OrderByDescending(a => a.Value))
            {
                var percentage = (double)assignee.Value / analysis.TotalItems * 100;
                var completed = analysis.CompletedByAssignee.GetValueOrDefault(assignee.Key, 0);
                var completionRate = assignee.Value > 0 ? (double)completed / assignee.Value * 100 : 0;
                var escapedAssignee = MarkdownHelper.EscapeTableCell(assignee.Key);
                sb.AppendLine($"| {escapedAssignee} | {assignee.Value} | {percentage:F2}% | {completionRate:F2}% |");
            }
            sb.AppendLine();
        }

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

    private static Dictionary<string, string> BuildMemberRoleMap(IReadOnlyList<TeamCapacity>? capacities)
    {
        var map = new Dictionary<string, string>();
        if (capacities == null)
        {
            return map;
        }

        foreach (var cap in capacities)
        {
            var key = Normalize(cap.DisplayName);
            if (!map.ContainsKey(key))
            {
                map[key] = cap.Activity ?? "Unspecified";
            }
        }

        return map;
    }

    private static string Normalize(string name)
    {
        var trimmed = name.Split('<')[0].Trim();
        return trimmed.ToLowerInvariant();
    }
}

