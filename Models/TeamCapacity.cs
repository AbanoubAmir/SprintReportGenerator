namespace SprintReportGenerator.Models;

public class TeamCapacity
{
    public string DisplayName { get; init; } = string.Empty;
    public string? Activity { get; init; }
    public double TotalCapacityHours { get; init; }
    public int DaysOff { get; init; }
}

