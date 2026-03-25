using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ReliabilityCostTool.Core.Common.Models;

namespace ReliabilityCostTool.Api.Services;

public sealed class GeneratedReportStore(ILogger<GeneratedReportStore> logger)
{
    private readonly ConcurrentDictionary<string, GeneratedWorkbook> _reports = new(StringComparer.OrdinalIgnoreCase);

    public string Save(GeneratedWorkbook workbook)
    {
        var id = Guid.NewGuid().ToString("N");
        _reports[id] = workbook;
        logger.LogInformation("Report saved with ID {ReportId} for workbook {FileName} ({ByteCount} bytes)",
            id, workbook.FileName, workbook.Content.Length);
        return id;
    }

    public bool TryGet(string reportId, out GeneratedWorkbook? workbook)
    {
        var found = _reports.TryGetValue(reportId, out workbook);
        if (!found)
        {
            logger.LogWarning("Report {ReportId} not found in store", reportId);
        }
        else
        {
            logger.LogInformation("Report {ReportId} retrieved from store", reportId);
        }
        return found;
    }
}
