using System.Text;
using ClosedXML.Excel;
using ExcelDataReader;
using ReliabilityCostTool.Core.Common.Models;
using ReliabilityCostTool.Core.General.Interfaces;

namespace ReliabilityCostTool.Core.General.Reports;

public sealed class WorkbookReportBuilder : IWorkbookReportBuilder
{
    public Task<GeneratedWorkbook> BuildAsync(
        AnalysisResult analysisResult,
        byte[] sourceWorkbookContent,
        CancellationToken cancellationToken = default)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        using var workbook = BuildWorkbookWithSourceSheets(sourceWorkbookContent);
        BuildSummarySheet(workbook, analysisResult);
        BuildDetailSheet(workbook, analysisResult.Findings);

        foreach (var group in analysisResult.FindingsByCategory)
        {
            var resourceTypeCounts = analysisResult.CategoryResourceTypeCounts
                .GetValueOrDefault(group.Key) ?? (IReadOnlyDictionary<string, int>)new Dictionary<string, int>();
            BuildCategorySheet(workbook, group.Key, group.Value, resourceTypeCounts);
        }

        BuildPricingSummarySheet(workbook, analysisResult);
        BuildPricingDebugSheet(workbook, analysisResult.Findings);
        BuildAzureSqlDatabaseSheet(workbook, analysisResult);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var outputName = $"{Path.GetFileNameWithoutExtension(analysisResult.InputFileName)}-reliability-remediation-report.xlsx";
        return Task.FromResult(new GeneratedWorkbook
        {
            AnalysisResult = analysisResult,
            Content = stream.ToArray(),
            FileName = outputName
        });
    }

    private static XLWorkbook BuildWorkbookWithSourceSheets(byte[] sourceWorkbookContent)
    {
        var workbook = new XLWorkbook();
        using var input = new MemoryStream(sourceWorkbookContent);
        using var reader = ExcelReaderFactory.CreateReader(input);

        do
        {
            var worksheetName = GetAvailableSheetName(workbook, string.IsNullOrWhiteSpace(reader.Name) ? "Sheet" : reader.Name);
            var sheet = workbook.Worksheets.Add(worksheetName);
            var rowNumber = 1;

            while (reader.Read())
            {
                for (var columnIndex = 0; columnIndex < reader.FieldCount; columnIndex++)
                {
                    var value = reader.GetValue(columnIndex);
                    if (value is null)
                    {
                        continue;
                    }

                    sheet.Cell(rowNumber, columnIndex + 1).Value = value.ToString();
                }

                rowNumber++;
            }

            sheet.Columns().AdjustToContents();
        }
        while (reader.NextResult());

        return workbook;
    }

    private static void BuildSummarySheet(XLWorkbook workbook, AnalysisResult analysisResult)
    {
        var sheet = workbook.Worksheets.Add(GetAvailableSheetName(workbook, "Summary"));
        sheet.Cell(1, 1).Value = "Reliability Remediation Cost Summary";
        sheet.Cell(2, 1).Value = "Input workbook";
        sheet.Cell(2, 2).Value = analysisResult.InputFileName;
        sheet.Cell(3, 1).Value = "Rows analyzed";
        sheet.Cell(3, 2).Value = analysisResult.Records.Count;
        sheet.Cell(4, 1).Value = "Findings";
        sheet.Cell(4, 2).Value = analysisResult.TotalFindings;
        sheet.Cell(5, 1).Value = "Estimated monthly remediation cost";
        sheet.Cell(5, 2).Value = analysisResult.TotalEstimatedMonthlyCost;
        sheet.Cell(5, 2).Style.NumberFormat.Format = "$#,##0.00";
        sheet.Cell(6, 1).Value = "Findings with matched pricing";
        sheet.Cell(6, 2).Value = analysisResult.PricingMatchedFindings;
        sheet.Cell(7, 1).Value = "Findings with unavailable pricing";
        sheet.Cell(7, 2).Value = analysisResult.PricingUnavailableFindings;

        sheet.Cell(9, 1).Value = "Category";
        sheet.Cell(9, 2).Value = "Total Resources";
        sheet.Cell(9, 3).Value = "Needing Remediation";
        sheet.Cell(9, 4).Value = "Price / Remediation";
        sheet.Cell(9, 5).Value = "Estimated Monthly Cost";

        var row = 10;
        foreach (var group in analysisResult.FindingsByCategory)
        {
            var remediationCount = group.Value.Count;
            var totalCost = group.Value.Sum(item => item.EstimatedMonthlyCost);
            var totalResources = analysisResult.CategoryResourceTypeCounts
                .GetValueOrDefault(group.Key)?.Values.Sum() ?? remediationCount;
            var pricePerRemediation = remediationCount > 0 ? totalCost / remediationCount : 0m;

            sheet.Cell(row, 1).Value = group.Key;
            sheet.Cell(row, 2).Value = totalResources;
            sheet.Cell(row, 3).Value = remediationCount;
            sheet.Cell(row, 4).Value = pricePerRemediation;
            sheet.Cell(row, 4).Style.NumberFormat.Format = "$#,##0.00";
            sheet.Cell(row, 5).Value = totalCost;
            sheet.Cell(row, 5).Style.NumberFormat.Format = "$#,##0.00";
            row++;
        }

        StyleSheet(sheet, 9, 5);
    }

    private static void BuildPricingSummarySheet(XLWorkbook workbook, AnalysisResult analysisResult)
    {
        var sheet = workbook.Worksheets.Add(GetAvailableSheetName(workbook, "Pricing Summary"));
        sheet.Cell(1, 1).Value = "Pricing Availability Summary";
        sheet.Cell(2, 1).Value = "Metric";
        sheet.Cell(2, 2).Value = "Value";
        sheet.Cell(3, 1).Value = "Findings with matched pricing";
        sheet.Cell(3, 2).Value = analysisResult.PricingMatchedFindings;
        sheet.Cell(4, 1).Value = "Findings with unavailable pricing";
        sheet.Cell(4, 2).Value = analysisResult.PricingUnavailableFindings;

        sheet.Cell(6, 1).Value = "Category";
        sheet.Cell(6, 2).Value = "Unavailable Pricing Findings";

        var row = 7;
        foreach (var category in analysisResult.PricingUnavailableByCategory)
        {
            sheet.Cell(row, 1).Value = category.Key;
            sheet.Cell(row, 2).Value = category.Value;
            row++;
        }

        StyleSheet(sheet, 2, 2);
        StyleSheet(sheet, 6, 2);
    }

    private static void BuildDetailSheet(XLWorkbook workbook, IReadOnlyList<ReliabilityFinding> findings)
    {
        var sheet = workbook.Worksheets.Add(GetAvailableSheetName(workbook, "All Findings"));
        WriteFindingTable(sheet, findings);
    }

    private static void BuildPricingDebugSheet(XLWorkbook workbook, IReadOnlyList<ReliabilityFinding> findings)
    {
        var sheet = workbook.Worksheets.Add(GetAvailableSheetName(workbook, "Pricing Debug"));
        var headers = new[]
        {
            "Resource",
            "Category",
            "Type",
            "Pricing Status",
            "Query Service",
            "Query Region",
            "Query SKU Hint",
            "Query Meter Hint",
            "Matched Product",
            "Matched SKU",
            "Matched Arm SKU",
            "Matched Meter",
            "Matched Unit Of Measure",
            "Estimated Monthly Cost",
            "Currency",
            "Sheet",
            "Row",
            "Assumption"
        };

        for (var column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
        }

        var row = 2;
        foreach (var finding in findings)
        {
            sheet.Cell(row, 1).Value = finding.ResourceName;
            sheet.Cell(row, 2).Value = finding.ResourceCategory;
            sheet.Cell(row, 3).Value = finding.ResourceType;
            sheet.Cell(row, 4).Value = finding.PricingStatus;
            sheet.Cell(row, 5).Value = finding.PriceQueryServiceName;
            sheet.Cell(row, 6).Value = finding.PriceQueryRegion;
            sheet.Cell(row, 7).Value = finding.PriceQuerySkuHint;
            sheet.Cell(row, 8).Value = finding.PriceQueryMeterHint;
            sheet.Cell(row, 9).Value = finding.MatchedProductName;
            sheet.Cell(row, 10).Value = finding.MatchedSkuName;
            sheet.Cell(row, 11).Value = finding.MatchedArmSkuName;
            sheet.Cell(row, 12).Value = finding.MatchedMeterName;
            sheet.Cell(row, 13).Value = finding.MatchedUnitOfMeasure;
            sheet.Cell(row, 14).Value = finding.EstimatedMonthlyCost;
            sheet.Cell(row, 14).Style.NumberFormat.Format = "$#,##0.00";
            sheet.Cell(row, 15).Value = finding.CurrencyCode;
            sheet.Cell(row, 16).Value = finding.SourceWorksheet;
            sheet.Cell(row, 17).Value = finding.SourceRowNumber;
            sheet.Cell(row, 18).Value = finding.Assumption;
            row++;
        }

        StyleSheet(sheet, 1, headers.Length);
    }

    private static void BuildCategorySheet(
        XLWorkbook workbook,
        string category,
        IReadOnlyList<ReliabilityFinding> findings,
        IReadOnlyDictionary<string, int> resourceTypeCounts)
    {
        var safeName = category.Length <= 31 ? category : category[..31];
        var sheet = workbook.Worksheets.Add(GetAvailableSheetName(workbook, safeName));

        sheet.Cell(1, 1).Value = $"{category} – Remediation Summary";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;

        var totalResources = resourceTypeCounts.Values.Sum();
        if (totalResources == 0)
        {
            totalResources = findings.Count;
        }

        sheet.Cell(3, 1).Value = "Total Resources";
        sheet.Cell(3, 2).Value = totalResources;
        sheet.Cell(4, 1).Value = "Needing Remediation";
        sheet.Cell(4, 2).Value = findings.Count;
        sheet.Cell(5, 1).Value = "Total Estimated Monthly Cost";
        sheet.Cell(5, 2).Value = findings.Sum(f => f.EstimatedMonthlyCost);
        sheet.Cell(5, 2).Style.NumberFormat.Format = "$#,##0.00";

        var headers = new[] { "Resource Type", "Total Count", "Needing Remediation", "Price / Remediation (Monthly)", "Total Monthly Cost" };
        const int headerRow = 7;

        for (var col = 0; col < headers.Length; col++)
        {
            sheet.Cell(headerRow, col + 1).Value = headers[col];
        }

        var groups = findings
            .GroupBy(f => f.ResourceType, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var row = headerRow + 1;

        foreach (var group in groups)
        {
            var groupFindings = group.ToList();
            var totalCount = resourceTypeCounts.GetValueOrDefault(group.Key, groupFindings.Count);
            var remediationCount = groupFindings.Count;
            var totalCost = groupFindings.Sum(f => f.EstimatedMonthlyCost);
            var pricePerRemediation = remediationCount > 0 ? totalCost / remediationCount : 0m;

            sheet.Cell(row, 1).Value = group.Key;
            sheet.Cell(row, 2).Value = totalCount;
            sheet.Cell(row, 3).Value = remediationCount;
            sheet.Cell(row, 4).Value = pricePerRemediation;
            sheet.Cell(row, 4).Style.NumberFormat.Format = "$#,##0.00";
            sheet.Cell(row, 5).Value = totalCost;
            sheet.Cell(row, 5).Style.NumberFormat.Format = "$#,##0.00";
            row++;
        }

        StyleSheet(sheet, headerRow, headers.Length);
    }

    private static void BuildAzureSqlDatabaseSheet(XLWorkbook workbook, AnalysisResult analysisResult)
    {
        var azureSqlRecords = analysisResult.Records
            .Where(record => string.Equals(record.ResourceType, "Azure SQL DB", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (azureSqlRecords.Count == 0)
        {
            return;
        }

        var azureSqlFindings = analysisResult.Findings
            .Where(finding => string.Equals(finding.ResourceType, "Azure SQL DB", StringComparison.OrdinalIgnoreCase))
            .ToLookup(finding => (finding.ResourceName, finding.SourceWorksheet, finding.SourceRowNumber));

        var sheet = workbook.Worksheets.Add(GetAvailableSheetName(workbook, "Azure SQL DB"));
        var headers = new[]
        {
            "Tier",
            "Source Sheet",
            "Source Row",
            "Total DBs",
            "Zone Redundant DBs",
            "DBs Without Zone Redundancy",
            "Zone Redundancy Estimated Monthly Cost",
            "Geo-Replicated DBs",
            "DBs Without Geo-Replication",
            "Geo-Replication Estimated Monthly Cost",
            "Currency",
            "Assumption"
        };

        for (var column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
        }

        var row = 2;
        foreach (var record in azureSqlRecords)
        {
            var totalDatabases = ParseDecimal(record.GetValue(Utils.ColumnAliases.AzureSqlDbCount));
            var zoneRedundantDatabases = ParseDecimal(record.GetValue(Utils.ColumnAliases.AzureSqlZoneRedundantDbCount));
            var geoReplicatedDatabases = ParseDecimal(record.GetValue(Utils.ColumnAliases.AzureSqlGeoReplicatedDbCount));
            var groupedFindings = azureSqlFindings[(record.ResourceName, record.SourceWorksheet, record.RowNumber)].ToList();
            var zoneFinding = groupedFindings.FirstOrDefault(finding => finding.GapDescription.Contains("zone redundant", StringComparison.OrdinalIgnoreCase));
            var geoFinding = groupedFindings.FirstOrDefault(finding => finding.GapDescription.Contains("geo-replication", StringComparison.OrdinalIgnoreCase));

            sheet.Cell(row, 1).Value = record.ResourceName;
            sheet.Cell(row, 2).Value = record.SourceWorksheet;
            sheet.Cell(row, 3).Value = record.RowNumber;
            sheet.Cell(row, 4).Value = totalDatabases;
            sheet.Cell(row, 5).Value = zoneRedundantDatabases;
            sheet.Cell(row, 6).Value = zoneFinding?.Quantity ?? Math.Max(0m, totalDatabases - zoneRedundantDatabases);
            sheet.Cell(row, 7).Value = zoneFinding?.EstimatedMonthlyCost ?? 0m;
            sheet.Cell(row, 7).Style.NumberFormat.Format = "$#,##0.00";
            sheet.Cell(row, 8).Value = geoReplicatedDatabases;
            sheet.Cell(row, 9).Value = geoFinding?.Quantity ?? Math.Max(0m, totalDatabases - geoReplicatedDatabases);
            sheet.Cell(row, 10).Value = geoFinding?.EstimatedMonthlyCost ?? 0m;
            sheet.Cell(row, 10).Style.NumberFormat.Format = "$#,##0.00";
            sheet.Cell(row, 11).Value = zoneFinding?.CurrencyCode ?? geoFinding?.CurrencyCode ?? "USD";
            sheet.Cell(row, 12).Value = string.Join(
                " ",
                groupedFindings
                    .Select(finding => finding.Assumption)
                    .Where(assumption => !string.IsNullOrWhiteSpace(assumption))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            row++;
        }

        StyleSheet(sheet, 1, headers.Length);
    }

    private static void WriteFindingTable(IXLWorksheet sheet, IReadOnlyList<ReliabilityFinding> findings)
    {
        var headers = new[]
        {
            "Resource",
            "Type",
            "Region",
            "SKU",
            "Gap",
            "Recommendation",
            "Quantity",
            "Estimated Monthly Cost",
            "Currency",
            "Pricing Status",
            "Confidence",
            "Sheet",
            "Row",
            "Assumption"
        };

        for (var column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
        }

        var row = 2;
        foreach (var finding in findings)
        {
            sheet.Cell(row, 1).Value = finding.ResourceName;
            sheet.Cell(row, 2).Value = finding.ResourceType;
            sheet.Cell(row, 3).Value = finding.Region;
            sheet.Cell(row, 4).Value = finding.Sku;
            sheet.Cell(row, 5).Value = finding.GapDescription;
            sheet.Cell(row, 6).Value = finding.Recommendation;
            sheet.Cell(row, 7).Value = finding.Quantity;
            sheet.Cell(row, 8).Value = finding.EstimatedMonthlyCost;
            sheet.Cell(row, 8).Style.NumberFormat.Format = "$#,##0.00";
            sheet.Cell(row, 9).Value = finding.CurrencyCode;
            sheet.Cell(row, 10).Value = finding.PricingStatus;
            sheet.Cell(row, 11).Value = finding.Confidence;
            sheet.Cell(row, 12).Value = finding.SourceWorksheet;
            sheet.Cell(row, 13).Value = finding.SourceRowNumber;
            sheet.Cell(row, 14).Value = finding.Assumption;
            row++;
        }

        StyleSheet(sheet, 1, headers.Length);
    }

    private static void StyleSheet(IXLWorksheet sheet, int headerRow, int columnCount)
    {
        var headerRange = sheet.Range(headerRow, 1, headerRow, columnCount);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#E6EEF5");
        sheet.SheetView.FreezeRows(headerRow);
        sheet.Columns().AdjustToContents();
    }

    private static string GetAvailableSheetName(XLWorkbook workbook, string baseName)
    {
        var safeBaseName = baseName.Length <= 31 ? baseName : baseName[..31];
        var candidate = safeBaseName;
        var index = 2;

        while (workbook.Worksheets.Any(sheet => string.Equals(sheet.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            var suffix = $" {index}";
            var prefixLength = Math.Min(safeBaseName.Length, 31 - suffix.Length);
            candidate = safeBaseName[..prefixLength] + suffix;
            index++;
        }

        return candidate;
    }

    private static decimal ParseDecimal(string? value) =>
        decimal.TryParse(value, out var parsedValue)
            ? parsedValue
            : 0m;
}
