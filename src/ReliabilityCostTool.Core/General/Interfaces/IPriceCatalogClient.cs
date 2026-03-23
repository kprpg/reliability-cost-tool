using ReliabilityCostTool.Core.Common.Models;

namespace ReliabilityCostTool.Core.General.Interfaces;

public interface IPriceCatalogClient
{
    Task<PriceCatalogItem?> FindBestPriceAsync(
        string serviceName,
        string? region,
        string? armSkuName,
        string? meterHint,
        CancellationToken cancellationToken = default);
}
