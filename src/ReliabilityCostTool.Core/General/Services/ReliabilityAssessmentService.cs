using ReliabilityCostTool.Core.Common.Models;
using ReliabilityCostTool.Core.General.Interfaces;
using Microsoft.Extensions.Logging;

namespace ReliabilityCostTool.Core.General.Services;

public sealed class ReliabilityAssessmentService(
    IAssessmentWorkbookParser workbookParser,
    IEnumerable<IReliabilityRule> rules,
    IPriceCatalogClient priceCatalogClient,
    IWorkbookReportBuilder workbookReportBuilder,
    ILogger<ReliabilityAssessmentService> logger) : IReliabilityAssessmentService
{
    private readonly IReadOnlyList<IReliabilityRule> _rules = rules.ToList();

    public async Task<GeneratedWorkbook> AnalyzeAsync(
        Stream input,
        string inputFileName,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting reliability assessment for workbook {InputFileName}", inputFileName);

        await using var bufferedInput = new MemoryStream();
        await input.CopyToAsync(bufferedInput, cancellationToken);
        var sourceWorkbookContent = bufferedInput.ToArray();
        logger.LogInformation("Buffered workbook {InputFileName} with {ByteCount} bytes", inputFileName, sourceWorkbookContent.Length);

        bufferedInput.Position = 0;
        var records = await workbookParser.ParseAsync(bufferedInput, cancellationToken);
        logger.LogInformation("Parsed {RecordCount} records from workbook {InputFileName}", records.Count, inputFileName);
        var findings = new List<ReliabilityFinding>();
        var categoryResourceTypeCounts = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var rule in _rules.Where(rule => rule.AppliesTo(record)))
            {
                if (!categoryResourceTypeCounts.TryGetValue(rule.CategoryName, out var typeCounts))
                {
                    typeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    categoryResourceTypeCounts[rule.CategoryName] = typeCounts;
                }

                typeCounts[record.ResourceType] = typeCounts.GetValueOrDefault(record.ResourceType) + 1;

                var ruleFindings = await rule.EvaluateAsync(record, priceCatalogClient, cancellationToken);
                findings.AddRange(ruleFindings);
            }
        }

        var analysisResult = new AnalysisResult
        {
            InputFileName = inputFileName,
            Records = records,
            Findings = findings,
            CategoryResourceTypeCounts = categoryResourceTypeCounts
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => (IReadOnlyDictionary<string, int>)kvp.Value,
                    StringComparer.OrdinalIgnoreCase)
        };

        logger.LogInformation(
            "Generated {FindingCount} findings for workbook {InputFileName} with estimated monthly cost {MonthlyCost}",
            analysisResult.TotalFindings,
            inputFileName,
            analysisResult.TotalEstimatedMonthlyCost);

        return await workbookReportBuilder.BuildAsync(analysisResult, sourceWorkbookContent, cancellationToken);
    }
}
