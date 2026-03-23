using ReliabilityCostTool.Core.Common.Models;

namespace ReliabilityCostTool.Core.General.Interfaces;

public interface IAssessmentWorkbookParser
{
    Task<IReadOnlyList<AssessmentRecord>> ParseAsync(Stream input, CancellationToken cancellationToken = default);
}
