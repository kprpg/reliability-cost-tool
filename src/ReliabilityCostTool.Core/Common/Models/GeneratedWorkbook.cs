namespace ReliabilityCostTool.Core.Common.Models;

public sealed class GeneratedWorkbook
{
    public required AnalysisResult AnalysisResult { get; init; }

    public required byte[] Content { get; init; }

    public required string FileName { get; init; }

    public string ContentType { get; init; } = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}
