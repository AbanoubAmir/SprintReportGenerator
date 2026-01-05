namespace SprintReportGenerator.Models;

public class SprintData
{
    public IReadOnlyList<WorkItem> WorkItems { get; init; } = Array.Empty<WorkItem>();
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
}

