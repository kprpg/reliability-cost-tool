using ReliabilityCostTool.Core.General.Workbook;

var workbookPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "St Jude - CRE Reliability Assessment.xlsx");
Console.WriteLine($"Workbook: {Path.GetFullPath(workbookPath)}");

try
{
    await using var stream = File.OpenRead(workbookPath);
    var parser = new AssessmentWorkbookParser();
    var records = await parser.ParseAsync(stream);
    Console.WriteLine($"Parsed records: {records.Count}");

    foreach (var record in records.Take(10))
    {
        Console.WriteLine($"Row {record.RowNumber}: {record.ResourceType} | {record.ResourceName} | {record.ReliabilityState}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Exception: {ex.GetType().FullName}");
    Console.WriteLine(ex.Message);
    if (ex.InnerException is not null)
    {
        Console.WriteLine($"Inner: {ex.InnerException.GetType().FullName}");
        Console.WriteLine(ex.InnerException.Message);
    }
}
