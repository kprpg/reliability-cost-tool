using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;
using ReliabilityCostTool.Core.AzureSqlDatabases;
using ReliabilityCostTool.Core.Backup;
using ReliabilityCostTool.Core.Common.Models;
using ReliabilityCostTool.Core.Compute;
using ReliabilityCostTool.Core.Databases;
using ReliabilityCostTool.Core.General.Interfaces;
using ReliabilityCostTool.Core.General.Pricing;
using ReliabilityCostTool.Core.General.Reports;
using ReliabilityCostTool.Core.General.Services;
using ReliabilityCostTool.Core.General.Workbook;
using ReliabilityCostTool.Core.SiteRecovery;
using ReliabilityCostTool.Core.Storage;
using ReliabilityCostTool.Core.Utils;

namespace ReliabilityCostTool.Core.Tests;

public class ReliabilityAssessmentTests
{
    [Fact]
    public async Task AnalyzeAsync_GeneratesWorkbookWithCategorySheetsAndFindings()
    {
        await using var input = BuildWorkbook(
            ("resource name", "resource type", "region", "sku", "instance count", "capacity gb", "reliability"),
            ("vm-prod-01", "Virtual Machine", "East US", "Standard_D2s_v5", "2", "", "Not zonally redundant"),
            ("stshared01", "Storage Account", "East US", "Standard_LRS", "", "512", "LRS only"),
            ("sqldb-01", "SQL Database", "East US", "GP_Gen5_2", "", "", "High availability not enabled"));

        var service = CreateService();

        var workbook = await service.AnalyzeAsync(input, "assessment.xlsx");

        Assert.Equal(3, workbook.AnalysisResult.Records.Count);
        Assert.Equal(3, workbook.AnalysisResult.TotalFindings);
        Assert.Contains(workbook.AnalysisResult.FindingsByCategory.Keys, key => key == "Compute");
        Assert.Contains(workbook.AnalysisResult.FindingsByCategory.Keys, key => key == "Storage");
        Assert.Contains(workbook.AnalysisResult.FindingsByCategory.Keys, key => key == "Databases");
        Assert.True(workbook.AnalysisResult.TotalEstimatedMonthlyCost > 0m);

        using var output = new XLWorkbook(new MemoryStream(workbook.Content));
        Assert.NotNull(output.Worksheet("Assessment"));
        Assert.NotNull(output.Worksheet("Summary"));
        Assert.NotNull(output.Worksheet("All Findings"));
        Assert.NotNull(output.Worksheet("Pricing Summary"));
        Assert.NotNull(output.Worksheet("Pricing Debug"));
        Assert.NotNull(output.Worksheet("Compute"));
        Assert.NotNull(output.Worksheet("Storage"));
        Assert.NotNull(output.Worksheet("Databases"));

        var allFindings = output.Worksheet("All Findings");
        Assert.Equal("Pricing Status", allFindings.Cell(1, 10).GetString());

        var pricingDebug = output.Worksheet("Pricing Debug");
        Assert.Equal("Query Service", pricingDebug.Cell(1, 5).GetString());
    }

