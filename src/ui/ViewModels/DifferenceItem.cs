using System.Collections.Generic;
using CSVoom.app;

namespace CSVoom.ui.ViewModels;

/// <summary>
///     Represents details about a difference found during comparison.
/// </summary>
/// <param name="ColumnIndex">The index of the column with the difference.</param>
/// <param name="Description">A description of the difference.</param>
/// <param name="Row">The row number where the difference was found.</param>
public record DifferenceDetail(int ColumnIndex, string Description, int Row = 0)
{
    /// <summary>
    ///     Gets a display label for the difference detail.
    /// </summary>
    public string DisplayLabel => ColumnIndex >= 0
        ? $"Row {Row}, Column {Parser.GetColumnIdentifier(ColumnIndex)} ({Description})"
        : $"Row {Row}: {Description}";
}

/// <summary>
///     Represents a row containing differences.
/// </summary>
/// <param name="Row">The row number.</param>
/// <param name="Details">A list of difference details for this row.</param>
public record DifferenceItem(int Row, List<DifferenceDetail> Details)
{
    /// <summary>
    ///     Gets a display label for the difference item.
    /// </summary>
    public string DisplayLabel => $"Row: {Row} ({Details.Count} differences)";
}
