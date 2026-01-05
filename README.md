# Sprint Report Generator

Generates comprehensive sprint analysis reports from Azure DevOps as Markdown.

## Features
- Fetches work items for a sprint/iteration (current or specified)
- Breakdowns by state, type, priority, assignee, and risks
- Completion vs time progress, plan vs actual, scope change tracking
- Estimates analysis and user story summaries
- Extensible section-based report builder (add a new section by implementing `IReportSection`)

## Prerequisites
- .NET 8.0 SDK or later
- Azure DevOps PAT with **Work Items (Read)** permission

## Setup
1) Install dependencies:
```bash
dotnet restore
```
2) Create a local config (gitignored):
```bash
copy appsettings.Template.json appsettings.json
```
3) Fill `appsettings.json` with your Azure DevOps details and PAT (keep it local).

## Configuration
`appsettings.json` (local only, not committed):
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
  }
}
```
- `IterationPath` (if set) overrides `SprintName`.
- If both are empty, the current sprint is used.

## Running
- Use configured sprint:
```bash
dotnet run
```
- Override sprint name via CLI:
```bash
dotnet run "Sprint Name"
```

## Output
- Generates `Sprint_Complete_Analysis_<MMM_d_yyyy>.md` in the working directory.
- Contents include Executive Summary, Current State, Completion, Estimates, Plan vs Actual, User Stories, and Summary/Insights.

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

## Troubleshooting
- Missing config: ensure BaseUrl/Organization/Project/PatToken are set in your local `appsettings.json`.
- Cannot determine sprint: set `SprintName` or `IterationPath`, or ensure a current sprint exists.
- No work items found: verify sprint name/team and PAT permissions.
