using System.Text;
using System.Linq;
using System.IO;
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

        var (isMemberReport, memberSprints, memberFilters) = ParseMemberReportArgs(args);
        if (isMemberReport)
        {
            await GenerateMemberReportsAsync(memberSprints, memberFilters, options, client);
            return;
        }

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
            IterationPath = config["AzureDevOps:IterationPath"],
            MemberFilters = config.GetSection("Report:MemberFilters")
                .GetChildren()
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim())
                .ToArray()
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

    private static (bool isMemberReport, List<string> sprintNames, List<string> memberFilters) ParseMemberReportArgs(string[] args)
    {
        var sprintNames = new List<string>();
        var memberFilters = new List<string>();

        if (args.Length == 0)
        {
            return (false, sprintNames, memberFilters);
        }

        var isMemberReport = string.Equals(args[0], "--member-report", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(args[0], "-m", StringComparison.OrdinalIgnoreCase);

        if (!isMemberReport)
        {
            return (false, sprintNames, memberFilters);
        }

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--members", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-mem", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    memberFilters.AddRange(SplitMembers(args[i + 1]));
                    i++;
                }
                continue;
            }

            sprintNames.Add(Uri.UnescapeDataString(arg));
        }

        return (true, sprintNames, memberFilters);
    }

    private static IEnumerable<string> SplitMembers(string value)
    {
        return value
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim());
    }

    private static async Task GenerateMemberReportsAsync(
        List<string> requestedSprints,
        List<string> requestedMembers,
        AzureDevOpsOptions options,
        IAzureDevOpsClient client)
    {
        var sprintNames = requestedSprints.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();

        if (sprintNames.Count == 0)
        {
            var fallback = await ResolveSprintNameAsync(Array.Empty<string>(), options, client);
            if (string.IsNullOrWhiteSpace(fallback))
            {
                Console.WriteLine("Error: Could not determine sprint name for member report. Provide sprint names after --member-report or configure defaults.");
                return;
            }

            sprintNames.Add(fallback);
        }

        var effectiveMembers = requestedMembers.Any()
            ? requestedMembers
            : (options.MemberFilters ?? Array.Empty<string>()).ToList();

        foreach (var name in sprintNames)
        {
            var sprintName = Uri.UnescapeDataString(name);
            Console.WriteLine($"Generating member task report for sprint: {sprintName}");

            var sprintData = await client.GetSprintDataAsync(sprintName, CancellationToken.None);

            var builder = new MemberTaskReportBuilder();
            var context = new ReportContext
            {
                SprintName = sprintName,
                TeamName = options.TeamName ?? "Team",
                StartDate = sprintData.StartDate,
                EndDate = sprintData.EndDate,
                GeneratedAt = DateTime.Now,
                TeamCapacities = sprintData.TeamCapacities,
                MemberFilters = effectiveMembers
            };

            var report = builder.Build(sprintData, context);
            var outputFile = $"Member_Task_Report_{SanitizeFileNameSegment(sprintName)}_{DateTime.Now:MMM_d_yyyy}.md";
            await File.WriteAllTextAsync(outputFile, report, Encoding.UTF8);
            Console.WriteLine($"Report generated: {outputFile}");
        }
    }

    private static string SanitizeFileNameSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safeChars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(safeChars).Replace(' ', '_');
    }
}
