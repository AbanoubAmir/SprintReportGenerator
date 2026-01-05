namespace SprintReportGenerator.Models;

public class AnalysisResult
{
    public int TotalItems { get; set; }
    public int CompletedCount { get; set; }
    public int InProgressCount { get; set; }
    public int NotStartedCount { get; set; }
    public int BlockedCount { get; set; }

    public double CompletedPercentage => TotalItems > 0 ? (double)CompletedCount / TotalItems * 100 : 0;
    public double InProgressPercentage => TotalItems > 0 ? (double)InProgressCount / TotalItems * 100 : 0;
    public double NotStartedPercentage => TotalItems > 0 ? (double)NotStartedCount / TotalItems * 100 : 0;
    public double BlockedPercentage => TotalItems > 0 ? (double)BlockedCount / TotalItems * 100 : 0;

    public Dictionary<string, int> StateBreakdown { get; set; } = new();
    public Dictionary<string, int> TypeBreakdown { get; set; } = new();
    public Dictionary<int, int> PriorityBreakdown { get; set; } = new();
    public Dictionary<string, int> AssigneeBreakdown { get; set; } = new();

    public Dictionary<string, int> CompletedByType { get; set; } = new();
    public Dictionary<int, int> CompletedByPriority { get; set; } = new();
    public Dictionary<string, int> CompletedByAssignee { get; set; } = new();

    public List<WorkItem> UnassignedItems { get; set; } = new();
    public List<WorkItem> BlockedItems { get; set; } = new();

    public double TotalOriginalEstimate { get; set; }
    public double TotalCompletedWork { get; set; }
    public double TotalRemainingWork { get; set; }

    public List<WorkItem> WorkItems { get; set; } = new();
    public List<WorkItem> OriginalPlanItems { get; set; } = new();
    public List<WorkItem> AddedItems { get; set; } = new();
    public DateTime? SprintStartDate { get; set; }
}

