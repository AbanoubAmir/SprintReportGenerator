using System.Text;
using System.Linq;
using Microsoft.Extensions.Logging;
using SprintReportGenerator.Models;

namespace SprintReportGenerator.Reporting;

public class MarkdownReportBuilder
{
    private readonly IReadOnlyList<IReportSection> sections;
    private readonly ILogger<MarkdownReportBuilder> logger;

    public MarkdownReportBuilder(IEnumerable<IReportSection> sections, ILogger<MarkdownReportBuilder> logger)
    {
        this.sections = sections.ToList();
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int GetSectionCount() => sections.Count;

    public string Build(AnalysisResult analysis, ReportContext context)
    {
        logger.LogInformation("Building markdown report for sprint: {SprintName}", context.SprintName);
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

        logger.LogDebug("Rendering {SectionCount} report sections", sections.Count);
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            var sectionType = section.GetType().Name;
            logger.LogDebug("Rendering section {SectionIndex} of {SectionCount}: {SectionType}", i + 1, sections.Count, sectionType);
            sb.AppendLine("---");
            sb.AppendLine();
            sb.Append(section.Render(analysis, context));
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("*End of Report*");

        var report = sb.ToString();
        logger.LogInformation("Report built successfully. Report length: {ReportLength} characters", report.Length);
        return report;
    }
}

