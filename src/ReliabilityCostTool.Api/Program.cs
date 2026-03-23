using ReliabilityCostTool.Api.Contracts;
using ReliabilityCostTool.Api.Services;
using ReliabilityCostTool.Core.AzureSqlDatabases;
using ReliabilityCostTool.Core.Backup;
using ReliabilityCostTool.Core.Compute;
using ReliabilityCostTool.Core.Databases;
using ReliabilityCostTool.Core.General.Interfaces;
using ReliabilityCostTool.Core.General.Pricing;
using ReliabilityCostTool.Core.General.Reports;
using ReliabilityCostTool.Core.General.Services;
using ReliabilityCostTool.Core.General.Workbook;
using ReliabilityCostTool.Core.SiteRecovery;
using ReliabilityCostTool.Core.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<GeneratedReportStore>();
builder.Services.AddSingleton<IAssessmentWorkbookParser, AssessmentWorkbookParser>();
builder.Services.AddSingleton<IWorkbookReportBuilder, WorkbookReportBuilder>();
builder.Services.AddScoped<IReliabilityAssessmentService, ReliabilityAssessmentService>();
builder.Services.AddScoped<IReliabilityRule, VmReliabilityRule>();
builder.Services.AddScoped<IReliabilityRule, StorageReliabilityRule>();
builder.Services.AddScoped<IReliabilityRule, DatabaseReliabilityRule>();
builder.Services.AddScoped<IReliabilityRule, AzureSqlDatabaseReliabilityRule>();
builder.Services.AddScoped<IReliabilityRule, BackupReliabilityRule>();
builder.Services.AddScoped<IReliabilityRule, SiteRecoveryRule>();
builder.Services.AddHttpClient<IPriceCatalogClient, AzureRetailPriceClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapPost("/api/reports/analyze", async (
    IFormFile file,
    IReliabilityAssessmentService assessmentService,
    GeneratedReportStore reportStore,
    CancellationToken cancellationToken) =>
{
    if (file.Length == 0)
    {
        return Results.BadRequest("The uploaded workbook is empty.");
    }

    await using var stream = file.OpenReadStream();
    var workbook = await assessmentService.AnalyzeAsync(stream, file.FileName, cancellationToken);
    var reportId = reportStore.Save(workbook);

    var response = new AnalyzeResponse
    {
        ReportId = reportId,
        InputFileName = workbook.AnalysisResult.InputFileName,
        RowsAnalyzed = workbook.AnalysisResult.Records.Count,
        Findings = workbook.AnalysisResult.TotalFindings,
        EstimatedMonthlyCost = workbook.AnalysisResult.TotalEstimatedMonthlyCost,
        Categories = workbook.AnalysisResult.FindingsByCategory
            .Select(group => new AnalyzeResponse.CategorySummary
            {
                Name = group.Key,
                Count = group.Value.Count,
                EstimatedMonthlyCost = group.Value.Sum(item => item.EstimatedMonthlyCost)
            })
            .ToList()
    };

    return Results.Ok(response);
})
.DisableAntiforgery();

app.MapGet("/api/reports/{reportId}/download", (string reportId, GeneratedReportStore reportStore) =>
{
    if (!reportStore.TryGet(reportId, out var workbook) || workbook is null)
    {
        return Results.NotFound();
    }

    return Results.File(workbook.Content, workbook.ContentType, workbook.FileName);
});

app.MapDefaultEndpoints();
app.Run();
