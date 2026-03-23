using ReliabilityCostTool.Core.Common.Models;
using ReliabilityCostTool.Core.Utils;

namespace ReliabilityCostTool.Core.AzureSqlDatabases;

internal static class AzureSqlDatabaseSectionParser
{
    public static IReadOnlyList<AssessmentRecord> ExtractRecords(
        string worksheetName,
        IReadOnlyList<IReadOnlyList<string>> worksheetRows)
    {
        var records = new List<AssessmentRecord>();

        for (var rowIndex = 0; rowIndex < worksheetRows.Count; rowIndex++)
        {
            if (!IsSectionMarkerRow(worksheetRows[rowIndex]))
            {
                continue;
            }

            var headerRowIndex = FindHeaderRowIndex(worksheetRows, rowIndex + 1);
            if (headerRowIndex < 0 ||
                !TryGetTableColumnIndexes(
                    worksheetRows[headerRowIndex],
                    out var tierColumnIndex,
                    out var totalColumnIndex,
                    out var zoneRedundantColumnIndex,
                    out var geoReplicatedColumnIndex))
            {
                continue;
            }

            for (var dataRowIndex = headerRowIndex + 1; dataRowIndex < worksheetRows.Count; dataRowIndex++)
            {
                var row = worksheetRows[dataRowIndex];
                if (IsTableTerminator(row))
                {
                    break;
                }

                var tier = GetCellValue(row, tierColumnIndex);
                var totalRaw = GetCellValue(row, totalColumnIndex);
                var zoneRedundantRaw = GetCellValue(row, zoneRedundantColumnIndex);
                var geoReplicatedRaw = GetCellValue(row, geoReplicatedColumnIndex);

                if (string.IsNullOrWhiteSpace(tier) ||
                    tier.Contains("total", StringComparison.OrdinalIgnoreCase) ||
                    !AssessmentHeuristics.TryParseDecimal(totalRaw, out var totalDatabases))
                {
                    continue;
                }

                var zoneRedundantDatabases = AssessmentHeuristics.TryParseDecimal(zoneRedundantRaw, out var parsedZoneRedundant)
                    ? parsedZoneRedundant
                    : 0m;
                var geoReplicatedDatabases = AssessmentHeuristics.TryParseDecimal(geoReplicatedRaw, out var parsedGeoReplicated)
                    ? parsedGeoReplicated
                    : 0m;

                records.Add(new AssessmentRecord
                {
                    SourceWorksheet = worksheetName,
                    RowNumber = dataRowIndex + 1,
                    Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["resource name"] = tier,
                        ["resource type"] = "Azure SQL DB",
                        ["sku"] = tier,
                        [ColumnAliases.AzureSqlDbByTier[0]] = tier,
                        [ColumnAliases.AzureSqlDbCount[0]] = totalDatabases.ToString("0.##"),
                        [ColumnAliases.AzureSqlZoneRedundantDbCount[0]] = zoneRedundantDatabases.ToString("0.##"),
                        [ColumnAliases.AzureSqlGeoReplicatedDbCount[0]] = geoReplicatedDatabases.ToString("0.##"),
                        ["reliability"] = BuildReliabilitySummary(totalDatabases, zoneRedundantDatabases, geoReplicatedDatabases)
                    }
                });
            }
        }

        return records;
    }

    private static bool IsSectionMarkerRow(IReadOnlyList<string> row) =>
        row.Any(cell => ColumnAliases.AzureSqlDbSection.Any(alias =>
            Normalize(cell).Contains(Normalize(alias), StringComparison.Ordinal)));

    private static int FindHeaderRowIndex(IReadOnlyList<IReadOnlyList<string>> rows, int startIndex)
    {
        var lastRowToCheck = Math.Min(rows.Count - 1, startIndex + 8);
        for (var rowIndex = startIndex; rowIndex <= lastRowToCheck; rowIndex++)
        {
            if (TryGetTableColumnIndexes(rows[rowIndex], out _, out _, out _, out _))
            {
                return rowIndex;
            }
        }

        return -1;
    }

    private static bool TryGetTableColumnIndexes(
        IReadOnlyList<string> row,
        out int tierColumnIndex,
        out int totalColumnIndex,
        out int zoneRedundantColumnIndex,
        out int geoReplicatedColumnIndex)
    {
        tierColumnIndex = FindFirstMatchingColumn(row, ColumnAliases.AzureSqlDbByTier);
        totalColumnIndex = FindFirstMatchingColumn(
            row,
            ColumnAliases.AzureSqlDbCount,
            normalizedCell => !normalizedCell.Contains("zone", StringComparison.Ordinal) &&
                              !normalizedCell.Contains("geo", StringComparison.Ordinal));
        zoneRedundantColumnIndex = FindFirstMatchingColumn(row, ColumnAliases.AzureSqlZoneRedundantDbCount);
        geoReplicatedColumnIndex = FindFirstMatchingColumn(row, ColumnAliases.AzureSqlGeoReplicatedDbCount);

        return tierColumnIndex >= 0 &&
               totalColumnIndex >= 0 &&
               zoneRedundantColumnIndex >= 0 &&
               geoReplicatedColumnIndex >= 0;
    }

    private static int FindFirstMatchingColumn(
        IReadOnlyList<string> row,
        IEnumerable<string> aliases,
        Func<string, bool>? predicate = null)
    {
        for (var columnIndex = 0; columnIndex < row.Count; columnIndex++)
        {
            var normalizedCell = Normalize(row[columnIndex]);
            if (string.IsNullOrWhiteSpace(normalizedCell))
            {
                continue;
            }

            if (predicate is not null && !predicate(normalizedCell))
            {
                continue;
            }

            if (aliases.Any(alias => normalizedCell.Contains(Normalize(alias), StringComparison.Ordinal)))
            {
                return columnIndex;
            }
        }

        return -1;
    }

    private static bool IsTableTerminator(IReadOnlyList<string> row)
    {
        if (row.All(string.IsNullOrWhiteSpace))
        {
            return true;
        }

        return IsSectionMarkerRow(row) || TryGetTableColumnIndexes(row, out _, out _, out _, out _);
    }

    private static string GetCellValue(IReadOnlyList<string> row, int columnIndex) =>
        columnIndex >= 0 && columnIndex < row.Count
            ? row[columnIndex].Trim()
            : string.Empty;

    private static string BuildReliabilitySummary(decimal totalDatabases, decimal zoneRedundantDatabases, decimal geoReplicatedDatabases)
    {
        var nonZoneRedundant = Math.Max(0m, totalDatabases - zoneRedundantDatabases);
        var withoutGeoReplication = Math.Max(0m, totalDatabases - geoReplicatedDatabases);
        return $"{nonZoneRedundant:0.##} Azure SQL DBs are not zone redundant; {withoutGeoReplication:0.##} Azure SQL DBs do not have geo-replication.";
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }
}