    [Fact]
    public async Task AnalyzeAsync_UsesAliasMatchingForWorkbookHeaders()
    {
        await using var input = BuildWorkbook(
            ("name", "type", "location", "size", "count", "notes"),
            ("vm-alt-01", "VM", "East US", "Standard_D2s_v5", "1", "single zone and not resilient"));

        var service = CreateService();

        var workbook = await service.AnalyzeAsync(input, "alias-input.xlsx");
        var finding = Assert.Single(workbook.AnalysisResult.Findings);

        Assert.Equal("vm-alt-01", finding.ResourceName);
        Assert.Equal("Compute", finding.ResourceCategory);
        Assert.Contains("Scale Set", finding.Recommendation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ParseAsync_ExtractsAzureSqlDatabaseSummaryRowsFromEmbeddedResiliencyTable()
    {
        await using var input = BuildAzureSqlSummaryWorkbook(
            ("General Purpose", "5", "2", "1"),
            ("Business Critical", "3", "3", "3"));

        var parser = new AssessmentWorkbookParser(NullLogger<AssessmentWorkbookParser>.Instance);

        var records = await parser.ParseAsync(input);
        var azureSqlRecords = records
            .Where(record => record.ResourceType.Equals("Azure SQL DB", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal(2, azureSqlRecords.Count);
        Assert.Equal("General Purpose", azureSqlRecords[0].ResourceName);
        Assert.Equal("5", azureSqlRecords[0].GetValue(ColumnAliases.AzureSqlDbCount));
        Assert.Equal("2", azureSqlRecords[0].GetValue(ColumnAliases.AzureSqlZoneRedundantDbCount));
        Assert.Equal("1", azureSqlRecords[0].GetValue(ColumnAliases.AzureSqlGeoReplicatedDbCount));
    }

    [Fact]
    public async Task AnalyzeAsync_CreatesAzureSqlFindingsForMissingZoneAndGeoReplicationCounts()
    {
        await using var input = BuildAzureSqlSummaryWorkbook(
            ("General Purpose", "5", "2", "1"),
            ("Business Critical", "3", "3", "3"));

        var service = CreateService();

        var workbook = await service.AnalyzeAsync(input, "azure-sql-summary.xlsx");
        var findings = workbook.AnalysisResult.Findings
            .Where(finding => finding.ResourceType == "Azure SQL DB")
            .ToList();

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, finding =>
            finding.GapDescription == "Azure SQL DBs do not have geo-replication." &&
            finding.Quantity == 4m &&
            finding.EstimatedMonthlyCost > 0m);
        Assert.Contains(findings, finding =>
            finding.GapDescription == "Azure SQL DBs are not zone redundant." &&
            finding.Quantity == 3m &&
            finding.EstimatedMonthlyCost > 0m);

        using var output = new XLWorkbook(new MemoryStream(workbook.Content));
        var azureSqlSheet = output.Worksheet("Azure SQL DB");
        var pricingSummarySheet = output.Worksheet("Pricing Summary");
        Assert.Equal("Tier", azureSqlSheet.Cell(1, 1).GetString());
        Assert.Equal("Pricing Availability Summary", pricingSummarySheet.Cell(1, 1).GetString());
        Assert.Equal("General Purpose", azureSqlSheet.Cell(2, 1).GetString());
        Assert.Equal(5m, azureSqlSheet.Cell(2, 4).GetValue<decimal>());
        Assert.Equal(3m, azureSqlSheet.Cell(2, 6).GetValue<decimal>());
        Assert.Equal(4m, azureSqlSheet.Cell(2, 9).GetValue<decimal>());
        Assert.True(azureSqlSheet.Cell(2, 7).GetValue<decimal>() > 0m);
        Assert.True(azureSqlSheet.Cell(2, 10).GetValue<decimal>() > 0m);
    }

    [Fact]
    public async Task FindBestPriceAsync_ReturnsNullWhenRetailPriceLookupTimesOut()
    {
        using var httpClient = new HttpClient(new TimeoutMessageHandler())
        {
            BaseAddress = new Uri("https://prices.azure.com")
        };

        var client = new AzureRetailPriceClient(httpClient, NullLogger<AzureRetailPriceClient>.Instance);

        var result = await client.FindBestPriceAsync("Virtual Machines", "East US", "Standard_D2s_v5", null);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindBestPriceAsync_UsesNormalizedSkuMatchingWhenBroadQueryIsNeeded()
    {
        const string response = """
        {
          "Items": [
            {
              "serviceName": "SQL Database",
              "productName": "Business Critical - Gen5",
              "skuName": "BC Gen5",
              "armSkuName": "BC_Gen5_2",
              "armRegionName": "eastus",
              "meterName": "2 vCore",
              "retailPrice": 1.20,
              "currencyCode": "USD",
              "unitOfMeasure": "1 Hour",
              "priceType": "Consumption"
            },
            {
              "serviceName": "SQL Database",
              "productName": "General Purpose - Gen5",
              "skuName": "General Purpose Gen5",
              "armSkuName": "GP_Gen5_2",
              "armRegionName": "eastus",
              "meterName": "2 vCore",
              "retailPrice": 0.40,
              "currencyCode": "USD",
              "unitOfMeasure": "1 Hour",
              "priceType": "Consumption"
            }
          ],
          "NextPageLink": null
        }
        """;

        using var httpClient = new HttpClient(new StubPricingMessageHandler(request =>
        {
            var query = request.RequestUri?.Query ?? string.Empty;
            if (query.Contains("armSkuName%20eq%20'GP_Gen5_2'", StringComparison.OrdinalIgnoreCase) ||
                query.Contains("armSkuName eq 'GP_Gen5_2'", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"Items\":[],\"NextPageLink\":null}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }))
        {
            BaseAddress = new Uri("https://prices.azure.com")
        };

        var client = new AzureRetailPriceClient(httpClient, NullLogger<AzureRetailPriceClient>.Instance);

        var result = await client.FindBestPriceAsync("SQL Database", "East US", "GP_Gen5_2", null);

        Assert.NotNull(result);
        Assert.Equal("GP_Gen5_2", result!.ArmSkuName);
        Assert.Equal(0.40m, result.UnitPrice);
    }

    private static IReliabilityAssessmentService CreateService()
    {
        return new ReliabilityAssessmentService(
            new AssessmentWorkbookParser(NullLogger<AssessmentWorkbookParser>.Instance),
            [
                new VmReliabilityRule(NullLogger<VmReliabilityRule>.Instance),
                new StorageReliabilityRule(NullLogger<StorageReliabilityRule>.Instance),
                new DatabaseReliabilityRule(NullLogger<DatabaseReliabilityRule>.Instance),
                new AzureSqlDatabaseReliabilityRule(NullLogger<AzureSqlDatabaseReliabilityRule>.Instance),
                new BackupReliabilityRule(NullLogger<BackupReliabilityRule>.Instance),
                new SiteRecoveryRule(NullLogger<SiteRecoveryRule>.Instance)
            ],
            new FakePriceCatalogClient(),
            new WorkbookReportBuilder(NullLogger<WorkbookReportBuilder>.Instance),
            NullLogger<ReliabilityAssessmentService>.Instance);
    }

    private static MemoryStream BuildWorkbook((string, string, string, string, string, string, string) headers, params (string, string, string, string, string, string, string)[] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Assessment");

        WriteRow(sheet, 1, headers.Item1, headers.Item2, headers.Item3, headers.Item4, headers.Item5, headers.Item6, headers.Item7);

        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            WriteRow(sheet, index + 2, row.Item1, row.Item2, row.Item3, row.Item4, row.Item5, row.Item6, row.Item7);
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream BuildWorkbook((string, string, string, string, string, string) headers, params (string, string, string, string, string, string)[] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Assessment");

        WriteRow(sheet, 1, headers.Item1, headers.Item2, headers.Item3, headers.Item4, headers.Item5, headers.Item6);

        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            WriteRow(sheet, index + 2, row.Item1, row.Item2, row.Item3, row.Item4, row.Item5, row.Item6);
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static void WriteRow(IXLWorksheet sheet, int rowNumber, params string[] values)
    {
        for (var column = 0; column < values.Length; column++)
        {
            sheet.Cell(rowNumber, column + 1).Value = values[column];
        }
    }

    private static MemoryStream BuildAzureSqlSummaryWorkbook(params (string Tier, string Total, string ZoneRedundant, string GeoReplicated)[] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Assessment");

        sheet.Cell(1, 1).Value = "Azure SQL DB REsiliency";
        sheet.Cell(3, 1).Value = "Azure SQL DB by Tier";
        sheet.Cell(3, 2).Value = "#Of DBs";
        sheet.Cell(3, 3).Value = "#Zone Redundant Azure SQL DBs";
        sheet.Cell(3, 4).Value = "#Of DBs with Geo-Rep";

        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            sheet.Cell(index + 4, 1).Value = row.Tier;
            sheet.Cell(index + 4, 2).Value = row.Total;
            sheet.Cell(index + 4, 3).Value = row.ZoneRedundant;
            sheet.Cell(index + 4, 4).Value = row.GeoReplicated;
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private sealed class FakePriceCatalogClient : IPriceCatalogClient
    {
        public Task<PriceCatalogItem?> FindBestPriceAsync(
            string serviceName,
            string? region,
            string? armSkuName,
            string? meterHint,
            CancellationToken cancellationToken = default)
        {
            var unitPrice = serviceName switch
            {
                "Virtual Machines" => 0.25m,
                "Storage" => 0.02m,
                "SQL Database" => 0.40m,
                "Azure Database for PostgreSQL" => 0.35m,
                "Azure Database for MySQL" => 0.33m,
                "Azure Cosmos DB" => 0.50m,
                "Backup" => 0.03m,
                "Azure Site Recovery" => 25m,
                _ => 0.10m
            };

            return Task.FromResult<PriceCatalogItem?>(new PriceCatalogItem
            {
                ServiceName = serviceName,
                ArmRegionName = region,
                ArmSkuName = armSkuName,
                MeterName = meterHint,
                UnitPrice = unitPrice,
                CurrencyCode = "USD",
                UnitOfMeasure = "1 Hour"
            });
        }
    }

    private sealed class TimeoutMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private sealed class StubPricingMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
