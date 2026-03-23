# Reliability Cost Tool

This solution is isolated under `reliability-cost-tool/` and is intended to analyze Excel-based Azure reliability assessments, estimate remediation costs using the Azure Retail Prices API, and generate a workbook with summary and per-resource sheets.

## Projects

- `src/ReliabilityCostTool.Web` - Blazor Server UI for workbook upload and report download.
- `src/ReliabilityCostTool.Api` - HTTP API for analysis automation and report download.
- `src/ReliabilityCostTool.Core` - Shared parsing, rules, pricing, and Excel report generation logic.
- `src/ReliabilityCostTool.ServiceDefaults` - Aspire service defaults, health checks, resilience, service discovery, and OpenTelemetry wiring.
- `src/ReliabilityCostTool.AppHost` - .NET Aspire AppHost for local orchestration.

## Current Coverage

First-pass remediation rules are implemented for:

- Compute / virtual machines
- Storage
- Databases
- Backup
- Site Recovery

The parser is schema-tolerant and uses header alias matching so it can iterate toward the real workbook structure without requiring a single rigid schema on day one.

## Run

### Web UI

```powershell
dotnet run --project .\src\ReliabilityCostTool.Web
```

### Aspire AppHost

```powershell
dotnet run --project .\src\ReliabilityCostTool.AppHost
```

### API

```powershell
dotnet run --project .\src\ReliabilityCostTool.Api
```

### Build

```powershell
dotnet build .\ReliabilityCostTool.sln
```

## Notes

- Input supports `.xls` and `.xlsx`.
- Output is generated as `.xlsx`.
- Pricing is looked up from the Azure Retail Prices API at analysis time.
- Cost estimates currently use heuristic mapping and should be refined once the exact workbook schema and target remediation policies are finalized.
- The machine now has `Aspire.ProjectTemplates` installed and the solution has been converted from an Aspire-ready placeholder to a real Aspire AppHost and ServiceDefaults setup.
