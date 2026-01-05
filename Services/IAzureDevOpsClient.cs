using SprintReportGenerator.Models;

namespace SprintReportGenerator.Services;

public interface IAzureDevOpsClient
{
    Task<string?> GetCurrentSprintNameAsync(CancellationToken cancellationToken);
    Task<SprintData> GetSprintDataAsync(string sprintName, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkItem>> GetWorkItemsByDateRangeAsync(DateTime startDate, DateTime endDate, string excludeIterationPath, CancellationToken cancellationToken);
}

