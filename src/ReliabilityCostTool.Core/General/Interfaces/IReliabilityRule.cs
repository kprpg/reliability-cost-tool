using ReliabilityCostTool.Core.Common.Models;

namespace ReliabilityCostTool.Core.General.Interfaces;

public interface IReliabilityRule
{
    string CategoryName { get; }

    bool AppliesTo(AssessmentRecord record);

    Task<IReadOnlyList<ReliabilityFinding>> EvaluateAsync(
        AssessmentRecord record,
        IPriceCatalogClient priceCatalogClient,
        CancellationToken cancellationToken = default);
}
