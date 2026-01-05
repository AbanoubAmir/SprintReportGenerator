namespace SprintReportGenerator.Reporting;

public class ReportContext
{
    public required string SprintName { get; init; }
    public required string TeamName { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.Now;
}

