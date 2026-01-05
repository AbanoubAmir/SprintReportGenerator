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
        var (iterationPath, startDate, endDate) = await ResolveIterationAsync(sprintName, cancellationToken);
        if (string.IsNullOrWhiteSpace(iterationPath))
        {
            return new SprintData
            {
                WorkItems = Array.Empty<WorkItem>(),
                StartDate = startDate,
                EndDate = endDate
            };
        }

        var workItemIds = await QueryWorkItemIdsAsync(iterationPath, cancellationToken);
        var workItems = await GetWorkItemDetailsAsync(workItemIds, cancellationToken);

        return new SprintData
        {
            WorkItems = workItems,
            StartDate = startDate,
            EndDate = endDate
        };
    }

    private void ConfigureHttpClient()
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{patToken}"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private async Task<(string? iterationPath, DateTime? startDate, DateTime? endDate)> ResolveIterationAsync(
        string sprintName,
        CancellationToken cancellationToken)
    {
        var iterations = await GetIterationsAsync(cancellationToken);
        if (iterations == null)
        {
            return (null, null, null);
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
            return (null, null, null);
        }

        var path = iteration["path"]?.ToString();
        var startDate = iteration["attributes"]?["startDate"]?.ToObject<DateTime?>();
        var endDate = iteration["attributes"]?["finishDate"]?.ToObject<DateTime?>();

        return (path, startDate, endDate);
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

                var workItem = new WorkItem
                {
                    Id = item?["id"]?.ToObject<int>() ?? 0,
                    Title = fields["System.Title"]?.ToString() ?? string.Empty,
                    State = fields["System.State"]?.ToString() ?? string.Empty,
                    WorkItemType = fields["System.WorkItemType"]?.ToString() ?? string.Empty,
                    AssignedTo = fields["System.AssignedTo"]?["displayName"]?.ToString() ?? "Unassigned",
                    Priority = fields["Microsoft.VSTS.Common.Priority"]?.ToObject<int?>() ?? 2,
                    AreaPath = fields["System.AreaPath"]?.ToString() ?? string.Empty,
                    Reason = fields["System.Reason"]?.ToString() ?? string.Empty,
                    CreatedDate = fields["System.CreatedDate"]?.ToObject<DateTime?>(),
                    ChangedDate = fields["System.ChangedDate"]?.ToObject<DateTime?>(),
                    OriginalEstimate = fields["Microsoft.VSTS.Scheduling.OriginalEstimate"]?.ToObject<double?>(),
                    CompletedWork = fields["Microsoft.VSTS.Scheduling.CompletedWork"]?.ToObject<double?>(),
                    RemainingWork = fields["Microsoft.VSTS.Scheduling.RemainingWork"]?.ToObject<double?>()
                };

                allWorkItems.Add(workItem);
            }
        }

        return allWorkItems;
    }
}

