using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ReliabilityCostTool.Core.Common.Models;
using ReliabilityCostTool.Core.General.Interfaces;
using ReliabilityCostTool.Core.Utils;

namespace ReliabilityCostTool.Core.General.Pricing;

public sealed class AzureRetailPriceClient(HttpClient httpClient, ILogger<AzureRetailPriceClient> logger) : IPriceCatalogClient
{
    private const string Endpoint = "https://prices.azure.com/api/retail/prices";
    private const int PageSize = 100;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private readonly ConcurrentDictionary<string, PriceCatalogItem?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<List<PriceCatalogItem>>> _queryCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<PriceCatalogItem?> FindBestPriceAsync(
        string serviceName,
        string? region,
        string? armSkuName,
        string? meterHint,
        CancellationToken cancellationToken = default)
    {
        var normalizedRegion = AssessmentHeuristics.NormalizeRegion(region);
        var cacheKey = string.Join('|', serviceName, normalizedRegion, armSkuName, meterHint);

        if (_cache.TryGetValue(cacheKey, out var cachedItem))
        {
            return cachedItem;
        }

        var bestItem = await FindBestMatchingItemAsync(serviceName, normalizedRegion, armSkuName, meterHint, cancellationToken);

        _cache[cacheKey] = bestItem;
        return bestItem;
    }

    private async Task<PriceCatalogItem?> FindBestMatchingItemAsync(
        string serviceName,
        string normalizedRegion,
        string? armSkuName,
        string? meterHint,
        CancellationToken cancellationToken)
    {
        var queries = BuildQueries(serviceName, normalizedRegion, armSkuName);

        foreach (var query in queries)
        {
            var items = await GetItemsForQueryAsync(query.Query, query.MaxPages, cancellationToken);
            var bestItem = SelectBestItem(items, armSkuName, meterHint, normalizedRegion);
            if (bestItem is not null)
            {
                return bestItem;
            }
        }

        return null;
    }

    private async Task<List<PriceCatalogItem>> GetItemsForQueryAsync(string query, int maxPages, CancellationToken cancellationToken)
    {
        var cacheKey = $"{maxPages}|{query}";
        var fetchTask = _queryCache.GetOrAdd(cacheKey, _ => FetchItemsAsync(query, maxPages, CancellationToken.None));

        try
        {
            return await fetchTask.WaitAsync(cancellationToken);
        }
        catch
        {
            _queryCache.TryRemove(cacheKey, out _);
            throw;
        }
    }

    private async Task<List<PriceCatalogItem>> FetchItemsAsync(string query, int maxPages, CancellationToken cancellationToken)
    {
        var items = new List<PriceCatalogItem>();
        string? nextPage = BuildQueryUrl(query);
        var pageCount = 0;

        while (!string.IsNullOrWhiteSpace(nextPage) && pageCount < maxPages)
        {
            pageCount++;
            RetailPriceResponse? response;

            try
            {
                using var requestTimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                requestTimeoutSource.CancelAfter(RequestTimeout);
                response = await httpClient.GetFromJsonAsync<RetailPriceResponse>(nextPage, requestTimeoutSource.Token);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Azure Retail Prices API request timed out for query {Query}", query);
                return items;
            }
            catch (HttpRequestException exception)
            {
                logger.LogWarning(exception, "Azure Retail Prices API request failed for query {Query}", query);
                return items;
            }

            if (response?.Items is null)
            {
                break;
            }

            items.AddRange(response.Items
                .Where(item => string.Equals(item.ResolvedPriceType, "Consumption", StringComparison.OrdinalIgnoreCase))
                .Select(item => new PriceCatalogItem
                {
                    ServiceName = item.ServiceName ?? string.Empty,
                    ProductName = item.ProductName,
                    SkuName = item.SkuName,
                    ArmSkuName = item.ArmSkuName,
                    ArmRegionName = item.ArmRegionName,
                    MeterName = item.MeterName,
                    UnitPrice = item.RetailPrice,
                    CurrencyCode = item.CurrencyCode ?? "USD",
                    UnitOfMeasure = item.UnitOfMeasure
                }));

            nextPage = response.NextPageLink;
        }

        return items;
    }

    private static IReadOnlyList<PricingQuery> BuildQueries(string serviceName, string region, string? armSkuName)
    {
        var queries = new List<PricingQuery>();

        if (!string.IsNullOrWhiteSpace(armSkuName))
        {
            queries.Add(new PricingQuery(BuildQuery(serviceName, region, armSkuName), 2));
        }

        queries.Add(new PricingQuery(BuildQuery(serviceName, region), 6));
        queries.Add(new PricingQuery(BuildServiceOnlyQuery(serviceName), 4));

        return queries;
    }

    private static string BuildQuery(string serviceName, string region, string? armSkuName = null)
    {
        var escapedServiceName = serviceName.Replace("'", "''", StringComparison.OrdinalIgnoreCase);
        var query = $"serviceName eq '{escapedServiceName}' and armRegionName eq '{region}'";

        if (!string.IsNullOrWhiteSpace(armSkuName))
        {
            var escapedSkuName = armSkuName.Replace("'", "''", StringComparison.OrdinalIgnoreCase);
            query += $" and armSkuName eq '{escapedSkuName}'";
        }

        return query;
    }

    private static string BuildServiceOnlyQuery(string serviceName)
    {
        var escapedServiceName = serviceName.Replace("'", "''", StringComparison.OrdinalIgnoreCase);
        return $"serviceName eq '{escapedServiceName}'";
    }

