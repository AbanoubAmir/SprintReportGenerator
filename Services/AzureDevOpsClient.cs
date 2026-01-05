using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SprintReportGenerator.Models;
using System.Linq;

namespace SprintReportGenerator.Services;

public class AzureDevOpsClient : IAzureDevOpsClient
{
    private readonly HttpClient httpClient;
    private readonly string baseUrl;
    private readonly string organization;
    private readonly string project;
    private readonly string patToken;
    private readonly string? teamName;
    private readonly string? iterationPath;
    private readonly ILogger<AzureDevOpsClient> logger;

    public AzureDevOpsClient(AzureDevOpsOptions options, ILogger<AzureDevOpsClient> logger)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "https://dev.azure.com" : options.BaseUrl;
        organization = options.Organization ?? throw new ArgumentException("Organization is required", nameof(options));
        project = options.Project ?? throw new ArgumentException("Project is required", nameof(options));
        patToken = options.PatToken ?? throw new ArgumentException("PatToken is required", nameof(options));
        teamName = options.TeamName;
        iterationPath = options.IterationPath;

        httpClient = new HttpClient();
        ConfigureHttpClient();
        logger.LogInformation("Azure DevOps client initialized. BaseUrl: {BaseUrl}, Organization: {Organization}, Project: {Project}", baseUrl, organization, project);
    }

    public async Task<string?> GetCurrentSprintNameAsync(CancellationToken cancellationToken)
    {
        var teamSegment = !string.IsNullOrEmpty(teamName)
            ? $"/{Uri.EscapeDataString(teamName)}"
            : string.Empty;

        var url =
            $"{baseUrl}/{organization}/{project}{teamSegment}/_apis/work/teamsettings/iterations?$timeframe=current&api-version=7.1";

        logger.LogInformation("Fetching current sprint name from Azure DevOps. Team: {TeamName}", teamName ?? "default");
        try
        {
            var response = await httpClient.GetStringAsync(url, cancellationToken);
            var json = JObject.Parse(response);
            var iterations = json["value"] as JArray;
            var sprintName = iterations?.FirstOrDefault()?["name"]?.ToString();
            if (!string.IsNullOrWhiteSpace(sprintName))
            {
                logger.LogInformation("Current sprint resolved: {SprintName}", sprintName);
            }
            else
            {
                logger.LogWarning("No current sprint found in response");
            }
            return sprintName;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching current sprint name from Azure DevOps");
            return null;
        }
    }

    public async Task<SprintData> GetSprintDataAsync(string sprintName, CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching sprint data for sprint: {SprintName}", sprintName);
        var iterationInfo = await ResolveIterationAsync(sprintName, cancellationToken);
        if (string.IsNullOrWhiteSpace(iterationInfo.IterationPath))
        {
            logger.LogWarning("Iteration path not found for sprint: {SprintName}", sprintName);
            return new SprintData
            {
                WorkItems = Array.Empty<WorkItem>(),
                StartDate = iterationInfo.StartDate,
                EndDate = iterationInfo.EndDate,
                IterationId = iterationInfo.IterationId,
                IterationPath = null,
                IterationWorkItemIds = Array.Empty<int>()
            };
        }

        logger.LogInformation("Resolved iteration. Path: {IterationPath}, StartDate: {StartDate}, EndDate: {EndDate}",
            iterationInfo.IterationPath, iterationInfo.StartDate, iterationInfo.EndDate);

        logger.LogInformation("Querying work item IDs for iteration");
        var iterationWorkItemIdsTask = QueryWorkItemIdsAsync(iterationInfo.IterationPath!, cancellationToken);
        
        var capacities = string.IsNullOrWhiteSpace(iterationInfo.IterationId)
            ? Array.Empty<TeamCapacity>()
            : await GetTeamCapacitiesAsync(iterationInfo.IterationId!, iterationInfo.StartDate, iterationInfo.EndDate, cancellationToken);

        logger.LogInformation("Retrieved {CapacityCount} team capacity entries", capacities.Count);

        var allTeamIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var capacity in capacities)
        {
            var displayName = capacity.DisplayName?.Trim() ?? string.Empty;
            var uniqueName = capacity.UniqueName?.Trim() ?? string.Empty;
            
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                if (displayName.Contains('<') && displayName.Contains('>'))
                {
                    var parts = displayName.Split(new[] { '<', '>' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 1)
                    {
                        allTeamIdentifiers.Add(parts[0].Trim());
                    }
                    if (parts.Length >= 2)
                    {
                        allTeamIdentifiers.Add(parts[1].Trim());
                    }
                }
                else
                {
                    allTeamIdentifiers.Add(displayName);
                }
            }
            
            if (!string.IsNullOrWhiteSpace(uniqueName))
            {
                allTeamIdentifiers.Add(uniqueName);
            }
        }

        logger.LogInformation("Identified {TeamMemberCount} team member identifiers for filtering (display names and unique names)", 
            allTeamIdentifiers.Count);

        Task<IReadOnlyList<WorkItem>>? crossIterationWorkItemsTask = null;
        if (iterationInfo.StartDate.HasValue && iterationInfo.EndDate.HasValue)
        {
            logger.LogInformation("Querying cross-iteration work items changed during sprint period (running in parallel)");
            crossIterationWorkItemsTask = GetWorkItemsByDateRangeAsync(
                iterationInfo.StartDate.Value, 
                iterationInfo.EndDate.Value, 
                iterationInfo.IterationPath!,
                allTeamIdentifiers,
                cancellationToken);
        }

        var iterationWorkItemIds = await iterationWorkItemIdsTask;
        logger.LogInformation("Found {WorkItemCount} work item IDs in iteration", iterationWorkItemIds.Count);

        var allWorkItemIds = new HashSet<int>(iterationWorkItemIds);

        if (crossIterationWorkItemsTask != null)
        {
            var crossIterationWorkItems = await crossIterationWorkItemsTask;
            var crossIterationIds = crossIterationWorkItems.Select(w => w.Id).ToHashSet();
            logger.LogInformation("Found {CrossIterationCount} cross-iteration work items changed during sprint period by team members", crossIterationIds.Count);
            
            allWorkItemIds.UnionWith(crossIterationIds);
            logger.LogInformation("Total unique work items: {TotalCount} (iteration: {IterationCount}, cross-iteration: {CrossIterationCount})",
                allWorkItemIds.Count, iterationWorkItemIds.Count, crossIterationIds.Count);
        }

        logger.LogInformation("Fetching work item details in {BatchCount} batch(es)", (allWorkItemIds.Count + 199) / 200);
        var workItems = await GetWorkItemDetailsAsync(allWorkItemIds.ToList(), cancellationToken);
        logger.LogInformation("Retrieved {WorkItemCount} work items", workItems.Count);

        return new SprintData
        {
            WorkItems = workItems,
            StartDate = iterationInfo.StartDate,
            EndDate = iterationInfo.EndDate,
            IterationId = iterationInfo.IterationId,
            IterationPath = iterationInfo.IterationPath,
            IterationWorkItemIds = iterationWorkItemIds,
            TeamCapacities = capacities
        };
    }

    private void ConfigureHttpClient()
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{patToken}"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private async Task<(string? IterationPath, string? IterationId, DateTime? StartDate, DateTime? EndDate)> ResolveIterationAsync(
        string sprintName,
        CancellationToken cancellationToken)
    {
        var iterations = await GetIterationsAsync(cancellationToken);
        if (iterations == null)
        {
            return (null, null, null, null);
        }

        JObject? iteration;
        if (!string.IsNullOrEmpty(iterationPath))
        {
            iteration = iterations.FirstOrDefault(i =>
                i["path"]?.ToString().Equals(iterationPath, StringComparison.OrdinalIgnoreCase) == true) as JObject;
        }
        else
        {
            iteration = iterations.FirstOrDefault(i =>
                i["name"]?.ToString().Equals(sprintName, StringComparison.OrdinalIgnoreCase) == true) as JObject;
        }

        if (iteration == null)
        {
            return (null, null, null, null);
        }

        var path = iteration["path"]?.ToString();
        var id = iteration["id"]?.ToString();
        var startDate = iteration["attributes"]?["startDate"]?.ToObject<DateTime?>();
        var endDate = iteration["attributes"]?["finishDate"]?.ToObject<DateTime?>();

        return (path, id, startDate, endDate);
    }

    private async Task<JArray?> GetIterationsAsync(CancellationToken cancellationToken)
    {
        var teamSegment = !string.IsNullOrEmpty(teamName)
            ? $"/{Uri.EscapeDataString(teamName)}"
            : string.Empty;
        var url =
            $"{baseUrl}/{organization}/{project}{teamSegment}/_apis/work/teamsettings/iterations?api-version=7.1";

        logger.LogDebug("Fetching iterations from Azure DevOps");
        var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var iterations = json["value"] as JArray;
        logger.LogDebug("Retrieved {IterationCount} iterations", iterations?.Count ?? 0);
        return iterations;
    }

    private async Task<IReadOnlyList<int>> QueryWorkItemIdsAsync(string iterationPath, CancellationToken cancellationToken)
    {
        var wiql = $@"
SELECT [System.Id] 
FROM WorkItems 
WHERE [System.TeamProject] = @project 
  AND [System.IterationPath] = '{iterationPath}'
ORDER BY [System.Id]";

        var wiqlUrl = $"{baseUrl}/{organization}/{project}/_apis/wit/wiql?api-version=7.1";
        var wiqlBody = new { query = wiql };
        var content = new StringContent(JsonConvert.SerializeObject(wiqlBody), Encoding.UTF8, "application/json");

        logger.LogDebug("Executing WIQL query for iteration path: {IterationPath}", iterationPath);
        var response = await httpClient.PostAsync(wiqlUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        var wiqlJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var wiqlResult = JObject.Parse(wiqlJson);
        var workItemRefs = wiqlResult["workItems"] as JArray;

        var workItemIds = workItemRefs?
            .Select(w => w?["id"]?.ToObject<int>() ?? 0)
            .Where(id => id > 0)
            .ToList() ?? new List<int>();

        logger.LogDebug("WIQL query returned {WorkItemIdCount} work item IDs", workItemIds.Count);
        return workItemIds;
    }

    public async Task<IReadOnlyList<WorkItem>> GetWorkItemsByDateRangeAsync(DateTime startDate, DateTime endDate, string excludeIterationPath, HashSet<string> teamMemberNames, CancellationToken cancellationToken)
    {
        var startDateStr = startDate.ToString("yyyy-MM-dd");
        var endDateStr = endDate.AddDays(1).ToString("yyyy-MM-dd");
        var escapedIterationPath = excludeIterationPath.Replace("'", "''");

        var wiql = $@"
SELECT [System.Id] 
FROM WorkItems 
WHERE [System.TeamProject] = @project 
  AND [System.ChangedDate] >= '{startDateStr}'
  AND [System.ChangedDate] < '{endDateStr}'
  AND [System.IterationPath] <> '{escapedIterationPath}'
ORDER BY [System.Id]";

        var wiqlUrl = $"{baseUrl}/{organization}/{project}/_apis/wit/wiql?api-version=7.1";
        var wiqlBody = new { query = wiql };
        var content = new StringContent(JsonConvert.SerializeObject(wiqlBody), Encoding.UTF8, "application/json");

        logger.LogDebug("Executing optimized WIQL query for cross-iteration work items changed between {StartDate} and {EndDate} (excluding iteration: {IterationPath})", 
            startDate, endDate, excludeIterationPath);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await httpClient.PostAsync(wiqlUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        var wiqlJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var wiqlResult = JObject.Parse(wiqlJson);
        stopwatch.Stop();
        
        var workItemRefs = wiqlResult["workItems"] as JArray;
        var workItemIds = workItemRefs?
            .Select(w => w?["id"]?.ToObject<int>() ?? 0)
            .Where(id => id > 0)
            .ToList() ?? new List<int>();

        var continuationToken = wiqlResult["continuationToken"]?.ToString();
        if (!string.IsNullOrWhiteSpace(continuationToken))
        {
            logger.LogInformation("WIQL query returned continuation token, fetching additional pages");
            var allWorkItemIds = new List<int>(workItemIds);
            
            while (!string.IsNullOrWhiteSpace(continuationToken))
            {
                var continuationUrl = $"{wiqlUrl}&$continuationToken={Uri.EscapeDataString(continuationToken)}";
                var continuationResponse = await httpClient.PostAsync(continuationUrl, content, cancellationToken);
                continuationResponse.EnsureSuccessStatusCode();
                var continuationJson = await continuationResponse.Content.ReadAsStringAsync(cancellationToken);
                var continuationResult = JObject.Parse(continuationJson);
                
                var continuationRefs = continuationResult["workItems"] as JArray;
                var continuationIds = continuationRefs?
                    .Select(w => w?["id"]?.ToObject<int>() ?? 0)
                    .Where(id => id > 0)
                    .ToList() ?? new List<int>();
                
                allWorkItemIds.AddRange(continuationIds);
                continuationToken = continuationResult["continuationToken"]?.ToString();
                logger.LogDebug("Fetched additional page: {ItemCount} items, continuation token: {HasToken}", 
                    continuationIds.Count, !string.IsNullOrWhiteSpace(continuationToken));
            }
            
            workItemIds = allWorkItemIds;
        }

        logger.LogInformation("WIQL query completed in {ElapsedMs}ms, returned {WorkItemIdCount} work item IDs", 
            stopwatch.ElapsedMilliseconds, workItemIds.Count);

        if (workItemIds.Count == 0)
        {
            return Array.Empty<WorkItem>();
        }

        logger.LogDebug("Fetching details for {WorkItemCount} cross-iteration work items", workItemIds.Count);
        stopwatch.Restart();
        var allWorkItems = await GetWorkItemDetailsAsync(workItemIds, cancellationToken);
        stopwatch.Stop();
        logger.LogInformation("Retrieved {WorkItemCount} cross-iteration work items in {ElapsedMs}ms", 
            allWorkItems.Count, stopwatch.ElapsedMilliseconds);

        if (teamMemberNames.Count == 0)
        {
            logger.LogWarning("No team members identified, returning all cross-iteration work items");
            return allWorkItems;
        }

        logger.LogInformation("Pre-filtering cross-iteration work items by team member association and effort indicators");
        
        var sprintStartDate = startDate;
        var sprintEndDate = endDate.AddDays(1);
        
        var preFilteredItems = allWorkItems.Where(w =>
        {
            var assignedTo = NormalizeName(w.AssignedTo);
            var assignedToUnique = NormalizeName(w.AssignedToUniqueName);
            var resolvedBy = NormalizeName(w.ResolvedBy);
            var resolvedByUnique = NormalizeName(w.ResolvedByUniqueName);
            var closedBy = NormalizeName(w.ClosedBy);
            var closedByUnique = NormalizeName(w.ClosedByUniqueName);
            var activatedBy = NormalizeName(w.ActivatedBy);
            var activatedByUnique = NormalizeName(w.ActivatedByUniqueName);

            var isAssociatedWithTeam = teamMemberNames.Contains(assignedTo) ||
                                      teamMemberNames.Contains(assignedToUnique) ||
                                      teamMemberNames.Contains(resolvedBy) ||
                                      teamMemberNames.Contains(resolvedByUnique) ||
                                      teamMemberNames.Contains(closedBy) ||
                                      teamMemberNames.Contains(closedByUnique) ||
                                      teamMemberNames.Contains(activatedBy) ||
                                      teamMemberNames.Contains(activatedByUnique);

            if (!isAssociatedWithTeam)
            {
                return false;
            }
            
            var hasEffort = (w.CompletedWork.HasValue && w.CompletedWork.Value > 0) ||
                           (w.RemainingWork.HasValue && w.RemainingWork.Value > 0) ||
                           (w.OriginalEstimate.HasValue && w.OriginalEstimate.Value > 0);
            
            var wasClosedDuringSprint = w.ClosedDate.HasValue &&
                                       w.ClosedDate.Value >= sprintStartDate &&
                                       w.ClosedDate.Value < sprintEndDate &&
                                       (teamMemberNames.Contains(closedBy) || teamMemberNames.Contains(closedByUnique));
            
            var wasResolvedDuringSprint = !string.IsNullOrWhiteSpace(w.ResolvedBy) &&
                                         (teamMemberNames.Contains(resolvedBy) || teamMemberNames.Contains(resolvedByUnique));
            
            var wasActivatedDuringSprint = !string.IsNullOrWhiteSpace(w.ActivatedBy) &&
                                          (teamMemberNames.Contains(activatedBy) || teamMemberNames.Contains(activatedByUnique));
            
            var wasAssignedToTeamMember = (teamMemberNames.Contains(assignedTo) || teamMemberNames.Contains(assignedToUnique)) &&
                                         w.ChangedDate.HasValue &&
                                         w.ChangedDate.Value >= sprintStartDate &&
                                         w.ChangedDate.Value < sprintEndDate;
            
            if (hasEffort || wasClosedDuringSprint || wasResolvedDuringSprint || wasActivatedDuringSprint || wasAssignedToTeamMember)
            {
                return true;
            }
            
            return false;
        }).ToList();

        logger.LogInformation("Pre-filtered from {TotalCount} to {PreFilteredCount} items associated with team members", 
            allWorkItems.Count, preFilteredItems.Count);

        if (preFilteredItems.Count == 0)
        {
            return Array.Empty<WorkItem>();
        }

        logger.LogInformation("Checking revisions to identify items with actual work by team members during sprint period");
        
        const int maxConcurrency = 10;
        var semaphore = new SemaphoreSlim(maxConcurrency);
        var teamWorkItems = new List<WorkItem>();
        
        var tasks = preFilteredItems.Select(async workItem =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var hasActualWork = await HasActualWorkByTeamMembersAsync(
                    workItem.Id, 
                    sprintStartDate, 
                    sprintEndDate, 
                    teamMemberNames, 
                    cancellationToken);
                
                if (hasActualWork)
                {
                    lock (teamWorkItems)
                    {
                        teamWorkItems.Add(workItem);
                    }
                }
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);

        logger.LogInformation("Filtered from {PreFilteredCount} to {TeamCount} cross-iteration work items with actual work by team members during sprint period", 
            preFilteredItems.Count, teamWorkItems.Count);
        
        return teamWorkItems;
    }

    private async Task<bool> HasActualWorkByTeamMembersAsync(
        int workItemId,
        DateTime sprintStartDate,
        DateTime sprintEndDate,
        HashSet<string> teamMemberNames,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{baseUrl}/{organization}/{project}/_apis/wit/workitems/{workItemId}/updates?api-version=7.1";
            var response = await httpClient.GetAsync(url, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Could not fetch revisions for work item {WorkItemId}, status: {StatusCode}", workItemId, response.StatusCode);
                return false;
            }
            
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var updates = JObject.Parse(json);
            var value = updates["value"] as JArray;
            
            if (value == null || value.Count == 0)
            {
                return false;
            }
            
            foreach (var update in value)
            {
                var revisedDate = update?["revisedDate"]?.ToObject<DateTime?>();
                if (!revisedDate.HasValue)
                {
                    continue;
                }
                
                if (revisedDate.Value < sprintStartDate || revisedDate.Value >= sprintEndDate)
                {
                    continue;
                }
                
                var revisedBy = update?["revisedBy"]?["displayName"]?.ToString();
                var revisedByUnique = update?["revisedBy"]?["uniqueName"]?.ToString();
                
                var revisedByNormalized = NormalizeName(revisedBy);
                var revisedByUniqueNormalized = NormalizeName(revisedByUnique);
                
                if (!teamMemberNames.Contains(revisedByNormalized) && 
                    !teamMemberNames.Contains(revisedByUniqueNormalized))
                {
                    continue;
                }
                
                var fields = update?["fields"] as JObject;
                if (fields == null)
                {
                    continue;
                }
                
                foreach (var field in fields)
                {
                    var fieldName = field.Key;
                    var fieldValue = field.Value;
                    
                    if (fieldValue == null)
                    {
                        continue;
                    }
                    
                    var newValue = fieldValue["newValue"];
                    var oldValue = fieldValue["oldValue"];
                    
                    if (fieldName.Equals("Microsoft.VSTS.Scheduling.CompletedWork", StringComparison.OrdinalIgnoreCase) ||
                        fieldName.Equals("Microsoft.VSTS.Scheduling.RemainingWork", StringComparison.OrdinalIgnoreCase) ||
                        fieldName.Equals("Microsoft.VSTS.Scheduling.OriginalEstimate", StringComparison.OrdinalIgnoreCase))
                    {
                        var newWorkValue = newValue?.ToObject<double?>();
                        var oldWorkValue = oldValue?.ToObject<double?>();
                        
                        if (newWorkValue.HasValue && oldWorkValue.HasValue && newWorkValue.Value != oldWorkValue.Value)
                        {
                            logger.LogDebug("Work item {WorkItemId} has effort change by team member {Member} during sprint", 
                                workItemId, revisedBy ?? revisedByUnique);
                            return true;
                        }
                    }
                    
                    if (fieldName.Equals("System.State", StringComparison.OrdinalIgnoreCase))
                    {
                        var newState = newValue?.ToString();
                        var oldState = oldValue?.ToString();
                        
                        if (!string.IsNullOrWhiteSpace(newState) && 
                            !string.IsNullOrWhiteSpace(oldState) && 
                            !newState.Equals(oldState, StringComparison.OrdinalIgnoreCase))
                        {
                            var meaningfulStates = new[] { "Active", "In Progress", "Resolved", "Closed", "Done" };
                            if (meaningfulStates.Any(s => newState.Equals(s, StringComparison.OrdinalIgnoreCase)) ||
                                meaningfulStates.Any(s => oldState.Equals(s, StringComparison.OrdinalIgnoreCase)))
                            {
                                logger.LogDebug("Work item {WorkItemId} has state change by team member {Member} during sprint: {OldState} -> {NewState}", 
                                    workItemId, revisedBy ?? revisedByUnique, oldState, newState);
                                return true;
                            }
                        }
                    }
                    
                    if (fieldName.Equals("System.AssignedTo", StringComparison.OrdinalIgnoreCase))
                    {
                        var newAssignee = newValue?["displayName"]?.ToString();
                        var newAssigneeUnique = newValue?["uniqueName"]?.ToString();
                        var oldAssignee = oldValue?["displayName"]?.ToString();
                        var oldAssigneeUnique = oldValue?["uniqueName"]?.ToString();
                        
                        var newAssigneeNormalized = NormalizeName(newAssignee);
                        var newAssigneeUniqueNormalized = NormalizeName(newAssigneeUnique);
                        
                        if (teamMemberNames.Contains(newAssigneeNormalized) || 
                            teamMemberNames.Contains(newAssigneeUniqueNormalized))
                        {
                            if (!string.IsNullOrWhiteSpace(newAssignee) && 
                                (string.IsNullOrWhiteSpace(oldAssignee) || 
                                 !newAssignee.Equals(oldAssignee, StringComparison.OrdinalIgnoreCase)))
                            {
                                logger.LogDebug("Work item {WorkItemId} was assigned to team member {Member} during sprint", 
                                    workItemId, newAssignee ?? newAssigneeUnique);
                                return true;
                            }
                        }
                    }
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error checking revisions for work item {WorkItemId}", workItemId);
            return false;
        }
    }

    private static string NormalizeName(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private async Task<IReadOnlyList<TeamCapacity>> GetTeamCapacitiesAsync(
        string iterationId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var teamSegment = !string.IsNullOrEmpty(teamName)
            ? $"/{Uri.EscapeDataString(teamName)}"
            : string.Empty;

        var url =
            $"{baseUrl}/{organization}/{project}{teamSegment}/_apis/work/teamsettings/iterations/{iterationId}/capacities?api-version=7.1-preview.1";

        logger.LogDebug("Fetching team capacities for iteration: {IterationId}", iterationId);
        var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var capacities = json["value"] as JArray;
        if (capacities == null)
        {
            logger.LogDebug("No team capacities found for iteration: {IterationId}", iterationId);
            return Array.Empty<TeamCapacity>();
        }

        var workingDays = GetWorkingDays(startDate, endDate);

        var entries = new List<TeamCapacity>();

        foreach (var capacity in capacities)
        {
            var member = capacity?["teamMember"];
            var displayName = member?["displayName"]?.ToString() ?? "Unknown";
            var uniqueName = member?["uniqueName"]?.ToString();
            var daysOffArray = capacity?["daysOff"] as JArray;
            var daysOff = daysOffArray?.Count ?? 0;

            var activities = capacity?["activities"] as JArray;
            if (activities == null || activities.Count == 0)
            {
                entries.Add(new TeamCapacity
                {
                    DisplayName = displayName,
                    UniqueName = uniqueName,
                    Activity = null,
                    TotalCapacityHours = 0,
                    DaysOff = daysOff
                });
                continue;
            }

            foreach (var act in activities)
            {
                var activityName = act?["name"]?.ToString() ?? "Unspecified";
                var capacityPerDay = act?["capacityPerDay"]?.ToObject<double?>() ?? 0;

                entries.Add(new TeamCapacity
                {
                    DisplayName = displayName,
                    UniqueName = uniqueName,
                    Activity = activityName,
                    TotalCapacityHours = capacityPerDay,
                    DaysOff = daysOff
                });
            }
        }

        // Normalize capacity to hours across the sprint
        var normalized = entries.Select(e => new TeamCapacity
        {
            DisplayName = e.DisplayName,
            Activity = e.Activity,
            TotalCapacityHours = workingDays > 0
                ? e.TotalCapacityHours * Math.Max(0, workingDays - e.DaysOff)
                : e.TotalCapacityHours,
            DaysOff = e.DaysOff
        }).ToList();

        return normalized;
    }

    private static int GetWorkingDays(DateTime? start, DateTime? end)
    {
        if (!start.HasValue || !end.HasValue)
        {
            return 0;
        }

        var days = 0;
        for (var date = start.Value.Date; date <= end.Value.Date; date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }
            days++;
        }

        return days;
    }
    private async Task<IReadOnlyList<WorkItem>> GetWorkItemDetailsAsync(
        IReadOnlyList<int> workItemIds,
        CancellationToken cancellationToken)
    {
        if (workItemIds.Count == 0)
        {
            return Array.Empty<WorkItem>();
        }

        var batches = workItemIds.Chunk(200).ToList();
        var allWorkItems = new List<WorkItem>();

        logger.LogDebug("Fetching work item details in {BatchCount} batch(es) using parallel processing", batches.Count);
        
        const int maxConcurrency = 5;
        var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = batches.Select(async (batch, batchIndex) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var ids = string.Join(",", batch);
                var url =
                    $"{baseUrl}/{organization}/{project}/_apis/wit/workitems?ids={ids}&$expand=all&api-version=7.1";

                logger.LogDebug("Fetching batch {BatchIndex} of {BatchCount} ({BatchSize} items)", batchIndex + 1, batches.Count, batch.Length);
                var response = await httpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();
                var json = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var items = json["value"] as JArray;

                var batchWorkItems = new List<WorkItem>();
                foreach (var item in items ?? new JArray())
                {
                    var fields = item?["fields"] as JObject;
                    if (fields == null)
                    {
                        logger.LogWarning("Work item {WorkItemId} has no fields, skipping", item?["id"]?.ToObject<int>());
                        continue;
                    }

                    var parentId = ResolveParentId(item);

                    var workItem = new WorkItem
                    {
                        Id = item?["id"]?.ToObject<int>() ?? 0,
                        Title = fields["System.Title"]?.ToString() ?? string.Empty,
                        State = fields["System.State"]?.ToString() ?? string.Empty,
                        WorkItemType = fields["System.WorkItemType"]?.ToString() ?? string.Empty,
                        AssignedTo = fields["System.AssignedTo"]?["displayName"]?.ToString() ?? "Unassigned",
                        AssignedToUniqueName = fields["System.AssignedTo"]?["uniqueName"]?.ToString(),
                        ActivatedBy = fields["Microsoft.VSTS.Common.ActivatedBy"]?["displayName"]?.ToString(),
                        ActivatedByUniqueName = fields["Microsoft.VSTS.Common.ActivatedBy"]?["uniqueName"]?.ToString(),
                        ResolvedBy = fields["Microsoft.VSTS.Common.ResolvedBy"]?["displayName"]?.ToString(),
                        ResolvedByUniqueName = fields["Microsoft.VSTS.Common.ResolvedBy"]?["uniqueName"]?.ToString(),
                        ClosedBy = fields["Microsoft.VSTS.Common.ClosedBy"]?["displayName"]?.ToString(),
                        ClosedByUniqueName = fields["Microsoft.VSTS.Common.ClosedBy"]?["uniqueName"]?.ToString(),
                        Priority = fields["Microsoft.VSTS.Common.Priority"]?.ToObject<int?>() ?? 2,
                        AreaPath = fields["System.AreaPath"]?.ToString() ?? string.Empty,
                        Reason = fields["System.Reason"]?.ToString() ?? string.Empty,
                        CreatedDate = fields["System.CreatedDate"]?.ToObject<DateTime?>(),
                        ChangedDate = fields["System.ChangedDate"]?.ToObject<DateTime?>(),
                        ClosedDate = fields["Microsoft.VSTS.Common.ClosedDate"]?.ToObject<DateTime?>(),
                        OriginalEstimate = fields["Microsoft.VSTS.Scheduling.OriginalEstimate"]?.ToObject<double?>(),
                        CompletedWork = fields["Microsoft.VSTS.Scheduling.CompletedWork"]?.ToObject<double?>(),
                        RemainingWork = fields["Microsoft.VSTS.Scheduling.RemainingWork"]?.ToObject<double?>(),
                        ParentId = parentId
                    };

                    batchWorkItems.Add(workItem);
                }
                logger.LogDebug("Processed batch {BatchIndex} of {BatchCount} ({BatchSize} items)", batchIndex + 1, batches.Count, batchWorkItems.Count);
                return batchWorkItems;
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        var results = await Task.WhenAll(tasks);
        allWorkItems.AddRange(results.SelectMany(r => r));

        logger.LogDebug("Completed fetching all work item details. Total items: {TotalItems}", allWorkItems.Count);
        return allWorkItems;
    }

    private static int? ResolveParentId(JToken? item)
    {
        var relations = item?["relations"] as JArray;
        var parentRelation = relations?
            .FirstOrDefault(r => r?["rel"]?.ToString().Equals("System.LinkTypes.Hierarchy-Reverse", StringComparison.OrdinalIgnoreCase) == true);

        var url = parentRelation?["url"]?.ToString();
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var idSegment = url.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        if (int.TryParse(idSegment, out var id))
        {
            return id;
        }

        return null;
    }
}

