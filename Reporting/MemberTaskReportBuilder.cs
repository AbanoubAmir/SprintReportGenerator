using System.Text;
using System.Linq;
using SprintReportGenerator.Analysis;
using SprintReportGenerator.Models;

namespace SprintReportGenerator.Reporting;

public class MemberTaskReportBuilder
{
    public string Build(SprintData sprintData, ReportContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Member Task Report: {context.SprintName}");
        sb.AppendLine();
        sb.AppendLine($"**Team:** {context.TeamName}  ");
        sb.AppendLine($"**Generated:** {context.GeneratedAt:yyyy-MM-dd HH:mm:ss}  ");

        if (context.StartDate.HasValue && context.EndDate.HasValue)
        {
            sb.AppendLine($"**Sprint Period:** {context.StartDate.Value:yyyy-MM-dd} to {context.EndDate.Value:yyyy-MM-dd}  ");
        }

        sb.AppendLine();

        var workItems = sprintData.WorkItems
            .Where(w =>
                w.WorkItemType.Equals("Task", StringComparison.OrdinalIgnoreCase) ||
                w.WorkItemType.Equals("Bug", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var normalizedFilters = NormalizeFilters(context.MemberFilters);
        if (normalizedFilters.Any())
        {
            workItems = workItems
                .Where(w => normalizedFilters.Contains(NormalizeName(w.AssignedTo)))
                .ToList();
        }

        if (normalizedFilters.Any())
        {
            sb.AppendLine($"> Filtered to members: {string.Join(", ", context.MemberFilters)}");
            sb.AppendLine();
        }

        if (workItems.Count == 0)
        {
            sb.AppendLine("> No tasks or bugs found for this sprint with the specified filters.");
            sb.AppendLine();
            return sb.ToString();
        }

        var stories = sprintData.WorkItems
            .Where(w => w.WorkItemType.Equals("User Story", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(w => w.Id, w => w);

        foreach (var task in workItems)
        {
            if (task.ParentId.HasValue && stories.TryGetValue(task.ParentId.Value, out var parent))
            {
                task.ParentTitle ??= parent.Title;
                task.ParentState ??= parent.State;
                task.ParentAssignedTo ??= parent.AssignedTo;
            }
        }

        var totalCompleted = workItems.Count(t => WorkItemStatus.IsCompleted(t.State));
        var totalOriginalEstimate = workItems.Sum(t => t.OriginalEstimate ?? 0);
        var totalCompletedWork = workItems.Sum(t => t.CompletedWork ?? 0);
        var totalRemainingWork = workItems.Sum(t => t.RemainingWork ?? 0);
        var totalTasks = workItems.Count(t => t.WorkItemType.Equals("Task", StringComparison.OrdinalIgnoreCase));
        var totalBugs = workItems.Count(t => t.WorkItemType.Equals("Bug", StringComparison.OrdinalIgnoreCase));

        sb.AppendLine("## Sprint Task Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| **Total Items (Tasks + Bugs)** | {workItems.Count} |");
        sb.AppendLine($"| **Tasks** | {totalTasks} |");
        sb.AppendLine($"| **Bugs** | {totalBugs} |");
        sb.AppendLine($"| **Completed Items** | {totalCompleted} |");
        sb.AppendLine($"| **Total Original Estimate (h)** | {totalOriginalEstimate:F1} |");
        sb.AppendLine($"| **Total Completed Work (h)** | {totalCompletedWork:F1} |");
        sb.AppendLine($"| **Total Remaining Work (h)** | {totalRemainingWork:F1} |");
        sb.AppendLine();

        var grouped = workItems
            .GroupBy(t => string.IsNullOrWhiteSpace(t.AssignedTo) ? "Unassigned" : t.AssignedTo)
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            var memberName = group.Key;
            var memberCompleted = group.Count(t => WorkItemStatus.IsCompleted(t.State));
            var memberOriginalEstimate = group.Sum(t => t.OriginalEstimate ?? 0);
            var memberCompletedWork = group.Sum(t => t.CompletedWork ?? 0);
            var memberRemainingWork = group.Sum(t => t.RemainingWork ?? 0);

            sb.AppendLine($"### {memberName}");
            sb.AppendLine();
            sb.AppendLine("| Metric | Value |");
            sb.AppendLine("|--------|-------|");
            sb.AppendLine($"| **Total Items** | {group.Count()} |");
            sb.AppendLine($"| **Completed Items** | {memberCompleted} |");
            sb.AppendLine($"| **Original Estimate (h)** | {memberOriginalEstimate:F1} |");
            sb.AppendLine($"| **Completed Work (h)** | {memberCompletedWork:F1} |");
            sb.AppendLine($"| **Remaining Work (h)** | {memberRemainingWork:F1} |");
            sb.AppendLine();

            sb.AppendLine("| # | ID | Type | Title | Status | Created | Completed/Updated | Orig Est (h) | Completed (h) | Remaining (h) | User Story | Story Status | Story Assignee |");
            sb.AppendLine("|---|----|------|-------|--------|---------|-------------------|--------------|---------------|---------------|------------|--------------|----------------|");

            var orderedTasks = group
                .OrderByDescending(t => WorkItemStatus.IsCompleted(t.State))
                .ThenBy(t => t.Id)
                .ToList();

            for (var i = 0; i < orderedTasks.Count; i++)
            {
                var task = orderedTasks[i];
                var status = WorkItemStatus.IsCompleted(task.State) ? "✅ Completed" : MarkdownHelper.EscapeTableCell(task.State);
                var type = MarkdownHelper.EscapeTableCell(task.WorkItemType);
                var title = MarkdownHelper.EscapeTableCell(task.Title);
                var storyTitle = string.IsNullOrWhiteSpace(task.ParentTitle)
                    ? "—"
                    : MarkdownHelper.EscapeTableCell(task.ParentTitle);
                var storyState = string.IsNullOrWhiteSpace(task.ParentState)
                    ? "—"
                    : MarkdownHelper.EscapeTableCell(task.ParentState);
                var storyAssignee = string.IsNullOrWhiteSpace(task.ParentAssignedTo)
                    ? "—"
                    : MarkdownHelper.EscapeTableCell(task.ParentAssignedTo);
                var created = task.CreatedDate?.ToString("yyyy-MM-dd") ?? "—";
                var completedOrUpdated = task.ClosedDate?.ToString("yyyy-MM-dd") ??
                                         task.ChangedDate?.ToString("yyyy-MM-dd") ?? "—";

                sb.AppendLine(
                    $"| {i + 1} | {task.Id} | {type} | {title} | {status} | {created} | {completedOrUpdated} | {task.OriginalEstimate?.ToString("F1") ?? "—"} | {task.CompletedWork?.ToString("F1") ?? "—"} | {task.RemainingWork?.ToString("F1") ?? "—"} | {storyTitle} | {storyState} | {storyAssignee} |");
            }

            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("*End of Member Task Report*");

        return sb.ToString();
    }

    private static HashSet<string> NormalizeFilters(IEnumerable<string> filters)
    {
        return filters
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(NormalizeName)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToHashSet();
    }

    private static string NormalizeName(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }
}


