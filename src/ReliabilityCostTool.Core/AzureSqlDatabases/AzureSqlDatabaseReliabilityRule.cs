using Microsoft.Extensions.Logging;
using ReliabilityCostTool.Core.Common.Models;
using ReliabilityCostTool.Core.General.Interfaces;
using ReliabilityCostTool.Core.Utils;

namespace ReliabilityCostTool.Core.AzureSqlDatabases;

public sealed class AzureSqlDatabaseReliabilityRule(ILogger<AzureSqlDatabaseReliabilityRule> logger) : IReliabilityRule
{
    public string CategoryName => "Databases";

    public bool AppliesTo(AssessmentRecord record) =>
        record.ResourceType.Contains("azure sql db", StringComparison.OrdinalIgnoreCase) ||
        record.GetValue(ColumnAliases.AzureSqlDbCount) is not null;

    public async Task<IReadOnlyList<ReliabilityFinding>> EvaluateAsync(
        AssessmentRecord record,
        IPriceCatalogClient priceCatalogClient,
        CancellationToken cancellationToken = default)
    {
        if (!TryReadCounts(record, out var totalDatabases, out var zoneRedundantDatabases, out var geoReplicatedDatabases))
        {
            logger.LogDebug("Could not read Azure SQL DB counts for {ResourceName}, skipping", record.ResourceName);
            return [];
        }

        logger.LogInformation("Evaluating Azure SQL DB reliability for {ResourceName}: total={TotalDbs}, zoneRedundant={ZoneDbs}, geoReplicated={GeoDbs}", record.ResourceName, totalDatabases, zoneRedundantDatabases, geoReplicatedDatabases);

        var findings = new List<ReliabilityFinding>();
        var nonZoneRedundantDatabases = Math.Max(0m, totalDatabases - zoneRedundantDatabases);
        var databasesWithoutGeoReplication = Math.Max(0m, totalDatabases - geoReplicatedDatabases);
        const string serviceName = "SQL Database";
        var price = await priceCatalogClient.FindBestPriceAsync(
            serviceName,
            record.Region,
            record.Sku,
            record.Sku,
            cancellationToken);
        var baseMonthlyCost = price is null ? 0m : price.UnitPrice * 730m;

        if (price is null)
        {
            logger.LogWarning("No price match for Azure SQL DB {ResourceName} (region={Region}, sku={Sku})", record.ResourceName, record.Region, record.Sku);
        }
        else
        {
            logger.LogInformation("Azure SQL DB {ResourceName} matched price {UnitPrice} {Currency}/hr, base monthly cost {BaseMonthlyCost:C}", record.ResourceName, price.UnitPrice, price.CurrencyCode, baseMonthlyCost);
        }

        if (nonZoneRedundantDatabases > 0m)
        {
            findings.Add(CreateFinding(
                record,
                nonZoneRedundantDatabases,
                baseMonthlyCost * 0.25m * nonZoneRedundantDatabases,
                price,
                serviceName,
                AssessmentHeuristics.NormalizeRegion(record.Region),
                record.Sku,
                record.Sku,
                price is null ? "Unavailable" : "Matched",
                price is null ? "Low" : "Medium",
                "Azure SQL DBs are not zone redundant.",
                "Enable zone redundancy for eligible Azure SQL Databases in this tier.",
                price is null
                    ? "Aggregate count derived from the Azure SQL DB resiliency summary table. Cost shown as 0 because no matching SQL Database tier price was found."
                    : "Aggregate count derived from the Azure SQL DB resiliency summary table. Zone redundancy cost is estimated as a 25% premium over the matched SQL Database tier price."));
        }

        if (databasesWithoutGeoReplication > 0m)
        {
            findings.Add(CreateFinding(
                record,
                databasesWithoutGeoReplication,
                baseMonthlyCost * databasesWithoutGeoReplication,
                price,
                serviceName,
                AssessmentHeuristics.NormalizeRegion(record.Region),
                record.Sku,
                record.Sku,
                price is null ? "Unavailable" : "Matched",
                price is null ? "Low" : "Medium",
                "Azure SQL DBs do not have geo-replication.",
                "Configure geo-replication or an equivalent cross-region failover strategy for Azure SQL Databases in this tier.",
                price is null
                    ? "Aggregate count derived from the Azure SQL DB resiliency summary table. Cost shown as 0 because no matching SQL Database tier price was found."
                    : "Aggregate count derived from the Azure SQL DB resiliency summary table. Geo-replication cost is estimated as one additional SQL Database of the same matched tier."));
        }

        return findings;
    }

    private static bool TryReadCounts(
        AssessmentRecord record,
        out decimal totalDatabases,
        out decimal zoneRedundantDatabases,
        out decimal geoReplicatedDatabases)
    {
        totalDatabases = 0m;
        zoneRedundantDatabases = 0m;
        geoReplicatedDatabases = 0m;

        if (!AssessmentHeuristics.TryParseDecimal(record.GetValue(ColumnAliases.AzureSqlDbCount), out totalDatabases))
        {
            return false;
        }

        AssessmentHeuristics.TryParseDecimal(record.GetValue(ColumnAliases.AzureSqlZoneRedundantDbCount), out zoneRedundantDatabases);
        AssessmentHeuristics.TryParseDecimal(record.GetValue(ColumnAliases.AzureSqlGeoReplicatedDbCount), out geoReplicatedDatabases);
        return true;
    }

    private static ReliabilityFinding CreateFinding(
        AssessmentRecord record,
        decimal quantity,
        decimal estimatedMonthlyCost,
        PriceCatalogItem? price,
        string priceQueryServiceName,
        string priceQueryRegion,
        string? priceQuerySkuHint,
        string? priceQueryMeterHint,
        string pricingStatus,
        string confidence,
        string gapDescription,
        string recommendation,
        string assumption) =>
        new()
        {
            ResourceCategory = "Databases",
            ResourceType = "Azure SQL DB",
            ResourceName = record.ResourceName,
            Region = record.Region,
            Sku = record.Sku,
            GapDescription = gapDescription,
            Recommendation = recommendation,
            Quantity = quantity,
            EstimatedMonthlyCost = estimatedMonthlyCost,
            CurrencyCode = price?.CurrencyCode ?? "USD",
            PricingStatus = pricingStatus,
            PriceQueryServiceName = priceQueryServiceName,
            PriceQueryRegion = priceQueryRegion,
            PriceQuerySkuHint = priceQuerySkuHint,
            PriceQueryMeterHint = priceQueryMeterHint,
            MatchedProductName = price?.ProductName,
            MatchedSkuName = price?.SkuName,
            MatchedArmSkuName = price?.ArmSkuName,
            MatchedMeterName = price?.MeterName,
            MatchedUnitOfMeasure = price?.UnitOfMeasure,
            Confidence = confidence,
            SourceWorksheet = record.SourceWorksheet,
            SourceRowNumber = record.RowNumber,
            Assumption = assumption
        };
}