using System.Collections.Generic;
using CSVoom.app;

namespace CSVoom.ui.ViewModels;

public record DifferenceDetail(int ColumnIndex, string Description, int Row = 0)
{
    public string DisplayLabel => ColumnIndex >= 0
        ? $"Row {Row}, Column {Parser.GetColumnIdentifier(ColumnIndex)} ({Description})"
        : $"Row {Row}: {Description}";
}

public record DifferenceItem(int Row, List<DifferenceDetail> Details)
{
    public string DisplayLabel => $"Row: {Row} ({Details.Count} differences)";
}
