using System.Linq;
using System.Text;
using SprintReportGenerator.Models;

namespace SprintReportGenerator.Reporting.Sections;

public class CapacitySection : IReportSection
{
    public string Title => "Capacity vs Delivery";

    public string Render(AnalysisResult analysis, ReportContext context)
    {
        var capacities = context.TeamCapacities;
        if (capacities == null || capacities.Count == 0)
        {
            return "## Capacity vs Delivery\n\n> Capacity data not available for this sprint.\n";
        }

        string Normalize(string name)
        {
            var trimmed = name.Split('<')[0].Trim();
            return trimmed.ToLowerInvariant();
        }

        var completedByAssignee = analysis.WorkItems
            .Where(w => w.CompletedWork.HasValue)
            .GroupBy(w => Normalize(w.AssignedTo))
            .ToDictionary(g => g.Key, g => g.Sum(w => w.CompletedWork ?? 0));

        var remainingByAssignee = analysis.WorkItems
            .Where(w => w.RemainingWork.HasValue)
            .GroupBy(w => Normalize(w.AssignedTo))
            .ToDictionary(g => g.Key, g => g.Sum(w => w.RemainingWork ?? 0));

        var sb = new StringBuilder();
        MarkdownHelper.AppendHeader(sb, Title);
        sb.AppendLine("> Roles are taken from capacity activities; \"Unspecified\" means no capacity record for that member.");
        sb.AppendLine();

        // By activity/role summary
        sb.AppendLine("### By Role");
        sb.AppendLine();
        sb.AppendLine("| Activity | Capacity (h) | Completed (h) | Remaining (h) | Utilization |");
        sb.AppendLine("|----------|--------------|---------------|---------------|-------------|");

        var capacitiesByActivity = capacities
            .GroupBy(c => c.Activity ?? "Unspecified")
            .Select(g => new
            {
                Activity = g.Key,
                Capacity = g.Sum(x => x.TotalCapacityHours),
                Members = g.Select(x => Normalize(x.DisplayName)).ToHashSet(StringComparer.OrdinalIgnoreCase),
                RawMembers = g.ToList()
            })
            .ToList();

        foreach (var entry in capacitiesByActivity.OrderByDescending(e => e.Capacity))
        {
            var completed = completedByAssignee
                .Where(kv => entry.Members.Contains(kv.Key))
                .Sum(kv => kv.Value);

            var remaining = remainingByAssignee
                .Where(kv => entry.Members.Contains(kv.Key))
                .Sum(kv => kv.Value);

            var utilization = entry.Capacity > 0 ? completed / entry.Capacity * 100 : 0;

            sb.AppendLine($"| {entry.Activity} | {entry.Capacity:F1} | {completed:F1} | {remaining:F1} | {utilization:F1}% |");
        }

        sb.AppendLine();

        // Per-role member breakdown
        foreach (var entry in capacitiesByActivity.OrderByDescending(e => e.Capacity))
        {
            sb.AppendLine($"#### Role: {entry.Activity}");
            sb.AppendLine();
            sb.AppendLine("| Member | Capacity (h) | Completed (h) | Remaining (h) | Utilization |");
            sb.AppendLine("|--------|--------------|---------------|---------------|-------------|");

            foreach (var cap in entry.RawMembers.OrderByDescending(c => c.TotalCapacityHours))
            {
                var key = Normalize(cap.DisplayName);
                var completed = completedByAssignee.GetValueOrDefault(key, 0);
                var remaining = remainingByAssignee.GetValueOrDefault(key, 0);
                var utilization = cap.TotalCapacityHours > 0
                    ? completed / cap.TotalCapacityHours * 100
                    : 0;

                sb.AppendLine($"| {cap.DisplayName} | {cap.TotalCapacityHours:F1} | {completed:F1} | {remaining:F1} | {utilization:F1}% |");
            }

            sb.AppendLine();
        }

        // Callouts
        var memberUtilization = capacities
            .Select(c =>
            {
                var key = Normalize(c.DisplayName);
                var completed = completedByAssignee.GetValueOrDefault(key, 0);
                var utilization = c.TotalCapacityHours > 0 ? completed / c.TotalCapacityHours * 100 : 0;
                return new
                {
                    c.DisplayName,
                    c.Activity,
                    c.TotalCapacityHours,
                    Utilization = utilization
                };
            })
            .ToList();

        var callouts = new List<string>();

        var overUtilized = memberUtilization
            .Where(x => x.TotalCapacityHours > 0 && x.Utilization > 110)
            .OrderByDescending(x => x.Utilization)
            .Take(5)
            .ToList();

        if (overUtilized.Any())
        {
            callouts.Add("Over-utilized: " + string.Join("; ", overUtilized.Select(o => $"{o.DisplayName} ({o.Activity ?? "Unspecified"}: {o.Utilization:F0}%)")));
        }

        var underUtilized = memberUtilization
            .Where(x => x.TotalCapacityHours > 0 && x.Utilization < 60)
            .OrderBy(x => x.Utilization)
            .Take(5)
            .ToList();

        if (underUtilized.Any())
        {
            callouts.Add("Under-utilized: " + string.Join("; ", underUtilized.Select(u => $"{u.DisplayName} ({u.Activity ?? "Unspecified"}: {u.Utilization:F0}%)")));
        }

        if (callouts.Any())
        {
            sb.AppendLine("### Highlights");
            sb.AppendLine();
            foreach (var c in callouts)
            {
                sb.AppendLine($"- {c}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

