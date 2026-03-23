using ReliabilityCostTool.Core.Common.Models;

namespace ReliabilityCostTool.Core.General.Interfaces;

public interface IWorkbookReportBuilder
{
    Task<GeneratedWorkbook> BuildAsync(
        AnalysisResult analysisResult,
        byte[] sourceWorkbookContent,
        CancellationToken cancellationToken = default);
}
