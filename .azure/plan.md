# Reliability Cost Tool Plan

## Status

- Approved by user to begin initial implementation in a new isolated folder.
- Current phase: Initial implementation completed and validated.
- Validation completed with `dotnet build` and `dotnet test` on the isolated solution.
- Solution has now been converted from an Aspire-ready placeholder to a real .NET Aspire AppHost and ServiceDefaults setup.

## Goal

Build a .NET-based client tool, preferably using .NET Aspire for orchestration, with a simple minimalist UI that:

- accepts an Excel assessment workbook as input,
- analyzes Azure resource reliability gaps from workbook rows,
- groups findings by resource type,
- downloads Azure pricing data for remediation cost calculations,
- generates a new Excel workbook with summary and per-resource-type sheets.

## Workspace Mode

- Mode: New solution within existing repository.
- Isolation: All new files live under `reliability-cost-tool/`.

## Initial Assumptions

- The input workbook contains rows describing Azure resources and reliability posture fields that can be mapped into normalized findings.
- Exact workbook schema will continue to evolve; the parser should be resilient and configurable.
- Pricing will use the Azure Retail Prices API with cached local retrieval and region-aware matching where possible.
- First iteration will prioritize architecture, extensibility, and an end-to-end happy path over exhaustive pricing coverage.

## Proposed Architecture

- Aspire AppHost for local orchestration.
- ASP.NET Core backend API for upload, analysis, pricing lookup, and report generation.
- Blazor Web frontend for a lightweight Windows-friendly UI.
- Shared domain/library projects for workbook parsing, pricing, report generation, and resource-specific remediation rules.

## Solution Structure

- `General/` for application-wide contracts and orchestration concerns.
- `Common/` for shared domain models and interfaces.
- One folder per Azure resource type, each containing parsing, rule, and pricing logic.
- `Utils/` for cross-cutting helpers only if needed.

## Resource-Type Folders Planned

- `Compute/`
- `Storage/`
- `Databases/`
- `Backup/`
- `SiteRecovery/`
- `Networking/`

## Deliverables For This Iteration

- New .NET solution scaffold under `reliability-cost-tool/`.
- Minimal UI for upload and result download.
- Backend pipeline implemented end-to-end.
- Excel read/write implementation with per-resource output sheets.
- Azure pricing retrieval service with cache.
- Sample heuristics for VMs, Storage, and Databases.
- Build and test validation.

## Deferred / Follow-up

- Full support for every Azure resource type in the source workbook.
- Advanced pricing edge cases such as reservations, licensing, and cross-region SKUs.
- Strong schema inference based on multiple real customer workbooks.
- Authentication and hosted Azure deployment assets.

## Execution Steps

1. Verify local .NET SDK and Aspire template availability.
2. Scaffold solution and projects in the isolated folder.
3. Implement shared domain models and workbook parsing.
4. Implement reliability rules per resource category.
5. Implement Azure Retail Prices API integration and caching.
6. Implement Excel export with summary and category worksheets.
7. Implement minimalist UI and API wiring.
8. Build and fix compile issues.

## Validation Plan

- `dotnet build` on the new solution.
- Basic smoke validation of upload-to-report workflow where feasible.
