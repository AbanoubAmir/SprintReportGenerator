using System.Text;
using System.Linq;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SprintReportGenerator.Analysis;
using SprintReportGenerator.Reporting;
using SprintReportGenerator.Reporting.Sections;
using SprintReportGenerator.Services;

namespace SprintReportGenerator;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length > 0 && (args[0].Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                                args[0].Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                                args[0].Equals("/?", StringComparison.OrdinalIgnoreCase)))
        {
            ShowHelp();
            return;
        }

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<Program>();

        logger.LogInformation("Starting sprint report generation");

        var configOptions = LoadOptions();
        if (!HasRequiredOptions(configOptions))
        {
            logger.LogError("Missing required configuration. Please check appsettings.json");
            Console.WriteLine("Error: Missing required configuration. Please check appsettings.json");
            return;
        }

        logger.LogInformation("Configuration loaded. Organization: {Organization}, Project: {Project}", configOptions.Organization, configOptions.Project);

        var parsedArgs = ParseArguments(args);
        var effectiveOptions = MergeOptions(configOptions, parsedArgs);

        var client = new AzureDevOpsClient(effectiveOptions, loggerFactory.CreateLogger<AzureDevOpsClient>());

        if (parsedArgs.ReportType == ReportType.MemberReport)
        {
            await GenerateMemberReportsAsync(parsedArgs, effectiveOptions, client, loggerFactory);
            return;
        }

        await GenerateSprintReportAsync(parsedArgs, effectiveOptions, client, loggerFactory);
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Sprint Report Generator");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run [--report-type] [options]");
        Console.WriteLine();
        Console.WriteLine("Report Types:");
        Console.WriteLine("  --sprint-report, -s    Generate sprint analysis report (default)");
        Console.WriteLine("  --member-report, -m   Generate member task report");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --sprint, -sprint <name>     Sprint name(s) - can specify multiple for member reports");
        Console.WriteLine("  --members, -mem <list>        Comma/semicolon-separated member filter list");
        Console.WriteLine("  --iteration-path, -ip <path>  Iteration path (overrides sprint name)");
        Console.WriteLine("  --team, -t <name>             Team name");
        Console.WriteLine("  --help, -h                    Show this help message");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run                                    # Sprint report using config/defaults");
        Console.WriteLine("  dotnet run -- --sprint-report --sprint \"Sprint 1\"");
        Console.WriteLine("  dotnet run -- --member-report --sprint \"Sprint 1\" \"Sprint 2\"");
        Console.WriteLine("  dotnet run -- --member-report --sprint \"Sprint 1\" --members \"John, Jane\"");
        Console.WriteLine("  dotnet run -- --member-report \"Jan-1%202026\" \"Dec-2%202025\"  # Legacy format still works");
        Console.WriteLine();
        Console.WriteLine("Parameter Priority: CLI arguments > appsettings.json > defaults");
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
        AzureDevOpsOptions options,
        IAzureDevOpsClient client,
        ILogger<Program> logger)
    {
        if (!string.IsNullOrWhiteSpace(options.SprintName))
        {
            logger.LogInformation("Using sprint name from configuration/CLI: {SprintName}", options.SprintName);
            return options.SprintName;
        }

        logger.LogInformation("Resolving current sprint name from Azure DevOps");
        var currentSprint = await client.GetCurrentSprintNameAsync(CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(currentSprint))
        {
            logger.LogInformation("Resolved current sprint: {SprintName}", currentSprint);
        }
        else
        {
            logger.LogWarning("Could not resolve current sprint name");
        }
        return currentSprint;
    }

    private static async Task GenerateSprintReportAsync(
        ParsedArguments parsedArgs,
        AzureDevOpsOptions options,
        IAzureDevOpsClient client,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<Program>();
        var sprintName = await ResolveSprintNameAsync(options, client, logger);

        if (string.IsNullOrWhiteSpace(sprintName))
        {
            logger.LogError("Could not determine sprint name. Please configure in appsettings.json, provide via --sprint, or ensure current iteration is set.");
            Console.WriteLine("Error: Could not determine sprint name. Please configure in appsettings.json, provide via --sprint, or ensure current iteration is set.");
            return;
        }

        logger.LogInformation("Generating sprint report for sprint: {SprintName}", sprintName);
        Console.WriteLine($"Generating report for sprint: {sprintName}");
        Console.WriteLine("Fetching work items...");

        var sprintData = await client.GetSprintDataAsync(sprintName, CancellationToken.None);

        logger.LogInformation("Fetched {WorkItemCount} work items for sprint {SprintName}. Sprint period: {StartDate} to {EndDate}",
            sprintData.WorkItems.Count, sprintName, sprintData.StartDate, sprintData.EndDate);

        Console.WriteLine("Analyzing data...");
        var analyzer = new WorkItemAnalyzer(loggerFactory.CreateLogger<WorkItemAnalyzer>());
        var analysis = analyzer.Analyze(sprintData.WorkItems, sprintData.StartDate, sprintData.IterationWorkItemIds);

        logger.LogInformation("Analysis complete. Total items: {TotalItems}, Completed: {CompletedCount}, In Progress: {InProgressCount}, Not Started: {NotStartedCount}",
            analysis.TotalItems, analysis.CompletedCount, analysis.InProgressCount, analysis.NotStartedCount);

        var builder = BuildReportBuilder(loggerFactory.CreateLogger<MarkdownReportBuilder>());
        var context = new ReportContext
        {
            SprintName = sprintName,
            TeamName = options.TeamName ?? "Team",
            StartDate = sprintData.StartDate,
            EndDate = sprintData.EndDate,
            GeneratedAt = DateTime.Now,
            TeamCapacities = sprintData.TeamCapacities,
            WorkItemUrlBase = BuildWorkItemUrlBase(options)
        };

        logger.LogInformation("Building report with {SectionCount} sections", builder.GetSectionCount());
        var report = builder.Build(analysis, context);
        var outputFile = $"Sprint_Complete_Analysis_{DateTime.Now:MMM_d_yyyy}.md";
        await File.WriteAllTextAsync(outputFile, report, Encoding.UTF8);

        logger.LogInformation("Report generated successfully. Output file: {OutputFile}, Report size: {ReportSize} characters", outputFile, report.Length);
        Console.WriteLine($"Report generated: {outputFile}");
    }

    private static MarkdownReportBuilder BuildReportBuilder(ILogger<MarkdownReportBuilder> logger)
    {
        var sections = new IReportSection[]
        {
            new ExecutiveSummarySection(),
            new CurrentStateSection(),
            new CompletionSection(),
            new PlanVsActualSection(),
            new CrossIterationWorkSection(),
            new CapacitySection(),
            new TaskEstimatesSection(),
            new UserStoriesSection(),
            new SummarySection(),
            new RecommendationsSection()
        };

        return new MarkdownReportBuilder(sections, logger);
    }

    private enum ReportType
    {
        SprintReport,
        MemberReport
    }

    private class ParsedArguments
    {
        public ReportType ReportType { get; set; } = ReportType.SprintReport;
        public List<string> SprintNames { get; set; } = new();
        public List<string> MemberFilters { get; set; } = new();
        public string? IterationPath { get; set; }
        public string? TeamName { get; set; }
    }

    private static ParsedArguments ParseArguments(string[] args)
    {
        var parsed = new ParsedArguments();

        if (args.Length == 0)
        {
            return parsed;
        }

        var i = 0;
        while (i < args.Length)
        {
            var arg = args[i];

            if (string.Equals(arg, "--member-report", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-m", StringComparison.OrdinalIgnoreCase))
            {
                parsed.ReportType = ReportType.MemberReport;
                i++;
                continue;
            }

            if (string.Equals(arg, "--sprint-report", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-s", StringComparison.OrdinalIgnoreCase))
            {
                parsed.ReportType = ReportType.SprintReport;
                i++;
                continue;
            }

            if (string.Equals(arg, "--sprint", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-sprint", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    var sprintValue = args[i + 1];
                    if (!sprintValue.StartsWith("--") && !sprintValue.StartsWith("-"))
                    {
                        parsed.SprintNames.Add(Uri.UnescapeDataString(sprintValue));
                        i += 2;
                        continue;
                    }
                }
                i++;
                continue;
            }

            if (string.Equals(arg, "--members", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-mem", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    parsed.MemberFilters.AddRange(SplitMembers(args[i + 1]));
                    i += 2;
                    continue;
                }
                i++;
                continue;
            }

            if (string.Equals(arg, "--iteration-path", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-ip", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    parsed.IterationPath = args[i + 1];
                    i += 2;
                    continue;
                }
                i++;
                continue;
            }

            if (string.Equals(arg, "--team", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-t", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    parsed.TeamName = args[i + 1];
                    i += 2;
                    continue;
                }
                i++;
                continue;
            }

            if (parsed.ReportType == ReportType.MemberReport && !arg.StartsWith("--") && !arg.StartsWith("-"))
            {
                parsed.SprintNames.Add(Uri.UnescapeDataString(arg));
            }
            else if (parsed.ReportType == ReportType.SprintReport && parsed.SprintNames.Count == 0 && !arg.StartsWith("--") && !arg.StartsWith("-"))
            {
                parsed.SprintNames.Add(Uri.UnescapeDataString(arg));
            }

            i++;
        }

        return parsed;
    }

    private static AzureDevOpsOptions MergeOptions(AzureDevOpsOptions configOptions, ParsedArguments parsedArgs)
    {
        return new AzureDevOpsOptions
        {
            BaseUrl = configOptions.BaseUrl,
            Organization = configOptions.Organization,
            Project = configOptions.Project,
            PatToken = configOptions.PatToken,
            TeamName = parsedArgs.TeamName ?? configOptions.TeamName,
            SprintName = parsedArgs.SprintNames.FirstOrDefault() ?? configOptions.SprintName,
            IterationPath = parsedArgs.IterationPath ?? configOptions.IterationPath,
            MemberFilters = parsedArgs.MemberFilters.Any() ? parsedArgs.MemberFilters.ToArray() : configOptions.MemberFilters
        };
    }

    private static IEnumerable<string> SplitMembers(string value)
    {
        return value
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim());
    }

    private static async Task GenerateMemberReportsAsync(
        ParsedArguments parsedArgs,
        AzureDevOpsOptions options,
        IAzureDevOpsClient client,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<Program>();
        var sprintNames = parsedArgs.SprintNames.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();

        if (sprintNames.Count == 0)
        {
            logger.LogInformation("No sprint names provided, resolving current sprint");
            var fallback = await ResolveSprintNameAsync(options, client, logger);
            if (string.IsNullOrWhiteSpace(fallback))
            {
                logger.LogError("Could not determine sprint name for member report. Provide sprint names via --sprint or configure defaults.");
                Console.WriteLine("Error: Could not determine sprint name for member report. Provide sprint names via --sprint or configure defaults.");
                return;
            }

            sprintNames.Add(fallback);
        }

        var effectiveMembers = parsedArgs.MemberFilters.Any()
            ? parsedArgs.MemberFilters
            : (options.MemberFilters ?? Array.Empty<string>()).ToList();

        logger.LogInformation("Generating member reports for {SprintCount} sprint(s) with {MemberCount} member filter(s)", sprintNames.Count, effectiveMembers.Count);

        foreach (var name in sprintNames)
        {
            var sprintName = Uri.UnescapeDataString(name);
            logger.LogInformation("Generating member task report for sprint: {SprintName}", sprintName);
            Console.WriteLine($"Generating member task report for sprint: {sprintName}");

            var sprintData = await client.GetSprintDataAsync(sprintName, CancellationToken.None);
            logger.LogInformation("Fetched {WorkItemCount} work items for member report", sprintData.WorkItems.Count);

            var builder = new MemberTaskReportBuilder(loggerFactory.CreateLogger<MemberTaskReportBuilder>());
            var context = new ReportContext
            {
                SprintName = sprintName,
                TeamName = options.TeamName ?? "Team",
                StartDate = sprintData.StartDate,
                EndDate = sprintData.EndDate,
                GeneratedAt = DateTime.Now,
                TeamCapacities = sprintData.TeamCapacities,
                MemberFilters = effectiveMembers,
                WorkItemUrlBase = BuildWorkItemUrlBase(options)
            };

            logger.LogInformation("Building member task report");
            var report = builder.Build(sprintData, context);
            var outputFile = $"Member_Task_Report_{SanitizeFileNameSegment(sprintName)}_{DateTime.Now:MMM_d_yyyy}.md";
            await File.WriteAllTextAsync(outputFile, report, Encoding.UTF8);
            logger.LogInformation("Member report generated successfully. Output file: {OutputFile}, Report size: {ReportSize} characters", outputFile, report.Length);
            Console.WriteLine($"Report generated: {outputFile}");
        }
    }

    private static string SanitizeFileNameSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safeChars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(safeChars).Replace(' ', '_');
    }

    private static string? BuildWorkItemUrlBase(AzureDevOpsOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl) ||
            string.IsNullOrWhiteSpace(options.Organization) ||
            string.IsNullOrWhiteSpace(options.Project))
        {
            return null;
        }

        var baseUrl = options.BaseUrl.TrimEnd('/');
        var org = options.Organization.Trim();
        var project = options.Project.Trim();
        return $"{baseUrl}/{org}/{project}/_workitems/edit/";
    }
}
