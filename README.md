# Reliability Cost Tool

A .NET 10 solution that analyzes Excel-based Azure reliability assessments, identifies remediation gaps (missing zone redundancy, HA, backup, site recovery), estimates remediation costs using the [Azure Retail Prices API](https://learn.microsoft.com/en-us/rest/api/cost-management/retail-prices/azure-retail-prices), and generates detailed Excel workbooks with summarized recommendations and cost breakdowns.

Built with .NET Aspire for orchestration, Blazor Server for the UI, and a minimal API backend.

---

## Architecture

```
User Upload (Excel workbook)
    │
    ▼
Web UI (Blazor Server) ──or── API (POST /api/reports/analyze)
    │
    ▼
AssessmentWorkbookParser  (+AzureSqlDatabaseSectionParser)
    │  produces List<AssessmentRecord>
    ▼
ReliabilityAssessmentService
    │  applies each IReliabilityRule to each record
    ▼
┌─────────────────────────────────────────────────────┐
│  VmReliabilityRule         StorageReliabilityRule    │
│  DatabaseReliabilityRule   AzureSqlDatabaseRule      │
│  BackupReliabilityRule     SiteRecoveryRule          │
└─────────────────────────────────────────────────────┘
    │  each rule calls AzureRetailPriceClient
    ▼
AnalysisResult (findings + pricing)
    │
    ▼
WorkbookReportBuilder → Generated .xlsx
    │
    ▼
Download via Web UI or GET /api/reports/{id}/download
```

---

## Projects (`src/`)

### ReliabilityCostTool.Core

The domain library — all parsing, rules, pricing, and report-generation logic lives here. No web dependencies.

| Folder                  | Purpose                                                                                                                                                                                                                                                                                                                                                                                               |
| ----------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Common/Models/**      | Core data models shared across the entire solution. `AssessmentRecord` (a single parsed Excel row with fuzzy column access), `ReliabilityFinding` (an individual gap with cost estimate), `AnalysisResult` (aggregated findings and statistics), `GeneratedWorkbook` (output XLSX bytes + metadata), `PriceCatalogItem` (price lookup result).                                                        |
| **General/Interfaces/** | Contracts for the major components: `IReliabilityRule` (category-specific rule evaluation), `IAssessmentWorkbookParser` (Excel → records), `IReliabilityAssessmentService` (orchestrator), `IPriceCatalogClient` (pricing lookups), `IWorkbookReportBuilder` (Excel output).                                                                                                                          |
| **General/Workbook/**   | `AssessmentWorkbookParser` — parses `.xls`/`.xlsx` files using ExcelDataReader, handles schema variations via column alias matching, and delegates Azure SQL DB sections to the specialized parser. `WorkbookReadException` for user-friendly parse error messages.                                                                                                                                   |
| **General/Pricing/**    | `AzureRetailPriceClient` — calls the Azure Retail Prices REST API with fuzzy matching on service name, region, SKU, and meter hints. Implements async caching, pagination, and timeout handling.                                                                                                                                                                                                      |
| **General/Reports/**    | `WorkbookReportBuilder` — builds a multi-tab XLSX report using ClosedXML: Summary, Detail, per-category tabs (Compute, Storage, Databases, Backup, Site Recovery), Pricing Debug, and Azure SQL DB Results.                                                                                                                                                                                           |
| **General/Services/**   | `ReliabilityAssessmentService` — the main orchestrator. Parses the workbook, iterates records through all registered `IReliabilityRule` implementations, collects findings, and delegates report building.                                                                                                                                                                                            |
| **Compute/**            | `VmReliabilityRule` — detects non-redundant Virtual Machines and VMSS instances; estimates cost for a second instance or VMSS migration (2× monthly VM price).                                                                                                                                                                                                                                        |
| **Storage/**            | `StorageReliabilityRule` — identifies storage accounts without ZRS/GZRS redundancy (blobs, disks, file shares); estimates per-GB-month cost to upgrade.                                                                                                                                                                                                                                               |
| **Databases/**          | `DatabaseReliabilityRule` — generic rule for SQL Server, PostgreSQL, MySQL, and Cosmos DB; recommends HA or geo-failover enablement (1× hourly compute/storage tier × 730 hours).                                                                                                                                                                                                                     |
| **AzureSqlDatabases/**  | `AzureSqlDatabaseReliabilityRule` — handles Azure SQL DB aggregate resiliency summaries (tier, total count, zone-redundant count, geo-replicated count). `AzureSqlDatabaseSectionParser` extracts structured tables from special sections in the workbook. Estimates a 25% premium for zone redundancy and 100% for geo-replication.                                                                  |
| **Backup/**             | `BackupReliabilityRule` — detects workloads missing backup coverage; estimates backup storage cost (per-GB-month × capacity, defaults to 100 GB).                                                                                                                                                                                                                                                     |
| **SiteRecovery/**       | `SiteRecoveryRule` — identifies workloads without Azure Site Recovery (ASR) replication; estimates per-instance monthly ASR price.                                                                                                                                                                                                                                                                    |
| **Utils/**              | `AssessmentHeuristics` — pattern matching for reliability gaps (gap markers like "not zon", "lrs", "single instance" vs. health markers like "zone redundant", "zrs", "gzrs"); numeric extraction for quantity/capacity; region normalization. `ColumnAliases` — maps multiple column header variants to semantic meanings (ResourceName, Region, SKU, ReliabilitySignals, Quantity, Capacity, etc.). |

### ReliabilityCostTool.Api

Minimal API project exposing HTTP endpoints for programmatic access.

| Folder/File    | Purpose                                                                                                                                              |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Program.cs** | DI wiring — registers all six `IReliabilityRule` implementations, workbook parser, report builder, pricing client, and report store. Maps endpoints. |
| **Contracts/** | `AnalyzeResponse` — response DTO for `/api/reports/analyze` (report ID, file name, record/finding counts, total cost, per-category breakdowns).      |
| **Services/**  | `GeneratedReportStore` — in-memory `ConcurrentDictionary<Guid, GeneratedWorkbook>` for transient report storage.                                     |

**Endpoints:**

| Route                              | Method | Description                                                        |
| ---------------------------------- | ------ | ------------------------------------------------------------------ |
| `/api/reports/analyze`             | POST   | Upload an Excel workbook; returns analysis results and a report ID |
| `/api/reports/{reportId}/download` | GET    | Download the generated `.xlsx` report                              |

### ReliabilityCostTool.Web

Blazor Server front-end with interactive server rendering.

| Folder/File                          | Purpose                                                                                                                                            |
| ------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Components/Pages/Home.razor[.cs]** | Main page — file upload, live processing log during analysis, results display (findings, Azure SQL tables, pricing warnings), and download button. |
| **Components/Pages/Error.razor**     | Error page.                                                                                                                                        |
| **Components/Pages/NotFound.razor**  | 404 page.                                                                                                                                          |
| **Components/Layout/**               | `MainLayout.razor` (master wrapper), `NavMenu.razor` (navigation), `ReconnectModal.razor` (Blazor circuit reconnection UI).                        |
| **Components/App.razor**             | Blazor router and entry point.                                                                                                                     |
| **Program.cs**                       | DI setup (same Core registrations as API) and Blazor Server pipeline.                                                                              |
| **wwwroot/**                         | Static assets (CSS).                                                                                                                               |

### ReliabilityCostTool.ServiceDefaults

Shared .NET Aspire service configuration, referenced by both Web and API projects.

| File              | Purpose                                                                                                                                                                                                                                             |
| ----------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Extensions.cs** | `AddServiceDefaults()` extension method that configures OpenTelemetry (logging, metrics, tracing), health checks (`/alive` for liveness, `/health` for readiness), service discovery, and HTTP client resilience (retry, circuit breaker, timeout). |

### ReliabilityCostTool.AppHost

.NET Aspire orchestration host for local development.

| File           | Purpose                                                                                                                                                |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Program.cs** | Registers the API and Web projects as Aspire resources, wires service discovery (Web → API), and configures health checks and external HTTP endpoints. |

---

## Reliability Rules Summary

| Rule                                | Category      | What It Detects                           | Cost Estimate Approach                   |
| ----------------------------------- | ------------- | ----------------------------------------- | ---------------------------------------- |
| **VmReliabilityRule**               | Compute       | Non-redundant VMs / VMSS                  | 2× monthly VM price (second instance)    |
| **StorageReliabilityRule**          | Storage       | LRS/non-ZRS storage                       | Per-GB-month ZRS tier price × capacity   |
| **DatabaseReliabilityRule**         | Databases     | SQL, PostgreSQL, MySQL, Cosmos without HA | 1× hourly compute tier × 730 hours       |
| **AzureSqlDatabaseReliabilityRule** | Databases     | Azure SQL DB missing ZR / geo-replication | 25% premium for ZR; 100% for geo-replica |
| **BackupReliabilityRule**           | Backup        | Missing backup coverage                   | Per-GB-month backup price × capacity     |
| **SiteRecoveryRule**                | Site Recovery | Missing ASR replication                   | Per-instance ASR monthly price           |

---

## Generated Report Structure

The output `.xlsx` workbook contains:

1. **Summary** — total rows analyzed, findings count, estimated total cost, pricing match rate
2. **Detail** — every finding with full metadata (gap, recommendation, cost, region, SKU, confidence, source row)
3. **Per-Category Sheets** — Compute, Storage, Databases, Backup, Site Recovery findings grouped with resource type distribution
4. **Pricing Summary** — matched vs. unavailable pricing statistics
5. **Pricing Debug** — detailed price-lookup metadata for each finding
6. **Azure SQL DB Results** — parsed resiliency summary tables (if applicable)

---

## Key Dependencies

| Package                               | Version | Usage                                  |
| ------------------------------------- | ------- | -------------------------------------- |
| .NET SDK                              | 10.0    | Target framework                       |
| ClosedXML                             | 0.105.0 | Excel report generation                |
| ExcelDataReader                       | 3.8.0   | Excel parsing (.xls/.xlsx)             |
| Aspire.AppHost.Sdk                    | 13.1.3  | Orchestration                          |
| OpenTelemetry                         | 1.14.0  | Distributed tracing, metrics, logging  |
| Microsoft.Extensions.Http.Resilience  | 10.1.0  | Retry / circuit breaker for HTTP calls |
| Microsoft.Extensions.ServiceDiscovery | 10.1.0  | Service resolution                     |

---

## Run

```powershell
# Aspire orchestrated (Web + API together)
dotnet run --project .\src\ReliabilityCostTool.AppHost

# Web UI only
dotnet run --project .\src\ReliabilityCostTool.Web

# API only
dotnet run --project .\src\ReliabilityCostTool.Api

# Build
dotnet build .\ReliabilityCostTool.sln
```

---

## Tests

```powershell
dotnet test .\tests\ReliabilityCostTool.Core.Tests
```

---

## Design Highlights

- **Pluggable rules** — add a new reliability check by implementing `IReliabilityRule` and registering it in DI.
- **Schema-tolerant parsing** — column alias matching means the parser adapts to workbook header variations without requiring a rigid schema.
- **Async pricing with caching** — `AzureRetailPriceClient` deduplicates concurrent lookups for the same service/SKU/region.
- **Heuristic gap detection** — pattern matching on reliability signals (gap markers vs. health markers) handles varied assessment formats.
- **Full observability** — OpenTelemetry instrumentation across all projects via ServiceDefaults.

## Notes

- Input supports `.xls` and `.xlsx`.
- Output is generated as `.xlsx`.
- Pricing is looked up from the Azure Retail Prices API at analysis time.
- Cost estimates currently use heuristic mapping and should be refined once the exact workbook schema and target remediation policies are finalized.
- The machine now has `Aspire.ProjectTemplates` installed and the solution has been converted from an Aspire-ready placeholder to a real Aspire AppHost and ServiceDefaults setup.
