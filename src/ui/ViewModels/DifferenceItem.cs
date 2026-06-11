using CSVoom.app;

namespace CSVoom.ui.ViewModels;

public record DifferenceItem(int Row, string Column, int ColumnIndex, string Description)
{
    public string DisplayLabel => ColumnIndex >= 0
        ? $"Row: {Row} - Col: {Parser.GetColumnLetter(ColumnIndex)} ({Description})"
        : $"Row: {Row} - {Description}";
}
