using System.Collections.Concurrent;
using ReliabilityCostTool.Core.Common.Models;

namespace ReliabilityCostTool.Api.Services;

public sealed class GeneratedReportStore
{
    private readonly ConcurrentDictionary<string, GeneratedWorkbook> _reports = new(StringComparer.OrdinalIgnoreCase);

    public string Save(GeneratedWorkbook workbook)
    {
        var id = Guid.NewGuid().ToString("N");
        _reports[id] = workbook;
        return id;
    }

    public bool TryGet(string reportId, out GeneratedWorkbook? workbook) =>
        _reports.TryGetValue(reportId, out workbook);
}
