using ReliabilityCostTool.Core.Common.Models;

namespace ReliabilityCostTool.Core.General.Interfaces;

public interface IReliabilityAssessmentService
{
    Task<GeneratedWorkbook> AnalyzeAsync(
        Stream input,
        string inputFileName,
        CancellationToken cancellationToken = default);
}
