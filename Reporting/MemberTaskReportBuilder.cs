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
        var filteredItems = normalizedFilters.Any()
            ? workItems.Where(w => ResolveOwner(w, normalizedFilters) != null).ToList()
            : workItems;

        if (normalizedFilters.Any())
        {
            sb.AppendLine($"> Filtered to members (includes reassigned items credited by activity): {string.Join(", ", context.MemberFilters)}");
            sb.AppendLine();
        }

        if (filteredItems.Count == 0)
        {
            sb.AppendLine("> No tasks or bugs found for this sprint with the specified filters.");
            sb.AppendLine();
            return sb.ToString();
        }

        var stories = sprintData.WorkItems
            .Where(w => w.WorkItemType.Equals("User Story", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(w => w.Id, w => w);

        foreach (var task in filteredItems)
        {
            if (task.ParentId.HasValue && stories.TryGetValue(task.ParentId.Value, out var parent))
            {
                task.ParentTitle ??= parent.Title;
                task.ParentState ??= parent.State;
                task.ParentAssignedTo ??= parent.AssignedTo;
            }
        }

        var totalCompleted = filteredItems.Count(t => WorkItemStatus.IsCompleted(t.State));
        var totalOriginalEstimate = filteredItems.Sum(t => t.OriginalEstimate ?? 0);
        var totalCompletedWork = filteredItems.Sum(t => t.CompletedWork ?? 0);
        var totalRemainingWork = filteredItems.Sum(t => t.RemainingWork ?? 0);
        var totalTasks = filteredItems.Count(t => t.WorkItemType.Equals("Task", StringComparison.OrdinalIgnoreCase));
        var totalBugs = filteredItems.Count(t => t.WorkItemType.Equals("Bug", StringComparison.OrdinalIgnoreCase));

        sb.AppendLine("## Sprint Task Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| **Total Items (Tasks + Bugs)** | {filteredItems.Count} |");
        sb.AppendLine($"| **Tasks** | {totalTasks} |");
        sb.AppendLine($"| **Bugs** | {totalBugs} |");
        sb.AppendLine($"| **Completed Items** | {totalCompleted} |");
        sb.AppendLine($"| **Total Original Estimate (h)** | {totalOriginalEstimate:F1} |");
        sb.AppendLine($"| **Total Completed Work (h)** | {totalCompletedWork:F1} |");
        sb.AppendLine($"| **Total Remaining Work (h)** | {totalRemainingWork:F1} |");
        sb.AppendLine();

        var grouped = filteredItems
            .Select(w => (WorkItem: w, Owner: ResolveOwner(w, normalizedFilters)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Owner))
            .GroupBy(x => x.Owner!)
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            var memberName = group.Key;
            var memberCompleted = group.Count(t => WorkItemStatus.IsCompleted(t.WorkItem.State));
            var memberOriginalEstimate = group.Sum(t => t.WorkItem.OriginalEstimate ?? 0);
            var memberCompletedWork = group.Sum(t => t.WorkItem.CompletedWork ?? 0);
            var memberRemainingWork = group.Sum(t => t.WorkItem.RemainingWork ?? 0);

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

            sb.AppendLine("| # | ID | Type | Title | Status | Created | Completed/Updated | Orig Est (h) | Completed (h) | Remaining (h) | Current Assignee | User Story | Story Status | Story Assignee |");
            sb.AppendLine("|---|----|------|-------|--------|---------|-------------------|--------------|---------------|---------------|------------------|------------|--------------|----------------|");

            var orderedTasks = group
                .OrderByDescending(t => WorkItemStatus.IsCompleted(t.WorkItem.State))
                .ThenBy(t => t.WorkItem.Id)
                .ToList();

            for (var i = 0; i < orderedTasks.Count; i++)
            {
                var task = orderedTasks[i].WorkItem;
                var status = WorkItemStatus.IsCompleted(task.State) ? "✅ Completed" : MarkdownHelper.EscapeTableCell(task.State);
                var type = MarkdownHelper.EscapeTableCell(task.WorkItemType);
                var idCell = MarkdownHelper.BuildWorkItemLink(task.Id, context.WorkItemUrlBase, escapeForTable: true);
                var title = MarkdownHelper.EscapeTableCell(task.Title);
                var storyTitle = string.IsNullOrWhiteSpace(task.ParentTitle)
                    ? "—"
                    : MarkdownHelper.EscapeTableCell(task.ParentTitle);
                var storyLink = MarkdownHelper.BuildWorkItemLink(task.ParentId, context.WorkItemUrlBase, storyTitle, escapeForTable: true);
                var storyState = string.IsNullOrWhiteSpace(task.ParentState)
                    ? "—"
                    : MarkdownHelper.EscapeTableCell(task.ParentState);
                var storyAssignee = string.IsNullOrWhiteSpace(task.ParentAssignedTo)
                    ? "—"
                    : MarkdownHelper.EscapeTableCell(task.ParentAssignedTo);
                var created = task.CreatedDate?.ToString("yyyy-MM-dd") ?? "—";
                var completedOrUpdated = task.ClosedDate?.ToString("yyyy-MM-dd") ??
                                         task.ChangedDate?.ToString("yyyy-MM-dd") ?? "—";
                var currentAssignee = MarkdownHelper.EscapeTableCell(task.AssignedTo);

                sb.AppendLine(
                    $"| {i + 1} | {idCell} | {type} | {title} | {status} | {created} | {completedOrUpdated} | {task.OriginalEstimate?.ToString("F1") ?? "—"} | {task.CompletedWork?.ToString("F1") ?? "—"} | {task.RemainingWork?.ToString("F1") ?? "—"} | {currentAssignee} | {storyLink} | {storyState} | {storyAssignee} |");
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

    private static string? ResolveOwner(WorkItem workItem, HashSet<string> normalizedFilters)
    {
        var assigned = NormalizeName(workItem.AssignedTo);
        var activated = NormalizeName(workItem.ActivatedBy);
        var resolved = NormalizeName(workItem.ResolvedBy);
        var closed = NormalizeName(workItem.ClosedBy);

        // No filters: fall back to current assignee (existing behavior)
        if (!normalizedFilters.Any())
        {
            return string.IsNullOrWhiteSpace(workItem.AssignedTo) ? "Unassigned" : workItem.AssignedTo;
        }

        // Prefer matches in order: current assignee, resolved by, closed by, activated by
        if (normalizedFilters.Contains(assigned) && !string.IsNullOrWhiteSpace(workItem.AssignedTo))
        {
            return workItem.AssignedTo;
        }
        if (normalizedFilters.Contains(resolved) && !string.IsNullOrWhiteSpace(workItem.ResolvedBy))
        {
            return workItem.ResolvedBy;
        }
        if (normalizedFilters.Contains(closed) && !string.IsNullOrWhiteSpace(workItem.ClosedBy))
        {
            return workItem.ClosedBy;
        }
        if (normalizedFilters.Contains(activated) && !string.IsNullOrWhiteSpace(workItem.ActivatedBy))
        {
            return workItem.ActivatedBy;
        }

        return null;
    }
}


