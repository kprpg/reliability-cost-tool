using ReliabilityCostTool.Core.Common.Models;
using ReliabilityCostTool.Core.General.Interfaces;
using ReliabilityCostTool.Core.Utils;

namespace ReliabilityCostTool.Core.Compute;

public sealed class VmReliabilityRule : IReliabilityRule
{
    public string CategoryName => "Compute";

    public bool AppliesTo(AssessmentRecord record) => AssessmentHeuristics.MatchesResourceType(
        record,
        "virtual machine",
        "vm",
        "vmss",
        "scale set");

    public async Task<IReadOnlyList<ReliabilityFinding>> EvaluateAsync(
        AssessmentRecord record,
        IPriceCatalogClient priceCatalogClient,
        CancellationToken cancellationToken = default)
    {
        if (!AssessmentHeuristics.IndicatesReliabilityGap(record, ColumnAliases.ReliabilitySignals))
        {
            return [];
        }

        var instanceCount = AssessmentHeuristics.ExtractQuantity(record);
        const string serviceName = "Virtual Machines";
        var price = await priceCatalogClient.FindBestPriceAsync(
            serviceName,
            record.Region,
            record.Sku,
            null,
            cancellationToken);

        var estimatedMonthlyCost = (price?.UnitPrice ?? 0m) > 0m
            ? price!.UnitPrice * 730m * instanceCount
            : 0m;

        return
        [
            new ReliabilityFinding
            {
                ResourceCategory = "Compute",
                ResourceType = record.ResourceType,
                ResourceName = record.ResourceName,
                Region = record.Region,
                Sku = record.Sku,
                GapDescription = record.ReliabilityState ?? "VM is not zonally resilient or lacks redundancy.",
                Recommendation = "Add a second VM instance or move to a Flexible VM Scale Set across Availability Zones, then place the workload behind a zone-redundant load-balancing tier.",
                Quantity = instanceCount,
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
                    ? "No exact VM price match was returned from the Azure Retail Prices API. Cost shown as 0 until the workbook provides a stronger SKU/region match."
                    : "Assumes one additional VM instance per current instance is required to close the zonal resilience gap."
            }
        ];
    }
}
