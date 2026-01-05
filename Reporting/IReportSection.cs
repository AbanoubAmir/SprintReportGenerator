using SprintReportGenerator.Models;

namespace SprintReportGenerator.Reporting;

public interface IReportSection
{
    string Title { get; }
    string Render(AnalysisResult analysis, ReportContext context);
}

