namespace SprintReportGenerator.Analysis;

public static class WorkItemStatus
{
    public static bool IsCompleted(string state)
    {
        return state.Equals("Closed", StringComparison.OrdinalIgnoreCase) ||
               state.Equals("Done", StringComparison.OrdinalIgnoreCase) ||
               state.Equals("Resolved", StringComparison.OrdinalIgnoreCase);
    }
}

