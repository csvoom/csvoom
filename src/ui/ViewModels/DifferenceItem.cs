using System.Collections.Generic;
using CSVoom.app;

namespace CSVoom.ui.ViewModels;

public record DifferenceDetail(int ColumnIndex, string Description, int Row = 0)
{
    public string DisplayLabel => ColumnIndex >= 0
        ? $"{Parser.GetColumnIdentifier(ColumnIndex)}: {Row}"
        : Description;
}

public record DifferenceItem(int Row, List<DifferenceDetail> Details)
{
    public string DisplayLabel => $"Row: {Row} ({Details.Count} differences)";
}
