using Microsoft.Extensions.Logging;
using ReliabilityCostTool.Core.Common.Models;
using ReliabilityCostTool.Core.General.Interfaces;
using ReliabilityCostTool.Core.Utils;

namespace ReliabilityCostTool.Core.Databases;

public sealed class DatabaseReliabilityRule(ILogger<DatabaseReliabilityRule> logger) : IReliabilityRule
{
    public string CategoryName => "Databases";

    public bool AppliesTo(AssessmentRecord record) =>
        record.GetValue(ColumnAliases.AzureSqlDbCount) is null &&
        AssessmentHeuristics.MatchesResourceType(
            record,
            "sql",
            "database",
            "postgres",
            "mysql",
            "cosmos");

    public async Task<IReadOnlyList<ReliabilityFinding>> EvaluateAsync(
        AssessmentRecord record,
        IPriceCatalogClient priceCatalogClient,
        CancellationToken cancellationToken = default)
    {
        if (!AssessmentHeuristics.IndicatesReliabilityGap(record, ColumnAliases.ReliabilitySignals))
        {
            logger.LogDebug("No reliability gap detected for database {ResourceName}, skipping", record.ResourceName);
            return [];
        }

        var serviceName = ResolveServiceName(record.ResourceType);
        logger.LogInformation("Evaluating database reliability for {ResourceName} (service={ServiceName}, region={Region})", record.ResourceName, serviceName, record.Region);
        var price = await priceCatalogClient.FindBestPriceAsync(
            serviceName,
            record.Region,
            record.Sku,
            null,
            cancellationToken);

        var estimatedMonthlyCost = (price?.UnitPrice ?? 0m) > 0m
            ? price!.UnitPrice * 730m
            : 0m;

        if (price is null)
        {
            logger.LogWarning("No price match found for database {ResourceName} (service={ServiceName}, region={Region})", record.ResourceName, serviceName, record.Region);
        }
        else
        {
            logger.LogInformation("Database {ResourceName} matched price {UnitPrice} {Currency}/hr, estimated monthly cost {MonthlyCost:C}", record.ResourceName, price.UnitPrice, price.CurrencyCode, estimatedMonthlyCost);
        }

        return
        [
            new ReliabilityFinding
            {
                ResourceCategory = "Databases",
                ResourceType = record.ResourceType,
                ResourceName = record.ResourceName,
                Region = record.Region,
                Sku = record.Sku,
                GapDescription = record.ReliabilityState ?? "Database platform lacks zone redundancy or high availability.",
                Recommendation = "Enable zone-redundant high availability, geo-redundant backup, or an equivalent managed failover capability for the database tier.",
                Quantity = 1m,
                EstimatedMonthlyCost = estimatedMonthlyCost,
                CurrencyCode = price?.CurrencyCode ?? "USD",
                PricingStatus = price is null ? "Unavailable" : "Matched",
                PriceQueryServiceName = serviceName,
                PriceQueryRegion = AssessmentHeuristics.NormalizeRegion(record.Region),
                PriceQuerySkuHint = record.Sku,
                MatchedProductName = price?.ProductName,
                MatchedSkuName = price?.SkuName,
                MatchedArmSkuName = price?.ArmSkuName,
                MatchedMeterName = price?.MeterName,
                MatchedUnitOfMeasure = price?.UnitOfMeasure,
                Confidence = price is null ? "Low" : "Medium",
                SourceWorksheet = record.SourceWorksheet,
                SourceRowNumber = record.RowNumber,
                Assumption = price is null
                    ? "No database price match was found from the Azure Retail Prices API. Cost shown as 0 pending a more exact service/SKU match."
                    : "Assumes database HA remediation approximately adds one additional hourly compute/storage footprint for the managed database service."
            }
        ];
    }

    private static string ResolveServiceName(string resourceType)
    {
        if (resourceType.Contains("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return "Azure Database for PostgreSQL";
        }

        if (resourceType.Contains("mysql", StringComparison.OrdinalIgnoreCase))
        {
            return "Azure Database for MySQL";
        }

        if (resourceType.Contains("cosmos", StringComparison.OrdinalIgnoreCase))
        {
            return "Azure Cosmos DB";
        }

        return "SQL Database";
    }
}
