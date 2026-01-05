namespace SprintReportGenerator.Models;

public class WorkItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string WorkItemType { get; set; } = string.Empty;
    public string AssignedTo { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string AreaPath { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime? CreatedDate { get; set; }
    public DateTime? ChangedDate { get; set; }
    public double? OriginalEstimate { get; set; }
    public double? CompletedWork { get; set; }
    public double? RemainingWork { get; set; }
}

