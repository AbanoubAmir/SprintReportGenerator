using System.Net.Http.Headers;
using System.Text;
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

    public AzureDevOpsClient(AzureDevOpsOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "https://dev.azure.com" : options.BaseUrl;
        organization = options.Organization ?? throw new ArgumentException("Organization is required", nameof(options));
        project = options.Project ?? throw new ArgumentException("Project is required", nameof(options));
        patToken = options.PatToken ?? throw new ArgumentException("PatToken is required", nameof(options));
        teamName = options.TeamName;
        iterationPath = options.IterationPath;

        httpClient = new HttpClient();
        ConfigureHttpClient();
    }

    public async Task<string?> GetCurrentSprintNameAsync(CancellationToken cancellationToken)
    {
        var teamSegment = !string.IsNullOrEmpty(teamName)
            ? $"/{Uri.EscapeDataString(teamName)}"
            : string.Empty;

        var url =
            $"{baseUrl}/{organization}/{project}{teamSegment}/_apis/work/teamsettings/iterations?$timeframe=current&api-version=7.1";

        try
        {
            var response = await httpClient.GetStringAsync(url, cancellationToken);
            var json = JObject.Parse(response);
            var iterations = json["value"] as JArray;
            return iterations?.FirstOrDefault()?["name"]?.ToString();
        }
        catch
        {
            return null;
        }
    }

    public async Task<SprintData> GetSprintDataAsync(string sprintName, CancellationToken cancellationToken)
    {
        var iterationInfo = await ResolveIterationAsync(sprintName, cancellationToken);
        if (string.IsNullOrWhiteSpace(iterationInfo.IterationPath))
        {
            return new SprintData
            {
                WorkItems = Array.Empty<WorkItem>(),
                StartDate = iterationInfo.StartDate,
                EndDate = iterationInfo.EndDate,
                IterationId = iterationInfo.IterationId
            };
        }

        var workItemIds = await QueryWorkItemIdsAsync(iterationInfo.IterationPath!, cancellationToken);
        var workItems = await GetWorkItemDetailsAsync(workItemIds, cancellationToken);

        var capacities = string.IsNullOrWhiteSpace(iterationInfo.IterationId)
            ? Array.Empty<TeamCapacity>()
            : await GetTeamCapacitiesAsync(iterationInfo.IterationId!, iterationInfo.StartDate, iterationInfo.EndDate, cancellationToken);

        return new SprintData
        {
            WorkItems = workItems,
            StartDate = iterationInfo.StartDate,
            EndDate = iterationInfo.EndDate,
            IterationId = iterationInfo.IterationId,
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

        var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json["value"] as JArray;
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

        var response = await httpClient.PostAsync(wiqlUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        var wiqlJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var wiqlResult = JObject.Parse(wiqlJson);
        var workItemRefs = wiqlResult["workItems"] as JArray;

        var workItemIds = workItemRefs?
            .Select(w => w?["id"]?.ToObject<int>() ?? 0)
            .Where(id => id > 0)
            .ToList() ?? new List<int>();

        return workItemIds;
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

        var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var capacities = json["value"] as JArray;
        if (capacities == null)
        {
            return Array.Empty<TeamCapacity>();
        }

        var workingDays = GetWorkingDays(startDate, endDate);

        var entries = new List<TeamCapacity>();

        foreach (var capacity in capacities)
        {
            var member = capacity?["teamMember"];
            var displayName = member?["displayName"]?.ToString() ?? "Unknown";
            var daysOffArray = capacity?["daysOff"] as JArray;
            var daysOff = daysOffArray?.Count ?? 0;

            var activities = capacity?["activities"] as JArray;
            if (activities == null || activities.Count == 0)
            {
                entries.Add(new TeamCapacity
                {
                    DisplayName = displayName,
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

        foreach (var batch in batches)
        {
            var ids = string.Join(",", batch);
            var url =
                $"{baseUrl}/{organization}/{project}/_apis/wit/workitems?ids={ids}&$expand=all&api-version=7.1";

            var response = await httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var items = json["value"] as JArray;

            foreach (var item in items ?? new JArray())
            {
                var fields = item?["fields"] as JObject;
                if (fields == null)
                {
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
                    ActivatedBy = fields["Microsoft.VSTS.Common.ActivatedBy"]?["displayName"]?.ToString(),
                    ResolvedBy = fields["Microsoft.VSTS.Common.ResolvedBy"]?["displayName"]?.ToString(),
                    ClosedBy = fields["Microsoft.VSTS.Common.ClosedBy"]?["displayName"]?.ToString(),
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

                allWorkItems.Add(workItem);
            }
        }

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

