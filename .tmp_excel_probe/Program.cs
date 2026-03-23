using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using ClosedXML.Excel;

var workbookPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "St Jude - CRE Reliability Assessment.xlsx"));
Console.WriteLine($"Workbook path: {workbookPath}");
Console.WriteLine($"Exists: {File.Exists(workbookPath)}");

if (!File.Exists(workbookPath))
{
    Environment.Exit(2);
}

Probe("NPOI WorkbookFactory", () =>
{
    using var stream = File.Open(workbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    return WorkbookFactory.Create(stream);
});

Probe("NPOI HSSFWorkbook", () =>
{
    using var stream = File.Open(workbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    return new HSSFWorkbook(stream);
});

ProbeClosedXml();

void Probe(string label, Func<IWorkbook> openWorkbook)
{
    Console.WriteLine($"=== {label} ===");
    try
    {
        using var workbook = openWorkbook();
        PrintWorkbook(workbook.GetType().FullName ?? workbook.GetType().Name, workbook.NumberOfSheets,
            Enumerable.Range(0, workbook.NumberOfSheets).Select(i => workbook.GetSheetName(i)).ToList(),
            index => ReadRows(workbook.GetSheetAt(index)));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAILED: {ex.GetType().FullName}: {ex.Message}");
    }
}

void ProbeClosedXml()
{
    Console.WriteLine("=== ClosedXML XLWorkbook ===");
    try
    {
        using var workbook = new XLWorkbook(workbookPath);
        var sheets = workbook.Worksheets.Select(ws => ws.Name).ToList();
        PrintWorkbook(workbook.GetType().FullName ?? workbook.GetType().Name, sheets.Count, sheets,
            index => ReadRows(workbook.Worksheet(index + 1)));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAILED: {ex.GetType().FullName}: {ex.Message}");
    }
}

void PrintWorkbook(string workbookType, int sheetCount, List<string> sheets, Func<int, List<string>> readRows)
{
    Console.WriteLine($"Workbook type: {workbookType}");
    Console.WriteLine($"Number of sheets: {sheetCount}");
    for (var i = 0; i < sheets.Count; i++)
    {
        Console.WriteLine($"Sheet {i}: {sheets[i]}");
    }

    var maxSheets = Math.Min(2, sheetCount);
    for (var sheetIndex = 0; sheetIndex < maxSheets; sheetIndex++)
    {
        Console.WriteLine($"--- First non-empty rows from sheet {sheetIndex}: {sheets[sheetIndex]} ---");
        var rows = readRows(sheetIndex);
        if (rows.Count == 0)
        {
            Console.WriteLine("No non-empty rows found.");
            continue;
        }

        foreach (var row in rows)
        {
            Console.WriteLine(row);
        }
    }
}

List<string> ReadRows(ISheet sheet)
{
    var formatter = new DataFormatter();
    var evaluator = sheet.Workbook.GetCreationHelper().CreateFormulaEvaluator();
    var rows = new List<string>();
    for (var rowIndex = sheet.FirstRowNum; rowIndex <= sheet.LastRowNum && rows.Count < 5; rowIndex++)
    {
        var row = sheet.GetRow(rowIndex);
        if (row is null)
        {
            continue;
        }

        var parts = new List<string>();
        for (var cellIndex = row.FirstCellNum; cellIndex >= 0 && cellIndex < row.LastCellNum; cellIndex++)
        {
            var cell = row.GetCell(cellIndex, MissingCellPolicy.RETURN_BLANK_AS_NULL);
            if (cell is null)
            {
                continue;
            }

            var text = formatter.FormatCellValue(cell, evaluator)?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add($"C{cellIndex}={text}");
            }
        }

        if (parts.Count > 0)
        {
            rows.Add($"Row {rowIndex}: {string.Join(" | ", parts)}");
        }
    }

    return rows;
}

List<string> ReadRows(IXLWorksheet sheet)
{
    var rows = new List<string>();
    foreach (var row in sheet.RowsUsed())
    {
        var parts = row.CellsUsed().Select(cell => new { cell.Address.ColumnNumber, Text = cell.GetFormattedString().Trim() })
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .Select(x => $"C{x.ColumnNumber - 1}={x.Text}")
            .ToList();

        if (parts.Count > 0)
        {
            rows.Add($"Row {row.RowNumber() - 1}: {string.Join(" | ", parts)}");
        }

        if (rows.Count >= 5)
        {
            break;
        }
    }

    return rows;
}
