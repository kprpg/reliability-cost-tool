using System.Text;
using ExcelDataReader;
using Microsoft.Extensions.Logging;
using ReliabilityCostTool.Core.AzureSqlDatabases;
using ReliabilityCostTool.Core.Common.Models;
using ReliabilityCostTool.Core.General.Interfaces;
using ReliabilityCostTool.Core.Utils;

namespace ReliabilityCostTool.Core.General.Workbook;

public sealed class AssessmentWorkbookParser(ILogger<AssessmentWorkbookParser> logger) : IAssessmentWorkbookParser
{
    public async Task<IReadOnlyList<AssessmentRecord>> ParseAsync(Stream input, CancellationToken cancellationToken = default)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        await using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        logger.LogInformation("Attempting to create Excel reader for buffered workbook stream of {ByteCount} bytes", buffer.Length);

        IExcelDataReader reader;
        try
        {
            reader = ExcelReaderFactory.CreateReader(buffer);
        }
        catch (ExcelDataReader.Exceptions.HeaderException exception)
        {
            logger.LogWarning(exception, "Workbook reader header validation failed");
            throw new WorkbookReadException(
                "The uploaded workbook could not be read. Verify that the file is a valid .xls or .xlsx workbook and is not protected, IRM-restricted, or corrupted.",
                exception);
        }
        catch (ExcelDataReader.Exceptions.ExcelReaderException exception)
        {
            logger.LogWarning(exception, "Workbook reader could not open the uploaded workbook stream");
            throw new WorkbookReadException(BuildFriendlyReadErrorMessage(exception), exception);
        }

        using (reader)
        {
            var records = new List<AssessmentRecord>();
            var worksheetCount = 0;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                worksheetCount++;

                var worksheetName = string.IsNullOrWhiteSpace(reader.Name) ? "Sheet" : reader.Name;
                var worksheetRows = new List<IReadOnlyList<string>>();
                logger.LogInformation("Reading worksheet {WorksheetName}", worksheetName);

                while (reader.Read())
                {
                    worksheetRows.Add(ReadRow(reader));
                }

                records.AddRange(ExtractStandardTableRecords(worksheetName, worksheetRows));
                records.AddRange(AzureSqlDatabaseSectionParser.ExtractRecords(worksheetName, worksheetRows));
                logger.LogInformation("Completed worksheet {WorksheetName} with {RowCount} raw rows", worksheetName, worksheetRows.Count);
            }
            while (reader.NextResult());

            logger.LogInformation("Workbook reader finished. Worksheets read: {WorksheetCount}. Records extracted: {RecordCount}", worksheetCount, records.Count);

            return records;
        }
    }

    private static string BuildFriendlyReadErrorMessage(ExcelDataReader.Exceptions.ExcelReaderException exception)
    {
        if (exception.Message.Contains("Neither stream 'Workbook' nor 'Book' was found in file", StringComparison.OrdinalIgnoreCase))
        {
            return "The uploaded workbook could not be read by the parser. The file appears to be protected, IRM-restricted, or in a non-standard Excel container format. Save a plain unprotected .xlsx copy and try again.";
        }

        return "The uploaded workbook could not be read. Verify that the file is a valid .xls or .xlsx workbook and try again.";
    }

    private static List<string> ReadRow(IExcelDataReader reader)
    {
        var values = new List<string>(reader.FieldCount);
        for (var index = 0; index < reader.FieldCount; index++)
        {
            values.Add(reader.GetValue(index)?.ToString()?.Trim() ?? string.Empty);
        }

        return values;
    }

    private static Dictionary<int, string> BuildHeaders(IReadOnlyList<string> rowValues)
    {
        var headers = new Dictionary<int, string>();
        for (var index = 0; index < rowValues.Count; index++)
        {
            var header = string.IsNullOrWhiteSpace(rowValues[index]) ? $"Column{index + 1}" : rowValues[index].Trim();
            if (headers.Values.Contains(header, StringComparer.OrdinalIgnoreCase))
            {
                header = $"{header}_{index + 1}";
            }

            headers[index] = header;
        }

        return headers;
    }

    private static IReadOnlyList<AssessmentRecord> ExtractStandardTableRecords(
        string worksheetName,
        IReadOnlyList<IReadOnlyList<string>> worksheetRows)
    {
        var records = new List<AssessmentRecord>();

        for (var headerRowIndex = 0; headerRowIndex < worksheetRows.Count; headerRowIndex++)
        {
            var candidateHeaderRow = worksheetRows[headerRowIndex];
            if (!LooksLikeStandardHeaderRow(candidateHeaderRow))
            {
                continue;
            }

            var headers = BuildHeaders(candidateHeaderRow);

            for (var rowIndex = headerRowIndex + 1; rowIndex < worksheetRows.Count; rowIndex++)
            {
                var values = worksheetRows[rowIndex];

                if (values.All(string.IsNullOrWhiteSpace))
                {
                    break;
                }

                if (LooksLikeStandardHeaderRow(values))
                {
                    headerRowIndex = rowIndex - 1;
                    break;
                }

                var mappedValues = MapValues(headers, values);
                if (mappedValues.Values.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                records.Add(new AssessmentRecord
                {
                    SourceWorksheet = worksheetName,
                    RowNumber = rowIndex + 1,
                    Values = mappedValues
                });

                if (rowIndex == worksheetRows.Count - 1)
                {
                    headerRowIndex = rowIndex;
                }
            }
        }

        return records;
    }

    private static Dictionary<string, string> MapValues(Dictionary<int, string> headers, IReadOnlyList<string> values)
    {
        var mappedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            var cellValue = header.Key < values.Count ? values[header.Key] : string.Empty;
            mappedValues[header.Value] = cellValue;
        }

        return mappedValues;
    }

    private static bool LooksLikeStandardHeaderRow(IReadOnlyList<string> row)
    {
        var aliasGroups = new[]
        {
            ColumnAliases.ResourceName,
            ColumnAliases.ResourceType,
            ColumnAliases.Region,
            ColumnAliases.Sku,
            ColumnAliases.Quantity,
            ColumnAliases.Capacity,
            ColumnAliases.ReliabilitySignals
        };

        var matchedGroups = aliasGroups.Count(aliases => row.Any(cell =>
            aliases.Any(alias => cell.Contains(alias, StringComparison.OrdinalIgnoreCase))));
        var hasIdentityColumn = row.Any(cell =>
            ColumnAliases.ResourceName.Any(alias => cell.Contains(alias, StringComparison.OrdinalIgnoreCase)) ||
            ColumnAliases.ResourceType.Any(alias => cell.Contains(alias, StringComparison.OrdinalIgnoreCase)));

        return hasIdentityColumn && matchedGroups >= 2;
    }
}
