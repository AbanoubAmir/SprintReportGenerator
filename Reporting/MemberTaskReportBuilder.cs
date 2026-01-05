using System.Text;
using System.Linq;
using Microsoft.Extensions.Logging;
using SprintReportGenerator.Analysis;
using SprintReportGenerator.Models;

namespace SprintReportGenerator.Reporting;

public class MemberTaskReportBuilder
{
    private readonly ILogger<MemberTaskReportBuilder> logger;

    public MemberTaskReportBuilder(ILogger<MemberTaskReportBuilder> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Build(SprintData sprintData, ReportContext context)
    {
        logger.LogInformation("Building member task report for sprint: {SprintName}", context.SprintName);
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

        var workItems = sprintData.WorkItems.ToList();
        logger.LogDebug("Found {WorkItemCount} work items (all types included)", workItems.Count);

        var normalizedFilters = NormalizeFilters(context.MemberFilters);
        var sprintEndDate = context.EndDate?.AddDays(1);
        var iterationWorkItemIds = sprintData.IterationWorkItemIds.ToHashSet();

        var filteredItems = workItems.Where(w =>
        {
            var owner = ResolveOwner(w, normalizedFilters);
            var isInSprintIteration = iterationWorkItemIds.Contains(w.Id);
            
            if (normalizedFilters.Any() && owner == null)
            {
                return false;
            }

            if (isInSprintIteration)
            {
                return true;
            }

            if (context.StartDate.HasValue && sprintEndDate.HasValue)
            {
                var wasActiveDuringSprint = false;
                var wasActiveAfterSprint = false;
                
                if (w.ChangedDate.HasValue)
                {
                    if (w.ChangedDate.Value >= context.StartDate.Value && w.ChangedDate.Value < sprintEndDate.Value)
                    {
                        wasActiveDuringSprint = true;
                    }
                    else if (w.ChangedDate.Value >= sprintEndDate.Value)
                    {
                        wasActiveAfterSprint = true;
                    }
                }
                
                if (w.ClosedDate.HasValue)
                {
                    if (w.ClosedDate.Value >= context.StartDate.Value && w.ClosedDate.Value < sprintEndDate.Value)
                    {
                        wasActiveDuringSprint = true;
                    }
                    else if (w.ClosedDate.Value >= sprintEndDate.Value)
                    {
                        wasActiveAfterSprint = true;
                    }
                }
                
                return wasActiveDuringSprint || wasActiveAfterSprint;
            }

            return true;
        }).ToList();

        if (normalizedFilters.Any())
        {
            logger.LogInformation("Filtering to {MemberCount} member(s): {Members}", normalizedFilters.Count, string.Join(", ", context.MemberFilters));
        }

        if (context.StartDate.HasValue && context.EndDate.HasValue)
        {
            logger.LogInformation("Including work items active during sprint period: {StartDate} to {EndDate} (including cross-iteration work)", 
                context.StartDate.Value, context.EndDate.Value);
        }

        logger.LogDebug("Filtered from {OriginalCount} to {FilteredCount} items", workItems.Count, filteredItems.Count);

        if (normalizedFilters.Any())
        {
            sb.AppendLine($"> Filtered to members (includes reassigned items credited by activity): {string.Join(", ", context.MemberFilters)}");
            sb.AppendLine();
        }

        if (context.StartDate.HasValue && context.EndDate.HasValue)
        {
            sb.AppendLine($"> Includes all work item types, cross-iteration work active during sprint period ({context.StartDate.Value:yyyy-MM-dd} to {context.EndDate.Value:yyyy-MM-dd}), and work in this sprint completed after sprint end");
            sb.AppendLine();
        }

        if (filteredItems.Count == 0)
        {
            logger.LogWarning("No work items found for sprint {SprintName} with the specified filters", context.SprintName);
            sb.AppendLine("> No work items found for this sprint with the specified filters.");
            sb.AppendLine();
            return sb.ToString();
        }

        logger.LogDebug("Resolving parent work items (User Stories, Features, Epics)");
        var parentWorkItems = sprintData.WorkItems
            .Where(w => w.WorkItemType.Equals("User Story", StringComparison.OrdinalIgnoreCase) ||
                       w.WorkItemType.Equals("Feature", StringComparison.OrdinalIgnoreCase) ||
                       w.WorkItemType.Equals("Epic", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(w => w.Id, w => w);
        logger.LogDebug("Found {ParentCount} parent work items", parentWorkItems.Count);

        foreach (var item in filteredItems)
        {
            if (item.ParentId.HasValue && parentWorkItems.TryGetValue(item.ParentId.Value, out var parent))
            {
                item.ParentTitle ??= parent.Title;
                item.ParentState ??= parent.State;
                item.ParentAssignedTo ??= parent.AssignedTo;
            }
        }

        var totalCompleted = filteredItems.Count(t => WorkItemStatus.IsCompleted(t.State));
        var totalOriginalEstimate = filteredItems.Sum(t => t.OriginalEstimate ?? 0);
        var totalCompletedWork = filteredItems.Sum(t => t.CompletedWork ?? 0);
        var totalRemainingWork = filteredItems.Sum(t => t.RemainingWork ?? 0);
        
        var workItemTypeBreakdown = filteredItems
            .GroupBy(t => t.WorkItemType)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Count());

        sb.AppendLine("## Sprint Work Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| **Total Items** | {filteredItems.Count} |");
        foreach (var typeGroup in workItemTypeBreakdown)
        {
            sb.AppendLine($"| **{typeGroup.Key}** | {typeGroup.Value} |");
        }
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

        logger.LogInformation("Grouping items by {MemberCount} member(s)", grouped.Count());
        foreach (var group in grouped)
        {
            var memberName = group.Key;
            logger.LogDebug("Processing member: {MemberName} with {ItemCount} items", memberName, group.Count());
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

            sb.AppendLine("| # | ID | Type | Title | Status | Created | Completed/Updated | Orig Est (h) | Completed (h) | Remaining (h) | Current Assignee | Parent Work Item | Parent Status | Parent Assignee |");
            sb.AppendLine("|---|----|------|-------|--------|---------|-------------------|--------------|---------------|---------------|------------------|-----------------|---------------|------------------|");

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

        var report = sb.ToString();
        logger.LogInformation("Member task report built successfully. Report length: {ReportLength} characters", report.Length);
        return report;
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
        var assignedUnique = NormalizeName(workItem.AssignedToUniqueName);
        var activated = NormalizeName(workItem.ActivatedBy);
        var activatedUnique = NormalizeName(workItem.ActivatedByUniqueName);
        var resolved = NormalizeName(workItem.ResolvedBy);
        var resolvedUnique = NormalizeName(workItem.ResolvedByUniqueName);
        var closed = NormalizeName(workItem.ClosedBy);
        var closedUnique = NormalizeName(workItem.ClosedByUniqueName);

        // No filters: fall back to current assignee (existing behavior)
        if (!normalizedFilters.Any())
        {
            return string.IsNullOrWhiteSpace(workItem.AssignedTo) ? "Unassigned" : workItem.AssignedTo;
        }

        // Prefer matches in order: current assignee, resolved by, closed by, activated by
        if ((normalizedFilters.Contains(assigned) || normalizedFilters.Contains(assignedUnique)) && !string.IsNullOrWhiteSpace(workItem.AssignedTo))
        {
            return workItem.AssignedTo;
        }
        if ((normalizedFilters.Contains(resolved) || normalizedFilters.Contains(resolvedUnique)) && !string.IsNullOrWhiteSpace(workItem.ResolvedBy))
        {
            return workItem.ResolvedBy;
        }
        if ((normalizedFilters.Contains(closed) || normalizedFilters.Contains(closedUnique)) && !string.IsNullOrWhiteSpace(workItem.ClosedBy))
        {
            return workItem.ClosedBy;
        }
        if ((normalizedFilters.Contains(activated) || normalizedFilters.Contains(activatedUnique)) && !string.IsNullOrWhiteSpace(workItem.ActivatedBy))
        {
            return workItem.ActivatedBy;
        }

        return null;
    }
}

