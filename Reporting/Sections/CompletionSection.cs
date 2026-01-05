using System.Text;
using SprintReportGenerator.Models;
using SprintReportGenerator.Reporting;

namespace SprintReportGenerator.Reporting.Sections;

public class CompletionSection : IReportSection
{
    public string Title => "Completion Analysis";

    public string Render(AnalysisResult analysis, ReportContext context)
    {
        var sb = new StringBuilder();
        MarkdownHelper.AppendHeader(sb, Title, 3);

        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| **Work Items Completion** | {analysis.CompletedPercentage:F2}% |");

        if (context.EndDate.HasValue)
        {
            var now = context.GeneratedAt;
            var totalDays = (context.EndDate.Value - (analysis.SprintStartDate ?? now)).Days;
            var elapsedDays = (now - (analysis.SprintStartDate ?? now)).Days;
            var timeProgress = totalDays > 0 ? (double)elapsedDays / totalDays * 100 : 0;
            var status = analysis.CompletedPercentage < timeProgress ? "⚠️ Behind Schedule" : "✅ On Track";
            var difference = analysis.CompletedPercentage - timeProgress;

            sb.AppendLine($"| **Time Progress** | {timeProgress:F0}% |");
            sb.AppendLine($"| **Status** | {status} |");
            sb.AppendLine($"| **Completion vs Time** | {Math.Abs(difference):F2}% {(difference < 0 ? \"behind\" : \"ahead of\")} time progress |");
        }

        sb.AppendLine($"| **Remaining Work Items** | {analysis.TotalItems - analysis.CompletedCount} |");
        if (context.EndDate.HasValue)
        {
            var daysRemaining = Math.Max(0, (context.EndDate.Value - context.GeneratedAt).Days);
            var sprintStatus = daysRemaining == 0 ? "✅ COMPLETED (Sprint has ended)" : "🔄 IN PROGRESS";
            sb.AppendLine($"| **Remaining Days** | {daysRemaining} |");
            sb.AppendLine($"| **Sprint Status** | {sprintStatus} |");
        }
        sb.AppendLine();

        return sb.ToString();
    }
}

