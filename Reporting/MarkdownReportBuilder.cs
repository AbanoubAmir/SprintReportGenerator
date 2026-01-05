using System.Text;
using System.Linq;
using SprintReportGenerator.Models;

namespace SprintReportGenerator.Reporting;

public class MarkdownReportBuilder
{
    private readonly IReadOnlyList<IReportSection> sections;

    public MarkdownReportBuilder(IEnumerable<IReportSection> sections)
    {
        this.sections = sections.ToList();
    }

    public string Build(AnalysisResult analysis, ReportContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Sprint Analysis Report: {context.SprintName}");
        sb.AppendLine();
        sb.AppendLine($"**Team:** {context.TeamName}  ");
        sb.AppendLine($"**Generated:** {context.GeneratedAt:yyyy-MM-dd HH:mm:ss}  ");

        if (context.StartDate.HasValue && context.EndDate.HasValue)
        {
            var now = context.GeneratedAt;
            var daysElapsed = (now - context.StartDate.Value).Days;
            var daysRemaining = Math.Max(0, (context.EndDate.Value - now).Days);
            sb.AppendLine($"**Sprint Period:** {context.StartDate.Value:yyyy-MM-dd} to {context.EndDate.Value:yyyy-MM-dd}  ");
            sb.AppendLine($"**Days Elapsed:** {daysElapsed}  ");
            sb.AppendLine($"**Days Remaining:** {daysRemaining}  ");
        }

        sb.AppendLine();

        foreach (var section in sections)
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.Append(section.Render(analysis, context));
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("*End of Report*");

        return sb.ToString();
    }
}

