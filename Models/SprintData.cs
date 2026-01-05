namespace SprintReportGenerator.Models;

public class SprintData
{
    public IReadOnlyList<WorkItem> WorkItems { get; init; } = Array.Empty<WorkItem>();
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public string? IterationId { get; init; }
    public IReadOnlyList<TeamCapacity> TeamCapacities { get; init; } = Array.Empty<TeamCapacity>();
}

