namespace ReliabilityCostTool.Core.General.Workbook;

public sealed class WorkbookReadException(string message, Exception? innerException = null)
    : Exception(message, innerException)
{
}