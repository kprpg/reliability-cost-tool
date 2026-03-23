using System.Globalization;
using System.Text.RegularExpressions;
using ReliabilityCostTool.Core.Common.Models;

namespace ReliabilityCostTool.Core.Utils;

public static partial class AssessmentHeuristics
{
    private static readonly string[] GapMarkers =
    [
        "not zon",
        "not configured",
        "not enabled",
        "disabled",
        "single zone",
        "single instance",
        "no backup",
        "no dr",
        "not redundant",
        "lrs",
        "locally redundant",
        "standard locally redundant",
        "not resilient",
        "gap"
    ];

    private static readonly string[] HealthyMarkers =
    [
        "zone redundant",
        "zrs",
        "gzrs",
        "ra-gzrs",
        "geo-zone-redundant",
        "ha enabled",
        "availability zones",
        "active-active",
        "active/passive"
    ];

    public static bool IndicatesReliabilityGap(AssessmentRecord record, params IEnumerable<string>[] aliasGroups)
    {
        var candidateValues = aliasGroups.Length == 0
            ? record.Values.Values
            : aliasGroups.SelectMany(record.GetMatchingValues).Select(pair => pair.Value);

        var values = candidateValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .ToArray();

        if (values.Length == 0)
        {
            values = [record.FlattenedText().ToLowerInvariant()];
        }

        var hasGap = values.Any(value => GapMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)));
        var isHealthy = values.Any(value => HealthyMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)));
        return hasGap || !isHealthy;
    }

    public static decimal ExtractQuantity(AssessmentRecord record, decimal fallback = 1m) =>
        TryParseDecimal(record.GetValue(ColumnAliases.Quantity), out var quantity) && quantity > 0m
            ? quantity
            : fallback;

    public static decimal? ExtractCapacityGb(AssessmentRecord record)
    {
        var rawValue = record.GetValue(ColumnAliases.Capacity);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (!TryParseDecimal(rawValue, out var quantity))
        {
            var match = NumericValueRegex().Match(rawValue);
            if (!match.Success || !TryParseDecimal(match.Value, out quantity))
            {
                return null;
            }
        }

        if (rawValue.Contains("tb", StringComparison.OrdinalIgnoreCase))
        {
            quantity *= 1024m;
        }

        return quantity;
    }

    public static bool MatchesResourceType(AssessmentRecord record, params string[] keywords) =>
        keywords.Any(keyword => record.ResourceType.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    public static string NormalizeRegion(string? region)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            return "eastus";
        }

        return region
            .Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
    }

    public static bool TryParseDecimal(string? rawValue, out decimal value)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            value = 0m;
            return false;
        }

        var normalized = rawValue
            .Replace("$", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(",", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out value) ||
               decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.CurrentCulture, out value);
    }

    [GeneratedRegex(@"[-+]?[0-9]*\.?[0-9]+")]
    private static partial Regex NumericValueRegex();
}
