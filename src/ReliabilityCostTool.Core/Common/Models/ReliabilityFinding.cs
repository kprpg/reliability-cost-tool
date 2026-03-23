namespace ReliabilityCostTool.Core.Common.Models;

public sealed class ReliabilityFinding
{
    public required string ResourceCategory { get; init; }

    public required string ResourceType { get; init; }

    public required string ResourceName { get; init; }

    public required string GapDescription { get; init; }

    public required string Recommendation { get; init; }

    public string? Region { get; init; }

    public string? Sku { get; init; }

    public decimal EstimatedMonthlyCost { get; init; }

    public string CurrencyCode { get; init; } = "USD";

    public string PricingStatus { get; init; } = "Matched";

    public string? PriceQueryServiceName { get; init; }

    public string? PriceQueryRegion { get; init; }

    public string? PriceQuerySkuHint { get; init; }

    public string? PriceQueryMeterHint { get; init; }

    public string? MatchedProductName { get; init; }

    public string? MatchedSkuName { get; init; }

    public string? MatchedArmSkuName { get; init; }

    public string? MatchedMeterName { get; init; }

    public string? MatchedUnitOfMeasure { get; init; }

    public decimal Quantity { get; init; } = 1m;

    public string Confidence { get; init; } = "Medium";

    public string SourceWorksheet { get; init; } = string.Empty;

    public int SourceRowNumber { get; init; }

    public string Assumption { get; init; } = string.Empty;
}
