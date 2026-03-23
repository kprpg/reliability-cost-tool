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
using ReliabilityCostTool.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();

app.Run();
