using Microsoft.Extensions.Logging;
using ReliabilityCostTool.Core.Common.Models;
using ReliabilityCostTool.Core.General.Interfaces;
using ReliabilityCostTool.Core.Utils;

namespace ReliabilityCostTool.Core.Backup;

public sealed class BackupReliabilityRule(ILogger<BackupReliabilityRule> logger) : IReliabilityRule
{
    public string CategoryName => "Backup";

    public bool AppliesTo(AssessmentRecord record) =>
        record.FlattenedText().Contains("backup", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<ReliabilityFinding>> EvaluateAsync(
        AssessmentRecord record,
        IPriceCatalogClient priceCatalogClient,
        CancellationToken cancellationToken = default)
    {
        if (!AssessmentHeuristics.IndicatesReliabilityGap(record, ColumnAliases.ReliabilitySignals))
        {
            logger.LogDebug("No reliability gap detected for backup {ResourceName}, skipping", record.ResourceName);
            return [];
        }

        logger.LogInformation("Evaluating backup reliability for {ResourceName} in {Region}", record.ResourceName, record.Region);
        var capacityGb = AssessmentHeuristics.ExtractCapacityGb(record) ?? 100m;
        const string serviceName = "Backup";
        var price = await priceCatalogClient.FindBestPriceAsync(
            serviceName,
            record.Region,
            null,
            null,
            cancellationToken);

        var estimatedMonthlyCost = (price?.UnitPrice ?? 0m) > 0m
            ? price!.UnitPrice * capacityGb
            : 0m;

        if (price is null)
        {
            logger.LogWarning("No price match found for backup {ResourceName} (region={Region})", record.ResourceName, record.Region);
        }
        else
        {
            logger.LogInformation("Backup {ResourceName} matched price {UnitPrice} {Currency}/GB, capacity {CapacityGb} GB, estimated monthly cost {MonthlyCost:C}", record.ResourceName, price.UnitPrice, price.CurrencyCode, capacityGb, estimatedMonthlyCost);
        }

        return
        [
            new ReliabilityFinding
            {
                ResourceCategory = "Backup",
                ResourceType = record.ResourceType,
                ResourceName = record.ResourceName,
                Region = record.Region,
                Sku = record.Sku,
                GapDescription = record.ReliabilityState ?? "Backup coverage is missing or insufficient.",
                Recommendation = "Enable Azure Backup policies with cross-zone or geo-redundant vault settings where supported and align retention to recovery objectives.",
                Quantity = capacityGb,
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
                    ? "Backup finding identified successfully, but Azure Retail Prices API did not return a usable price in time. Cost is shown as 0 until pricing can be matched or retried."
                    : "Assumes a backup-protected capacity baseline. This category is included for completeness and should be refined with actual protected-instance sizing."
            }
        ];
    }
}
