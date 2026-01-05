using SprintReportGenerator.Models;

namespace SprintReportGenerator.Analysis;

public interface IWorkItemAnalyzer
{
    AnalysisResult Analyze(IEnumerable<WorkItem> workItems, DateTime? sprintStartDate);
}

