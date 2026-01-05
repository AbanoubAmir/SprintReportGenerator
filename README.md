# Sprint Report Generator

Generates comprehensive sprint analysis reports from Azure DevOps as Markdown.

## Features
- **Two Report Types**: Sprint analysis reports and member task reports
- **Comprehensive Work Tracking**: Includes all work item types (Tasks, Bugs, Features, Epics, etc.)
- **Cross-Iteration Work**: Tracks work done during sprint period even if items are in different iterations
- **Flexible Filtering**: Filter by members, sprints, iteration paths, and team names
- **Parameter Priority**: CLI arguments override config, which overrides defaults
- **Breakdowns**: By state, type, priority, assignee, and risks
- **Progress Tracking**: Completion vs time progress, plan vs actual, scope change tracking
- **Estimates Analysis**: Original estimates, completed work, and remaining work
- **Extensible**: Section-based report builder (add a new section by implementing `IReportSection`)
- **Performance Optimized**: Parallel queries and optimized data fetching

## Prerequisites
- .NET 8.0 SDK or later
- Azure DevOps PAT with **Work Items (Read)** permission

## Setup
1) Install dependencies:
```bash
dotnet restore
```
2) Open the included `appsettings.json` and fill your Azure DevOps details and PAT.
3) Optional: create `appsettings.local.json` (gitignored) to override values locally without touching the shared `appsettings.json`.

## Configuration
`appsettings.json` (committed with placeholders; edit in place):
```json
{
  "AzureDevOps": {
    "BaseUrl": "https://dev.azure.com",
    "Organization": "your-organization",
    "Project": "your-project",
    "PatToken": "<personal-access-token>",
    "TeamName": "",
    "SprintName": "",
    "IterationPath": ""
  },
  "Report": {
    "MemberFilters": []
  }
}
```
- Overrides in `appsettings.local.json` take precedence when present (and stay local).
- `IterationPath` (if set) overrides `SprintName`.
- If both are empty, the current sprint is used.
- `MemberFilters` in config can be used to filter member reports without specifying in CLI.

## Running

### Command-Line Interface

The tool supports a unified command-line interface for both report types. Parameters follow this priority:
1. **CLI arguments** (highest priority)
2. **appsettings.json / appsettings.local.json**
3. **Defaults** (e.g., current sprint if nothing specified)

### Report Types

- `--sprint-report` or `-s`: Generate sprint analysis report (default if no report type specified)
- `--member-report` or `-m`: Generate member task report

### Common Parameters

- `--sprint` or `-sprint <name>`: Sprint name(s) - can specify multiple for member reports
- `--members` or `-mem <list>`: Comma/semicolon-separated member filter list
- `--iteration-path` or `-ip <path>`: Iteration path (overrides sprint name)
- `--team` or `-t <name>`: Team name
- `--help` or `-h`: Show help message

### Sprint Analysis Report Examples

```bash
# Use defaults from config (current sprint or configured sprint)
dotnet run

# Explicit sprint report with sprint name
dotnet run -- --sprint-report --sprint "Sprint 1"

# With iteration path
dotnet run -- --sprint-report --sprint "Sprint 1" --iteration-path "Project\\Iteration\\Sprint 1"

# Legacy format (still works)
dotnet run "Sprint Name"
```

### Member Task Report Examples

```bash
# Multiple sprints (new format)
dotnet run -- --member-report --sprint "Jan-1 2026" "Dec-2 2025"

# With member filters
dotnet run -- --member-report --sprint "Sprint 1" --members "John, Jane"

# Legacy format (still works)
dotnet run -- --member-report "Jan-1%202026" "Dec-2%202025"

# With all parameters
dotnet run -- --member-report --sprint "Sprint 1" --members "John, Jane" --iteration-path "path" --team "Team Name"

# Multiple sprints with member filters
dotnet run -- --member-report --sprint "Sprint 1" "Sprint 2" --members "John;Jane;Bob"
```

### Help

```bash
dotnet run -- --help
```

### Notes

- If no `--members` are provided and `Report:MemberFilters` in config is empty, the member report includes everyone.
- To keep member names out of source control, set filters in `appsettings.local.json` (gitignored).
- Sprint names with spaces or special characters should be URL-encoded in legacy format (e.g., `Jan-1%202026`).
- The new format handles spaces automatically: `--sprint "Jan-1 2026"`.

## Output

### Sprint Analysis Report
- Generates `Sprint_Complete_Analysis_<MMM_d_yyyy>.md` in the working directory.
- Contents include:
  - Executive Summary
  - Current State (breakdown by state, type, priority, assignee)
  - Completion metrics
  - Estimates analysis
  - Plan vs Actual
  - Capacity analysis
  - Task estimates
  - User Stories summary
  - Summary and Insights
  - Recommendations

### Member Task Report
- Generates `Member_Task_Report_<sprint>_<date>.md` when `--member-report` is used.
- Includes all work item types (not just Tasks and Bugs).
- Tracks cross-iteration work done during the sprint period.
- Grouped by team member with:
  - Total items, completed items
  - Original estimates, completed work, remaining work
  - Detailed task/bug listing with parent work items
  - Status, dates, and assignments
- Honors member filters from CLI or `Report:MemberFilters` in `appsettings.json`.
- Includes work items that were active during the sprint period, even if they're in different iterations.

## Architecture (extensible)
- **Data**: `IAzureDevOpsClient` / `AzureDevOpsClient`
- **Analysis**: `IWorkItemAnalyzer` / `WorkItemAnalyzer`
- **Reporting**: `MarkdownReportBuilder` with pluggable `IReportSection` implementations under `Reporting/Sections`.

To add a new section: create a class implementing `IReportSection`, then register it in `Program.cs` (`BuildReportBuilder`).

## Security & hygiene
- `appsettings.json` is gitignored; keep PATs out of git.
- Generated reports (`Sprint_Complete_Analysis_*.md/.txt`) are gitignored.
- Review `git status` before publishing to ensure no secrets or reports are staged.

## Publishing to GitHub (manual steps)
1) Ensure working tree is clean of secrets and reports:
```bash
git status
```
2) Build locally to verify:
```bash
dotnet build
```
3) Commit safe files:
```bash
git add .
git commit -m "Prepare public release"
```
4) Create a GitHub repo and push:
```bash
git remote add origin https://github.com/<your-account>/<repo>.git
git push -u origin main
```

## Advanced Features

### Cross-Iteration Work Tracking
The tool automatically includes work items that were changed during the sprint period, even if they're assigned to different iterations. This ensures comprehensive tracking of all team effort during the sprint.

### All Work Item Types
Reports include all work item types (Tasks, Bugs, Features, Epics, Issues, Code Reviews, etc.), not just Tasks and Bugs. This provides a complete picture of team activity.

### Performance Optimizations
- Parallel query execution for faster data retrieval
- Optimized WIQL queries that exclude already-fetched items
- Parallel batch processing for work item details
- Automatic pagination support for large datasets

## Troubleshooting

- **Missing config**: Ensure `BaseUrl`, `Organization`, `Project`, and `PatToken` are set in your local `appsettings.json`.
- **Cannot determine sprint**: Set `SprintName` or `IterationPath` in config, provide via `--sprint`, or ensure a current sprint exists in Azure DevOps.
- **No work items found**: Verify sprint name/team and PAT permissions. Check that the sprint exists and has work items assigned.
- **Slow performance**: The tool automatically optimizes queries, but very large projects may take time. Check logs for query execution times.
- **Help**: Run `dotnet run -- --help` to see all available options and examples.
