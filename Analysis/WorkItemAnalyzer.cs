using System.Linq;
using SprintReportGenerator.Models;

namespace SprintReportGenerator.Analysis;

public class WorkItemAnalyzer : IWorkItemAnalyzer
{
    public AnalysisResult Analyze(IEnumerable<WorkItem> workItems, DateTime? sprintStartDate)
    {
        var result = new AnalysisResult
        {
            WorkItems = workItems.ToList(),
            SprintStartDate = sprintStartDate
        };

        foreach (var item in result.WorkItems)
        {
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
        return result;
    }
}