    private static string BuildQueryUrl(string query) =>
        $"{Endpoint}?$filter={Uri.EscapeDataString(query)}&$top={PageSize}";

    private static PriceCatalogItem? SelectBestItem(
        IReadOnlyList<PriceCatalogItem> items,
        string? armSkuName,
        string? meterHint,
        string region)
    {
        var scored = items
            .Where(item => item.UnitPrice > 0m)
            .Select(item => (Item: item, Score: Score(item, armSkuName, meterHint, region)))
            .OrderByDescending(pair => pair.Score)
            .ToList();

        if (scored.Count == 0)
        {
            return null;
        }

        var topScore = scored[0].Score;
        var topItems = scored
            .Where(pair => pair.Score == topScore)
            .Select(pair => pair.Item)
            .OrderBy(item => item.UnitPrice)
            .ToList();

        // Pick the median-priced item among equally-scored top candidates
        // to avoid systematically underestimating with the cheapest SKU.
        return topItems[topItems.Count / 2];
    }

    private static int Score(PriceCatalogItem item, string? armSkuName, string? meterHint, string region)
    {
        var score = 0;

        if (string.Equals(item.ArmRegionName, region, StringComparison.OrdinalIgnoreCase))
        {
            score += 4;
        }

        if (!string.IsNullOrWhiteSpace(armSkuName) &&
            (string.Equals(item.ArmSkuName, armSkuName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(item.SkuName, armSkuName, StringComparison.OrdinalIgnoreCase) ||
             (item.ArmSkuName?.Contains(armSkuName, StringComparison.OrdinalIgnoreCase) ?? false) ||
             (item.SkuName?.Contains(armSkuName, StringComparison.OrdinalIgnoreCase) ?? false)))
        {
            score += 8;
        }

        var skuHintTokens = Tokenize(armSkuName);
        if (skuHintTokens.Count > 0)
        {
            score += 3 * CountCommonTokens(skuHintTokens, Tokenize(item.ArmSkuName));
            score += 3 * CountCommonTokens(skuHintTokens, Tokenize(item.SkuName));
            score += 2 * CountCommonTokens(skuHintTokens, Tokenize(item.ProductName));
            score += CountCommonTokens(skuHintTokens, Tokenize(item.MeterName));
        }

        if (!string.IsNullOrWhiteSpace(meterHint) &&
            ((item.MeterName?.Contains(meterHint, StringComparison.OrdinalIgnoreCase) ?? false) ||
             (item.ProductName?.Contains(meterHint, StringComparison.OrdinalIgnoreCase) ?? false) ||
             (item.SkuName?.Contains(meterHint, StringComparison.OrdinalIgnoreCase) ?? false)))
        {
            score += 6;
        }

        var meterHintTokens = Tokenize(meterHint);
        if (meterHintTokens.Count > 0)
        {
            score += 2 * CountCommonTokens(meterHintTokens, Tokenize(item.MeterName));
            score += CountCommonTokens(meterHintTokens, Tokenize(item.ProductName));
        }

        if (item.ProductName?.Contains("Spot", StringComparison.OrdinalIgnoreCase) ?? false)
        {
            score -= 10;
        }

        if (item.ProductName?.Contains("Reservation", StringComparison.OrdinalIgnoreCase) ?? false)
        {
            score -= 8;
        }

        return score;
    }

    private static int CountCommonTokens(HashSet<string> left, HashSet<string> right) =>
        left.Count == 0 || right.Count == 0
            ? 0
            : left.Intersect(right, StringComparer.OrdinalIgnoreCase).Count();

    private static HashSet<string> Tokenize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var normalized = new string(value
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray());

        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawToken in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var token in ExpandToken(rawToken))
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }

    private static IEnumerable<string> ExpandToken(string rawToken)
    {
        switch (rawToken)
        {
            case "gp":
                yield return "general";
                yield return "purpose";
                break;
            case "bc":
                yield return "business";
                yield return "critical";
                break;
            case "mo":
                yield return "memory";
                yield return "optimized";
                break;
            default:
                yield return rawToken;
                break;
        }
    }

    private sealed record PricingQuery(string Query, int MaxPages);

    private sealed class RetailPriceResponse
    {
        [JsonPropertyName("Items")]
        public List<RetailPriceItem>? Items { get; init; }

        [JsonPropertyName("NextPageLink")]
        public string? NextPageLink { get; init; }
    }

    private sealed class RetailPriceItem
    {
        [JsonPropertyName("serviceName")]
        public string? ServiceName { get; init; }

        [JsonPropertyName("productName")]
        public string? ProductName { get; init; }

        [JsonPropertyName("skuName")]
        public string? SkuName { get; init; }

        [JsonPropertyName("armSkuName")]
        public string? ArmSkuName { get; init; }

        [JsonPropertyName("armRegionName")]
        public string? ArmRegionName { get; init; }

        [JsonPropertyName("meterName")]
        public string? MeterName { get; init; }

        [JsonPropertyName("retailPrice")]
        public decimal RetailPrice { get; init; }

        [JsonPropertyName("currencyCode")]
        public string? CurrencyCode { get; init; }

        [JsonPropertyName("unitOfMeasure")]
        public string? UnitOfMeasure { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("priceType")]
        public string? LegacyPriceType { get; init; }

        public string? ResolvedPriceType => Type ?? LegacyPriceType;
    }
}
