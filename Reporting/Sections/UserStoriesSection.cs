using System.Linq;
using System.Text;
using SprintReportGenerator.Analysis;
using SprintReportGenerator.Models;
using SprintReportGenerator.Reporting;

namespace SprintReportGenerator.Reporting.Sections;

public class UserStoriesSection : IReportSection
{
    public string Title => "User Stories Breakdown";

    public string Render(AnalysisResult analysis, ReportContext context)
    {
        var sb = new StringBuilder();
        MarkdownHelper.AppendHeader(sb, Title);

        var userStories = analysis.WorkItems.Where(w => w.WorkItemType.Equals("User Story", StringComparison.OrdinalIgnoreCase)).ToList();
        if (userStories.Count == 0)
        {
            sb.AppendLine("> No user stories found for this sprint.");
            sb.AppendLine();
            return sb.ToString();
        }

        var originalStories = analysis.OriginalPlanItems.Where(w => w.WorkItemType.Equals("User Story", StringComparison.OrdinalIgnoreCase)).ToList();
        var addedStories = analysis.AddedItems.Where(w => w.WorkItemType.Equals("User Story", StringComparison.OrdinalIgnoreCase)).ToList();

        if (originalStories.Any())
        {
            var originalCompleted = originalStories.Count(w => WorkItemStatus.IsCompleted(w.State));
            var originalIncomplete = originalStories.Count - originalCompleted;

            sb.AppendLine($"### Original Plan User Stories: {originalStories.Count} stories");
            sb.AppendLine();
            sb.AppendLine("| Status | Count | Percentage |");
            sb.AppendLine("|--------|-------|------------|");
            sb.AppendLine($"| **Completed** | {originalCompleted} | {originalCompleted * 100.0 / Math.Max(originalStories.Count, 1):F2}% |");
            sb.AppendLine($"| **Incomplete** | {originalIncomplete} | {originalIncomplete * 100.0 / Math.Max(originalStories.Count, 1):F2}% |");
            sb.AppendLine();

            var completedStories = originalStories.Where(w => WorkItemStatus.IsCompleted(w.State)).Take(50).ToList();
            if (completedStories.Any())
            {
                sb.AppendLine("#### ✅ Completed User Stories");
                sb.AppendLine();
                foreach (var story in completedStories)
                {
                    sb.AppendLine($"- [{story.Id}] {story.Title}");
                }
                if (originalCompleted > 50)
                {
                    sb.AppendLine($"- *... and {originalCompleted - 50} more*");
                }
                sb.AppendLine();
            }

            if (originalIncomplete > 0)
            {
                var incompleteStories = originalStories.Where(w => !WorkItemStatus.IsCompleted(w.State)).Take(50).ToList();
                sb.AppendLine("#### ⚠️ Incomplete User Stories");
                sb.AppendLine();
                foreach (var story in incompleteStories)
                {
                    sb.AppendLine($"- [{story.Id}] {story.Title}");
                }
                if (originalIncomplete > 50)
                {
                    sb.AppendLine($"- *... and {originalIncomplete - 50} more*");
                }
                sb.AppendLine();
            }
        }

        if (addedStories.Any())
        {
            var addedCompleted = addedStories.Count(w => WorkItemStatus.IsCompleted(w.State));
            sb.AppendLine($"### Added During Sprint User Stories: {addedStories.Count} stories");
            sb.AppendLine();
            sb.AppendLine($"**Status:** {(addedCompleted == addedStories.Count ? "✅ All Completed" : "⚠️ Partially Complete")} ({addedCompleted * 100.0 / Math.Max(addedStories.Count, 1):F2}% completion)");
            sb.AppendLine();

            sb.AppendLine("| # | ID | Title | Added | Status | Priority | Assigned |");
            sb.AppendLine("|---|----|------|-------|--------|----------|----------|");
            for (var index = 0; index < addedStories.Count; index++)
            {
                var story = addedStories[index];
                var status = WorkItemStatus.IsCompleted(story.State) ? "✅ Completed" : "⚠️ Incomplete";
                var addedDate = story.CreatedDate?.ToString("yyyy-MM-dd") ?? "Unknown";
                var escapedTitle = MarkdownHelper.EscapeTableCell(story.Title);
                var escapedAssignee = MarkdownHelper.EscapeTableCell(story.AssignedTo);
                sb.AppendLine($"| {index + 1} | {story.Id} | {escapedTitle} | {addedDate} | {status} | {story.Priority} | {escapedAssignee} |");
            }
            sb.AppendLine();
        }

        var totalCompleted = userStories.Count(w => WorkItemStatus.IsCompleted(w.State));
        sb.AppendLine("### Summary");
        sb.AppendLine();
        sb.AppendLine("| Category | Count | Completed | Completion Rate |");
        sb.AppendLine("|----------|-------|-----------|----------------|");
        sb.AppendLine($"| **Original Plan** | {originalStories.Count} | {originalStories.Count(w => WorkItemStatus.IsCompleted(w.State))} | {(originalStories.Count > 0 ? originalStories.Count(w => WorkItemStatus.IsCompleted(w.State)) * 100.0 / originalStories.Count : 0):F2}% |");
        sb.AppendLine($"| **Added During Sprint** | {addedStories.Count} | {addedStories.Count(w => WorkItemStatus.IsCompleted(w.State))} | {(addedStories.Count > 0 ? addedStories.Count(w => WorkItemStatus.IsCompleted(w.State)) * 100.0 / addedStories.Count : 0):F2}% |");
        sb.AppendLine($"| **Total User Stories** | {userStories.Count} | {totalCompleted} | {totalCompleted * 100.0 / Math.Max(userStories.Count, 1):F2}% |");
        sb.AppendLine();

        return sb.ToString();
    }
}

