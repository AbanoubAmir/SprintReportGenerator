using System.Text;
using Microsoft.Extensions.Configuration;
using SprintReportGenerator.Analysis;
using SprintReportGenerator.Reporting;
using SprintReportGenerator.Reporting.Sections;
using SprintReportGenerator.Services;

namespace SprintReportGenerator;

class Program
{
    static async Task Main(string[] args)
    {
        var options = LoadOptions();
        if (!HasRequiredOptions(options))
        {
            Console.WriteLine("Error: Missing required configuration. Please check appsettings.json");
            return;
        }

        var client = new AzureDevOpsClient(options);
        var sprintName = await ResolveSprintNameAsync(args, options, client);

        if (string.IsNullOrWhiteSpace(sprintName))
        {
            Console.WriteLine("Error: Could not determine sprint name. Please configure in appsettings.json, provide as argument, or ensure current iteration is set.");
            return;
        }

        Console.WriteLine($"Generating report for sprint: {sprintName}");
        Console.WriteLine("Fetching work items...");

        var sprintData = await client.GetSprintDataAsync(sprintName, CancellationToken.None);

        Console.WriteLine("Analyzing data...");
        var analyzer = new WorkItemAnalyzer();
        var analysis = analyzer.Analyze(sprintData.WorkItems, sprintData.StartDate);

        var builder = BuildReportBuilder();
        var context = new ReportContext
        {
            SprintName = sprintName,
            TeamName = options.TeamName ?? "Team",
            StartDate = sprintData.StartDate,
            EndDate = sprintData.EndDate,
            GeneratedAt = DateTime.Now,
            TeamCapacities = sprintData.TeamCapacities
        };

        var report = builder.Build(analysis, context);
        var outputFile = $"Sprint_Complete_Analysis_{DateTime.Now:MMM_d_yyyy}.md";
        await File.WriteAllTextAsync(outputFile, report, Encoding.UTF8);

        Console.WriteLine($"Report generated: {outputFile}");
    }

    private static AzureDevOpsOptions LoadOptions()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.local.json", optional: true)
            .Build();

        return new AzureDevOpsOptions
        {
            BaseUrl = config["AzureDevOps:BaseUrl"] ?? "https://dev.azure.com",
            Organization = config["AzureDevOps:Organization"],
            Project = config["AzureDevOps:Project"],
            PatToken = config["AzureDevOps:PatToken"],
            TeamName = config["AzureDevOps:TeamName"],
            SprintName = config["AzureDevOps:SprintName"],
            IterationPath = config["AzureDevOps:IterationPath"]
        };
    }

    private static bool HasRequiredOptions(AzureDevOpsOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.Organization)
               && !string.IsNullOrWhiteSpace(options.Project)
               && !string.IsNullOrWhiteSpace(options.PatToken);
    }

    private static async Task<string?> ResolveSprintNameAsync(
        string[] args,
        AzureDevOpsOptions options,
        IAzureDevOpsClient client)
    {
        if (!string.IsNullOrWhiteSpace(options.SprintName))
        {
            return options.SprintName;
        }

        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            return args[0];
        }

        return await client.GetCurrentSprintNameAsync(CancellationToken.None);
    }

    private static MarkdownReportBuilder BuildReportBuilder()
    {
        var sections = new IReportSection[]
        {
            new ExecutiveSummarySection(),
            new CurrentStateSection(),
            new CompletionSection(),
            new PlanVsActualSection(),
            new CapacitySection(),
            new TaskEstimatesSection(),
            new UserStoriesSection(),
            new SummarySection(),
            new RecommendationsSection()
        };

        return new MarkdownReportBuilder(sections);
    }
}
