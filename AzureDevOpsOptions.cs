namespace SprintReportGenerator;

/// <summary>
/// Azure DevOps connection and sprint selection settings.
/// </summary>
public class AzureDevOpsOptions
{
    public string BaseUrl { get; set; } = "https://dev.azure.com";
    public string? Organization { get; set; }
    public string? Project { get; set; }
    public string? PatToken { get; set; }
    public string? TeamName { get; set; }
    public string? SprintName { get; set; }
    public string? IterationPath { get; set; }
    public IReadOnlyList<string> MemberFilters { get; set; } = Array.Empty<string>();
}

