namespace ReliabilityCostTool.Core.Common.Models;

public sealed class PriceCatalogItem
{
    public required string ServiceName { get; init; }

    public string? ProductName { get; init; }

    public string? SkuName { get; init; }

    public string? ArmSkuName { get; init; }

    public string? ArmRegionName { get; init; }

    public string? MeterName { get; init; }

    public required decimal UnitPrice { get; init; }

    public string CurrencyCode { get; init; } = "USD";

    public string? UnitOfMeasure { get; init; }
}
