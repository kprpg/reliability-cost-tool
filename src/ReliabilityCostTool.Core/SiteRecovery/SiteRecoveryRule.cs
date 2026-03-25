using Microsoft.Extensions.Logging;
using ReliabilityCostTool.Core.Common.Models;
using ReliabilityCostTool.Core.General.Interfaces;
using ReliabilityCostTool.Core.Utils;

namespace ReliabilityCostTool.Core.SiteRecovery;

public sealed class SiteRecoveryRule(ILogger<SiteRecoveryRule> logger) : IReliabilityRule
{
    public string CategoryName => "Site Recovery";

    public bool AppliesTo(AssessmentRecord record) =>
        record.FlattenedText().Contains("site recovery", StringComparison.OrdinalIgnoreCase) ||
        record.FlattenedText().Contains("asr", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<ReliabilityFinding>> EvaluateAsync(
        AssessmentRecord record,
        IPriceCatalogClient priceCatalogClient,
        CancellationToken cancellationToken = default)
    {
        if (!AssessmentHeuristics.IndicatesReliabilityGap(record, ColumnAliases.ReliabilitySignals))
        {
            logger.LogDebug("No reliability gap detected for site recovery {ResourceName}, skipping", record.ResourceName);
            return [];
        }

        logger.LogInformation("Evaluating site recovery for {ResourceName} in {Region}", record.ResourceName, record.Region);
        var quantity = AssessmentHeuristics.ExtractQuantity(record);
        const string serviceName = "Azure Site Recovery";
        var price = await priceCatalogClient.FindBestPriceAsync(
            serviceName,
            record.Region,
            null,
            null,
            cancellationToken);

        var estimatedMonthlyCost = (price?.UnitPrice ?? 0m) > 0m
            ? price!.UnitPrice * quantity
            : 0m;

        if (price is null)
        {
            logger.LogWarning("No price match found for site recovery {ResourceName} (region={Region})", record.ResourceName, record.Region);
        }
        else
        {
            logger.LogInformation("Site recovery {ResourceName} matched price {UnitPrice} {Currency}/instance, estimated monthly cost {MonthlyCost:C}", record.ResourceName, price.UnitPrice, price.CurrencyCode, estimatedMonthlyCost);
        }

        return
        [
            new ReliabilityFinding
            {
                ResourceCategory = "Site Recovery",
                ResourceType = record.ResourceType,
                ResourceName = record.ResourceName,
                Region = record.Region,
                Sku = record.Sku,
                GapDescription = record.ReliabilityState ?? "Disaster recovery orchestration is not configured.",
                Recommendation = "Enable Azure Site Recovery replication and failover plans for workloads that need cross-region disaster recovery beyond zonal resilience.",
                Quantity = quantity,
                EstimatedMonthlyCost = estimatedMonthlyCost,
                CurrencyCode = price?.CurrencyCode ?? "USD",
                PricingStatus = price is null ? "Unavailable" : "Matched",
                PriceQueryServiceName = serviceName,
                PriceQueryRegion = AssessmentHeuristics.NormalizeRegion(record.Region),
                MatchedProductName = price?.ProductName,
                MatchedSkuName = price?.SkuName,
                MatchedArmSkuName = price?.ArmSkuName,
                MatchedMeterName = price?.MeterName,
                MatchedUnitOfMeasure = price?.UnitOfMeasure,
                Confidence = price is null ? "Low" : "Low",
                SourceWorksheet = record.SourceWorksheet,
                SourceRowNumber = record.RowNumber,
                Assumption = price is null
                    ? "Site Recovery finding identified successfully, but Azure Retail Prices API did not return a usable price in time. Cost is shown as 0 until pricing can be matched or retried."
                    : "Assumes one protected instance charge per workload instance where cross-region recovery is required."
            }
        ];
    }
}
