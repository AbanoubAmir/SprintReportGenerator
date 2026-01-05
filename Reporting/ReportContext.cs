using SprintReportGenerator.Models;

namespace SprintReportGenerator.Reporting;

public class ReportContext
{
    public required string SprintName { get; init; }
    public required string TeamName { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.Now;
    public IReadOnlyList<TeamCapacity> TeamCapacities { get; init; } = Array.Empty<TeamCapacity>();
    public IReadOnlyList<string> MemberFilters { get; init; } = Array.Empty<string>();
}

