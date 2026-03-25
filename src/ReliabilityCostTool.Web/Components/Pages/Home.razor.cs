using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using ReliabilityCostTool.Core.Common.Models;
using ReliabilityCostTool.Core.General.Interfaces;
using ReliabilityCostTool.Core.General.Workbook;

namespace ReliabilityCostTool.Web.Components.Pages;

public partial class Home : ComponentBase
{
    private const long MaxUploadSize = 25 * 1024 * 1024;

    [Inject]
    private IReliabilityAssessmentService AssessmentService { get; set; } = default!;

    [Inject]
    private IJSRuntime JsRuntime { get; set; } = default!;

    [Inject]
    private ILogger<Home> Logger { get; set; } = default!;

    protected bool IsBusy { get; set; }

    protected string? ErrorMessage { get; set; }

    protected string? StatusMessage { get; set; }

    protected GeneratedWorkbook? Workbook { get; set; }

    protected IReadOnlyList<AzureSqlResultRow> AzureSqlResults => BuildAzureSqlResults();

    protected bool HasUnavailablePricing => Workbook?.AnalysisResult.PricingUnavailableFindings > 0;

    protected string PricingWarningMessage => BuildPricingWarningMessage();

    protected async Task AnalyzeAsync(InputFileChangeEventArgs eventArgs)
    {
        ErrorMessage = null;
        StatusMessage = null;
        Workbook = null;
        IsBusy = true;
        Logger.LogInformation("Upload event received");
        await InvokeAsync(StateHasChanged);

        try
        {
            var file = eventArgs.File;
            Logger.LogInformation("File upload selected: {FileName}, size {FileSize} bytes", file.Name, file.Size);
            StatusMessage = $"Uploading {file.Name}...";
            await InvokeAsync(StateHasChanged);

            await using var source = file.OpenReadStream(MaxUploadSize);
            Logger.LogInformation("Opened browser upload stream for {FileName}", file.Name);
            await using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer);
            Logger.LogInformation("Copied upload stream into memory buffer ({ByteCount} bytes) for {FileName}", buffer.Length, file.Name);
            buffer.Position = 0;
            StatusMessage = "Workbook uploaded. Starting parser and analysis.";
            await InvokeAsync(StateHasChanged);

            Workbook = await AssessmentService.AnalyzeAsync(buffer, file.Name);
            StatusMessage = $"Workbook read successfully. Parsed {Workbook.AnalysisResult.Records.Count} records and generated {Workbook.AnalysisResult.TotalFindings} findings.";
            Logger.LogInformation(
                "Workbook {FileName} analyzed successfully. Records={RecordCount}, Findings={FindingCount}, AzureSqlRows={AzureSqlCount}",
                file.Name,
                Workbook.AnalysisResult.Records.Count,
                Workbook.AnalysisResult.TotalFindings,
                AzureSqlResults.Count);
        }
        catch (WorkbookReadException exception)
        {
            ErrorMessage = exception.Message;
            Logger.LogWarning(exception, "Workbook read failed in UI workflow");
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            Logger.LogError(exception, "Unexpected failure while analyzing workbook");
        }
        finally
        {
            IsBusy = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    protected async Task DownloadAsync()
    {
        if (Workbook is null)
        {
            return;
        }

        await JsRuntime.InvokeVoidAsync(
            "reliabilityCostTool.downloadFile",
            Workbook.FileName,
            Workbook.ContentType,
            Convert.ToBase64String(Workbook.Content));

        Logger.LogInformation("Output workbook {OutputFileName} downloaded", Workbook.FileName);
    }

    private IReadOnlyList<AzureSqlResultRow> BuildAzureSqlResults()
    {
        if (Workbook is null)
        {
            return [];
        }

        var findingsByKey = Workbook.AnalysisResult.Findings
            .Where(finding => string.Equals(finding.ResourceType, "Azure SQL DB", StringComparison.OrdinalIgnoreCase))
            .ToLookup(finding => (finding.ResourceName, finding.SourceWorksheet, finding.SourceRowNumber));

        return Workbook.AnalysisResult.Records
            .Where(record => string.Equals(record.ResourceType, "Azure SQL DB", StringComparison.OrdinalIgnoreCase))
            .Select(record =>
            {
                var groupedFindings = findingsByKey[(record.ResourceName, record.SourceWorksheet, record.RowNumber)].ToList();
                var zoneFinding = groupedFindings.FirstOrDefault(finding => finding.GapDescription.Contains("zone redundant", StringComparison.OrdinalIgnoreCase));
                var geoFinding = groupedFindings.FirstOrDefault(finding => finding.GapDescription.Contains("geo-replication", StringComparison.OrdinalIgnoreCase));

                return new AzureSqlResultRow(
                    record.ResourceName,
                    record.SourceWorksheet,
                    record.GetValue(Core.Utils.ColumnAliases.AzureSqlDbCount) ?? "0",
                    zoneFinding?.Quantity ?? 0m,
                    zoneFinding?.EstimatedMonthlyCost ?? 0m,
                    geoFinding?.Quantity ?? 0m,
                    geoFinding?.EstimatedMonthlyCost ?? 0m,
                    zoneFinding?.CurrencyCode ?? geoFinding?.CurrencyCode ?? "USD");
            })
            .ToList();
    }

    private string BuildPricingWarningMessage()
    {
        if (Workbook is null || Workbook.AnalysisResult.PricingUnavailableFindings == 0)
        {
            return string.Empty;
        }

        var unavailableCategories = Workbook.AnalysisResult.PricingUnavailableByCategory
            .Select(entry => $"{entry.Key} ({entry.Value})")
            .ToList();

        var categorySummary = unavailableCategories.Count == 0
            ? string.Empty
            : $" Affected categories: {string.Join(", ", unavailableCategories)}.";

        return $"Pricing could not be matched for {Workbook.AnalysisResult.PricingUnavailableFindings} finding(s). Those rows stay in the report, but their estimated monthly cost remains $0.00 until pricing can be matched or retried.{categorySummary}";
    }

    protected sealed record AzureSqlResultRow(
        string Tier,
        string SourceWorksheet,
        string TotalDatabases,
        decimal MissingZoneRedundancy,
        decimal ZoneRedundancyCost,
        decimal MissingGeoReplication,
        decimal GeoReplicationCost,
        string CurrencyCode);
}
