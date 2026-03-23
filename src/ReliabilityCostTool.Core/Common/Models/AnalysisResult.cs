namespace ReliabilityCostTool.Core.Common.Models;

public sealed class AnalysisResult
{
    public required string InputFileName { get; init; }

    public required IReadOnlyList<AssessmentRecord> Records { get; init; }

    public required IReadOnlyList<ReliabilityFinding> Findings { get; init; }

    /// <summary>
    /// Total record counts per category, broken down by resource type.
    /// Outer key = category name, inner key = resource type, value = total records (including healthy ones).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> CategoryResourceTypeCounts { get; init; }
        = new Dictionary<string, IReadOnlyDictionary<string, int>>();

    public int TotalFindings => Findings.Count;

    public decimal TotalEstimatedMonthlyCost => Findings.Sum(finding => finding.EstimatedMonthlyCost);

    public int PricingUnavailableFindings => Findings.Count(finding =>
        string.Equals(finding.PricingStatus, "Unavailable", StringComparison.OrdinalIgnoreCase));

    public int PricingMatchedFindings => Findings.Count(finding =>
        string.Equals(finding.PricingStatus, "Matched", StringComparison.OrdinalIgnoreCase));

    public IReadOnlyDictionary<string, IReadOnlyList<ReliabilityFinding>> FindingsByCategory =>
        Findings
            .GroupBy(finding => finding.ResourceCategory, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ReliabilityFinding>)group.ToList(), StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, int> PricingUnavailableByCategory =>
        Findings
            .Where(finding => string.Equals(finding.PricingStatus, "Unavailable", StringComparison.OrdinalIgnoreCase))
            .GroupBy(finding => finding.ResourceCategory, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
}
