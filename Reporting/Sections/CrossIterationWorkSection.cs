using System.Linq;
using System.Text;
using SprintReportGenerator.Analysis;
using SprintReportGenerator.Models;
using SprintReportGenerator.Reporting;

namespace SprintReportGenerator.Reporting.Sections;

public class CrossIterationWorkSection : IReportSection
{
    public string Title => "Cross-Iteration Work Analysis";

    public string Render(AnalysisResult analysis, ReportContext context)
    {
        var sb = new StringBuilder();
        MarkdownHelper.AppendHeader(sb, Title);

        if (analysis.CrossIterationItems.Count == 0)
        {
            sb.AppendLine("> No cross-iteration work was tracked during this sprint period.");
            sb.AppendLine();
            return sb.ToString();
        }

        sb.AppendLine("This section shows work items from other iterations that were actively worked on during this sprint period. ");
        sb.AppendLine("This helps explain team allocation and effort distribution beyond the sprint's planned iteration.");
        sb.AppendLine();

        var crossIterationCompleted = analysis.CrossIterationItems.Count(w => WorkItemStatus.IsCompleted(w.State));
        var crossIterationInProgress = analysis.CrossIterationItems.Count(w => 
            w.State.Equals("Active", StringComparison.OrdinalIgnoreCase) || 
            w.State.Equals("In Progress", StringComparison.OrdinalIgnoreCase));

        var crossIterationOriginalEstimate = analysis.CrossIterationItems.Sum(w => w.OriginalEstimate ?? 0);
        var crossIterationCompletedWork = analysis.CrossIterationItems.Sum(w => w.CompletedWork ?? 0);
        var crossIterationRemainingWork = analysis.CrossIterationItems.Sum(w => w.RemainingWork ?? 0);

        var sprintIterationOriginalEstimate = analysis.SprintIterationItems.Sum(w => w.OriginalEstimate ?? 0);
        var sprintIterationCompletedWork = analysis.SprintIterationItems.Sum(w => w.CompletedWork ?? 0);
        var sprintIterationRemainingWork = analysis.SprintIterationItems.Sum(w => w.RemainingWork ?? 0);

        var totalOriginalEstimate = crossIterationOriginalEstimate + sprintIterationOriginalEstimate;
        var totalCompletedWork = crossIterationCompletedWork + sprintIterationCompletedWork;
        var totalRemainingWork = crossIterationRemainingWork + sprintIterationRemainingWork;

        sb.AppendLine("### Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Sprint Iteration | Cross-Iteration | Total |");
        sb.AppendLine("|--------|------------------|-----------------|-------|");
        sb.AppendLine($"| **Total Items** | {analysis.SprintIterationItems.Count} | {analysis.CrossIterationItems.Count} | {analysis.TotalItems} |");
        sb.AppendLine($"| **Completed Items** | {analysis.SprintIterationItems.Count(w => WorkItemStatus.IsCompleted(w.State))} | {crossIterationCompleted} | {analysis.CompletedCount} |");
        sb.AppendLine($"| **In Progress** | {analysis.SprintIterationItems.Count(w => w.State.Equals("Active", StringComparison.OrdinalIgnoreCase) || w.State.Equals("In Progress", StringComparison.OrdinalIgnoreCase))} | {crossIterationInProgress} | {analysis.InProgressCount} |");
        sb.AppendLine($"| **Original Estimate (h)** | {sprintIterationOriginalEstimate:F1} | {crossIterationOriginalEstimate:F1} | {totalOriginalEstimate:F1} |");
        sb.AppendLine($"| **Completed Work (h)** | {sprintIterationCompletedWork:F1} | {crossIterationCompletedWork:F1} | {totalCompletedWork:F1} |");
        sb.AppendLine($"| **Remaining Work (h)** | {sprintIterationRemainingWork:F1} | {crossIterationRemainingWork:F1} | {totalRemainingWork:F1} |");
        sb.AppendLine();

        if (totalCompletedWork > 0)
        {
            var crossIterationPercentage = (crossIterationCompletedWork / totalCompletedWork) * 100;
            var sprintIterationPercentage = (sprintIterationCompletedWork / totalCompletedWork) * 100;
            
            sb.AppendLine("### Effort Distribution");
            sb.AppendLine();
            sb.AppendLine($"**Completed Work Distribution:**");
            sb.AppendLine($"- Sprint Iteration: {sprintIterationPercentage:F1}% ({sprintIterationCompletedWork:F1}h)");
            sb.AppendLine($"- Cross-Iteration: {crossIterationPercentage:F1}% ({crossIterationCompletedWork:F1}h)");
            sb.AppendLine();
        }

        sb.AppendLine("### Cross-Iteration Work Items by Type");
        sb.AppendLine();
        sb.AppendLine("| Type | Count | Completed | Original Estimate (h) | Completed Work (h) | Remaining Work (h) |");
        sb.AppendLine("|------|-------|-----------|----------------------|-------------------|-------------------|");
        
        var crossIterationByType = analysis.CrossIterationItems
            .GroupBy(w => w.WorkItemType)
            .OrderByDescending(g => g.Count())
            .ToList();

        foreach (var group in crossIterationByType)
        {
            var type = group.Key;
            var count = group.Count();
            var completed = group.Count(w => WorkItemStatus.IsCompleted(w.State));
            var origEst = group.Sum(w => w.OriginalEstimate ?? 0);
            var completedWork = group.Sum(w => w.CompletedWork ?? 0);
            var remainingWork = group.Sum(w => w.RemainingWork ?? 0);
            var escapedType = MarkdownHelper.EscapeTableCell(type);
            
            sb.AppendLine($"| {escapedType} | {count} | {completed} | {origEst:F1} | {completedWork:F1} | {remainingWork:F1} |");
        }
        sb.AppendLine();

        sb.AppendLine("### Cross-Iteration Work Items by Assignee");
        sb.AppendLine();
        sb.AppendLine("| Assignee | Count | Completed | Original Estimate (h) | Completed Work (h) | Remaining Work (h) |");
        sb.AppendLine("|----------|-------|-----------|----------------------|-------------------|-------------------|");
        
        var crossIterationByAssignee = analysis.CrossIterationItems
            .GroupBy(w => string.IsNullOrWhiteSpace(w.AssignedTo) ? "Unassigned" : w.AssignedTo)
            .OrderByDescending(g => g.Sum(w => w.CompletedWork ?? 0))
            .ToList();

        foreach (var group in crossIterationByAssignee)
        {
            var assignee = group.Key;
            var count = group.Count();
            var completed = group.Count(w => WorkItemStatus.IsCompleted(w.State));
            var origEst = group.Sum(w => w.OriginalEstimate ?? 0);
            var completedWork = group.Sum(w => w.CompletedWork ?? 0);
            var remainingWork = group.Sum(w => w.RemainingWork ?? 0);
            var escapedAssignee = MarkdownHelper.EscapeTableCell(assignee);
            
            sb.AppendLine($"| {escapedAssignee} | {count} | {completed} | {origEst:F1} | {completedWork:F1} | {remainingWork:F1} |");
        }
        sb.AppendLine();

        if (analysis.CrossIterationItems.Count > 0 && totalCompletedWork > 0)
        {
            var crossIterationEffortPercentage = (crossIterationCompletedWork / totalCompletedWork) * 100;
            sb.AppendLine("### Insights");
            sb.AppendLine();
            sb.AppendLine($"- **{crossIterationEffortPercentage:F1}%** of completed work effort was spent on items from other iterations.");
            sb.AppendLine($"- This represents **{analysis.CrossIterationItems.Count}** work items actively worked on during the sprint period.");
            if (crossIterationEffortPercentage > 20)
            {
                sb.AppendLine("> ⚠️ **Note:** A significant portion of team effort was allocated to cross-iteration work. ");
                sb.AppendLine("> Consider reviewing sprint planning and capacity allocation to ensure sprint goals are achievable.");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

