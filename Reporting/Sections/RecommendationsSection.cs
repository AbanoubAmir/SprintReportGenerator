using System.Text;
using SprintReportGenerator.Models;

namespace SprintReportGenerator.Reporting.Sections;

public class RecommendationsSection : IReportSection
{
    public string Title => "Suggested Actions for Next Sprint";

    public string Render(AnalysisResult analysis, ReportContext context)
    {
        var sb = new StringBuilder();
        MarkdownHelper.AppendHeader(sb, Title);

        var suggestions = new List<string>();

        // Scope and planning
        if (analysis.AddedItems.Count > 0)
        {
            suggestions.Add($"Control scope churn: {analysis.AddedItems.Count} items were added. Gate mid-sprint scope via PO sign-off and capacity checks.");
        }

        // Completion vs time
        if (context.EndDate.HasValue && analysis.SprintStartDate.HasValue)
        {
            var totalDays = (context.EndDate.Value - analysis.SprintStartDate.Value).Days;
            var elapsedDays = (context.GeneratedAt - analysis.SprintStartDate.Value).Days;
            var timeProgress = totalDays > 0 ? (double)elapsedDays / totalDays * 100 : 0;
            if (analysis.CompletedPercentage + 5 < timeProgress) // allow a small buffer
            {
                suggestions.Add("Delivery pacing: completion is behind time progress. Tighten daily re-planning and unblock top-priority items first.");
            }
        }

        // Blocked items
        if (analysis.BlockedItems.Any())
        {
            var reasonSamples = analysis.BlockedItems
                .Where(b => !string.IsNullOrWhiteSpace(b.Reason))
                .Select(b => b.Reason)
                .Distinct()
                .Take(3)
                .ToList();

            var reasonText = reasonSamples.Count > 0
                ? $" Common reasons: {string.Join("; ", reasonSamples)}."
                : string.Empty;

            suggestions.Add($"Unblock quickly: {analysis.BlockedItems.Count} blocked items. Add an explicit daily unblock pass and owner per blocker.{reasonText}");
        }

        // Unassigned items
        if (analysis.UnassignedItems.Any())
        {
            suggestions.Add($"Assignment hygiene: {analysis.UnassignedItems.Count} unassigned items. Enforce ownership at intake and standups.");
        }

        // Estimates
        if (analysis.TotalOriginalEstimate > 0)
        {
            var completionByEstimate = analysis.TotalCompletedWork / Math.Max(analysis.TotalOriginalEstimate, 1);
            if (completionByEstimate < 0.8)
            {
                suggestions.Add("Estimating: large under-run remaining. Use small batch sizing and mid-sprint estimate reviews for risky items.");
            }
            else if (completionByEstimate > 1.2)
            {
                suggestions.Add("Estimating: significant overrun. Calibrate estimates using recent velocity and break down large tasks earlier.");
            }
        }

        // Assignee load distribution (light heuristic)
        var maxAssigneeCount = analysis.AssigneeBreakdown.Any()
            ? analysis.AssigneeBreakdown.Max(kv => kv.Value)
            : 0;
        if (maxAssigneeCount > 0 && analysis.AssigneeBreakdown.Count > 0)
        {
            var avgLoad = (double)analysis.TotalItems / analysis.AssigneeBreakdown.Count;
            if (maxAssigneeCount > avgLoad * 1.5)
            {
                suggestions.Add("Work distribution: rebalance high-load assignees to reduce single-threading risk.");
            }
        }

        // Capacity-based insights
        var capacities = context.TeamCapacities ?? Array.Empty<TeamCapacity>();
        if (capacities.Count > 0)
        {
            string Normalize(string name) => name.Split('<')[0].Trim().ToLowerInvariant();

            var completedByAssignee = analysis.WorkItems
                .Where(w => w.CompletedWork.HasValue)
                .GroupBy(w => Normalize(w.AssignedTo))
                .ToDictionary(g => g.Key, g => g.Sum(w => w.CompletedWork ?? 0));

            var memberUtil = capacities.Select(c =>
            {
                var key = Normalize(c.DisplayName);
                var completed = completedByAssignee.GetValueOrDefault(key, 0);
                var util = c.TotalCapacityHours > 0 ? completed / c.TotalCapacityHours * 100 : 0;
                return new { c.DisplayName, Activity = c.Activity ?? "Unspecified", Util = util };
            }).ToList();

            var over = memberUtil.Where(m => m.Util > 120).OrderByDescending(m => m.Util).Take(3).ToList();
            var under = memberUtil.Where(m => m.Util < 60 && m.Util >= 0).OrderBy(m => m.Util).Take(3).ToList();

            if (over.Any())
            {
                suggestions.Add("Capacity: rebalance from over-utilized members " + string.Join(", ", over.Select(o => $"{o.DisplayName} ({o.Activity}, {o.Util:F0}%)")) + ".");
            }
            if (under.Any())
            {
                suggestions.Add("Capacity: assign more to under-utilized members " + string.Join(", ", under.Select(u => $"{u.DisplayName} ({u.Activity}, {u.Util:F0}%)")) + ".");
            }
        }

        // General cadence
        suggestions.Add("Retrospective follow-through: pick 1–2 improvements (unblocking, scope control) and track them as work items next sprint.");

        if (suggestions.Count == 0)
        {
            suggestions.Add("No critical recommendations detected. Maintain current cadence and continue monitoring blockers and scope changes.");
        }

        foreach (var suggestion in suggestions)
        {
            sb.AppendLine($"- {suggestion}");
        }

        sb.AppendLine();
        return sb.ToString();
    }
}

