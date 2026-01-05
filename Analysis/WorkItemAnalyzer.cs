using System.Linq;
using Microsoft.Extensions.Logging;
using SprintReportGenerator.Models;

namespace SprintReportGenerator.Analysis;

public class WorkItemAnalyzer : IWorkItemAnalyzer
{
    private readonly ILogger<WorkItemAnalyzer> logger;

    public WorkItemAnalyzer(ILogger<WorkItemAnalyzer> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public AnalysisResult Analyze(IEnumerable<WorkItem> workItems, DateTime? sprintStartDate, IReadOnlyList<int>? sprintIterationWorkItemIds = null)
    {
        var workItemsList = workItems.ToList();
        var iterationIds = sprintIterationWorkItemIds?.ToHashSet() ?? new HashSet<int>();
        
        logger.LogInformation("Starting work item analysis. Work item count: {WorkItemCount}, Sprint start date: {SprintStartDate}, Sprint iteration items: {IterationItemCount}",
            workItemsList.Count, sprintStartDate, iterationIds.Count);

        var result = new AnalysisResult
        {
            WorkItems = workItemsList,
            SprintStartDate = sprintStartDate
        };

        foreach (var item in result.WorkItems)
        {
            var isInSprintIteration = iterationIds.Contains(item.Id);
            if (isInSprintIteration)
            {
                result.SprintIterationItems.Add(item);
            }
            else
            {
                result.CrossIterationItems.Add(item);
            }

            var isOriginalPlan = !sprintStartDate.HasValue ||
                                 !item.CreatedDate.HasValue ||
                                 item.CreatedDate.Value < sprintStartDate.Value.AddDays(1);

            if (isOriginalPlan)
            {
                result.OriginalPlanItems.Add(item);
            }
            else
            {
                result.AddedItems.Add(item);
            }

            var isCompleted = WorkItemStatus.IsCompleted(item.State);
            if (isCompleted)
            {
                result.CompletedCount++;
            }
            else if (item.State.Equals("Active", StringComparison.OrdinalIgnoreCase) ||
                     item.State.Equals("In Progress", StringComparison.OrdinalIgnoreCase))
            {
                result.InProgressCount++;
            }
            else if (item.State.Equals("New", StringComparison.OrdinalIgnoreCase) ||
                     item.State.Equals("To Do", StringComparison.OrdinalIgnoreCase))
            {
                result.NotStartedCount++;
            }
            else if (item.State.Equals("Blocked", StringComparison.OrdinalIgnoreCase))
            {
                result.BlockedCount++;
            }

            result.StateBreakdown[item.State] = result.StateBreakdown.GetValueOrDefault(item.State, 0) + 1;
            result.TypeBreakdown[item.WorkItemType] = result.TypeBreakdown.GetValueOrDefault(item.WorkItemType, 0) + 1;
            result.PriorityBreakdown[item.Priority] = result.PriorityBreakdown.GetValueOrDefault(item.Priority, 0) + 1;
            result.AssigneeBreakdown[item.AssignedTo] = result.AssigneeBreakdown.GetValueOrDefault(item.AssignedTo, 0) + 1;

            if (isCompleted)
            {
                result.CompletedByType[item.WorkItemType] = result.CompletedByType.GetValueOrDefault(item.WorkItemType, 0) + 1;
                result.CompletedByPriority[item.Priority] = result.CompletedByPriority.GetValueOrDefault(item.Priority, 0) + 1;
                result.CompletedByAssignee[item.AssignedTo] = result.CompletedByAssignee.GetValueOrDefault(item.AssignedTo, 0) + 1;
            }

            if (item.OriginalEstimate.HasValue)
            {
                result.TotalOriginalEstimate += item.OriginalEstimate.Value;
            }

            if (item.CompletedWork.HasValue)
            {
                result.TotalCompletedWork += item.CompletedWork.Value;
            }

            if (item.RemainingWork.HasValue)
            {
                result.TotalRemainingWork += item.RemainingWork.Value;
            }

            if (string.IsNullOrEmpty(item.AssignedTo) || item.AssignedTo == "Unassigned")
            {
                result.UnassignedItems.Add(item);
            }

            if (item.State.Equals("Blocked", StringComparison.OrdinalIgnoreCase))
            {
                result.BlockedItems.Add(item);
            }
        }

        result.TotalItems = result.WorkItems.Count;
        
        logger.LogInformation("Analysis complete. Total: {TotalItems}, Completed: {CompletedCount}, In Progress: {InProgressCount}, Not Started: {NotStartedCount}, Blocked: {BlockedCount}, Original Plan: {OriginalPlanCount}, Added: {AddedCount}",
            result.TotalItems, result.CompletedCount, result.InProgressCount, result.NotStartedCount, result.BlockedCount, result.OriginalPlanItems.Count, result.AddedItems.Count);
        logger.LogInformation("Work breakdown - Original Estimate: {OriginalEstimate}h, Completed Work: {CompletedWork}h, Remaining Work: {RemainingWork}h",
            result.TotalOriginalEstimate, result.TotalCompletedWork, result.TotalRemainingWork);
        
        return result;
    }
}

