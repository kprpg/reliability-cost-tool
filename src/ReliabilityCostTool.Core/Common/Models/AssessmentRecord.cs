using ReliabilityCostTool.Core.Utils;

namespace ReliabilityCostTool.Core.Common.Models;

public sealed class AssessmentRecord
{
    public required string SourceWorksheet { get; init; }

    public required int RowNumber { get; init; }

    public required IReadOnlyDictionary<string, string> Values { get; init; }

    public string ResourceName =>
        GetValue(ColumnAliases.ResourceName) ?? $"Row {RowNumber}";

    public string ResourceType =>
        GetValue(ColumnAliases.ResourceType) ?? "Unknown";

    public string? Region => GetValue(ColumnAliases.Region);

    public string? Sku => GetValue(ColumnAliases.Sku);

    public string? ReliabilityState =>
        GetValue(ColumnAliases.ReliabilitySignals);

    public string? GetValue(params IEnumerable<string>[] aliasGroups)
    {
        foreach (var aliasGroup in aliasGroups)
        {
            foreach (var alias in aliasGroup)
            {
                if (Values.TryGetValue(alias, out var exactValue) && !string.IsNullOrWhiteSpace(exactValue))
                {
                    return exactValue.Trim();
                }

                var match = Values.FirstOrDefault(pair =>
                    pair.Key.Contains(alias, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(pair.Value));

                if (!string.IsNullOrWhiteSpace(match.Value))
                {
                    return match.Value.Trim();
                }
            }
        }

        return null;
    }

    public IEnumerable<KeyValuePair<string, string>> GetMatchingValues(IEnumerable<string> aliases)
    {
        var aliasSet = aliases.ToArray();
        return Values.Where(pair => aliasSet.Any(alias =>
            pair.Key.Contains(alias, StringComparison.OrdinalIgnoreCase)));
    }

    public string FlattenedText() => string.Join(' ', Values.Select(pair => $"{pair.Key} {pair.Value}"));
}
