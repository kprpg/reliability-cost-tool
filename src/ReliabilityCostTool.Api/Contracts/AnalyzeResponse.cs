namespace ReliabilityCostTool.Api.Contracts;

public sealed class AnalyzeResponse
{
    public required string ReportId { get; init; }

    public required string InputFileName { get; init; }

    public required int RowsAnalyzed { get; init; }

    public required int Findings { get; init; }

    public required decimal EstimatedMonthlyCost { get; init; }

    public required IReadOnlyList<CategorySummary> Categories { get; init; }

    public sealed class CategorySummary
    {
        public required string Name { get; init; }

        public required int Count { get; init; }

        public required decimal EstimatedMonthlyCost { get; init; }
    }
}
