using Microsoft.Extensions.Logging;
using ReliabilityCostTool.Core.Common.Models;
using ReliabilityCostTool.Core.General.Interfaces;
using ReliabilityCostTool.Core.Utils;

namespace ReliabilityCostTool.Core.Storage;

public sealed class StorageReliabilityRule(ILogger<StorageReliabilityRule> logger) : IReliabilityRule
{
    public string CategoryName => "Storage";

    public bool AppliesTo(AssessmentRecord record) => AssessmentHeuristics.MatchesResourceType(
        record,
        "storage",
        "blob",
        "file share",
        "managed disk",
        "disk");

    public async Task<IReadOnlyList<ReliabilityFinding>> EvaluateAsync(
        AssessmentRecord record,
        IPriceCatalogClient priceCatalogClient,
        CancellationToken cancellationToken = default)
    {
        if (!AssessmentHeuristics.IndicatesReliabilityGap(record, ColumnAliases.ReliabilitySignals))
        {
            logger.LogDebug("No reliability gap detected for storage {ResourceName}, skipping", record.ResourceName);
            return [];
        }

        logger.LogInformation("Evaluating storage reliability for {ResourceName} in {Region}", record.ResourceName, record.Region);
        var capacityGb = AssessmentHeuristics.ExtractCapacityGb(record) ?? 1m;
        const string serviceName = "Storage";
        const string meterHint = "ZRS";
        var price = await priceCatalogClient.FindBestPriceAsync(
            serviceName,
            record.Region,
            record.Sku,
            meterHint,
            cancellationToken);

        var estimatedMonthlyCost = (price?.UnitPrice ?? 0m) > 0m
            ? price!.UnitPrice * capacityGb
            : 0m;

        if (price is null)
        {
            logger.LogWarning("No price match found for storage {ResourceName} (service={ServiceName}, region={Region})", record.ResourceName, serviceName, record.Region);
        }
        else
        {
            logger.LogInformation("Storage {ResourceName} matched price {UnitPrice} {Currency}/GB, capacity {CapacityGb} GB, estimated monthly cost {MonthlyCost:C}", record.ResourceName, price.UnitPrice, price.CurrencyCode, capacityGb, estimatedMonthlyCost);
        }

        return
        [
            new ReliabilityFinding
            {
                ResourceCategory = "Storage",
                ResourceType = record.ResourceType,
                ResourceName = record.ResourceName,
                Region = record.Region,
                Sku = record.Sku,
                GapDescription = record.ReliabilityState ?? "Storage resource is not zone-redundant.",
                Recommendation = "Upgrade the storage redundancy option to ZRS or GZRS where supported, and validate application compatibility for synchronous zone replication.",
                Quantity = capacityGb,
                EstimatedMonthlyCost = estimatedMonthlyCost,
                CurrencyCode = price?.CurrencyCode ?? "USD",
                PricingStatus = price is null ? "Unavailable" : "Matched",
                PriceQueryServiceName = serviceName,
                PriceQueryRegion = AssessmentHeuristics.NormalizeRegion(record.Region),
                PriceQuerySkuHint = record.Sku,
                PriceQueryMeterHint = meterHint,
                MatchedProductName = price?.ProductName,
                MatchedSkuName = price?.SkuName,
                MatchedArmSkuName = price?.ArmSkuName,
                MatchedMeterName = price?.MeterName,
                MatchedUnitOfMeasure = price?.UnitOfMeasure,
                Confidence = price is null ? "Low" : "Medium",
                SourceWorksheet = record.SourceWorksheet,
                SourceRowNumber = record.RowNumber,
                Assumption = price is null
                    ? "No exact ZRS storage price was matched. Cost shown as 0 until a compatible SKU and region are available."
                    : capacityGb == 1m
                        ? "Capacity was not provided in the workbook. Cost is shown for 1 GB-month as a minimum placeholder."
                        : "Assumes Azure Retail Prices API returned a per-GB monthly price for the target redundancy tier."
            }
        ];
    }
}
