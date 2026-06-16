using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Data;
using CSVoom.app;

namespace CSVoom.ui;

/// <summary>
///     Provides utility methods for working with DataGrid columns.
/// </summary>
public static class DataGridUtils
{
    /// <summary>
    ///     The offset of the row number column.
    /// </summary>
    public const int RowNumberColumnOffset = 1;

    /// <summary>
    ///     Initializes the columns of a <see cref="DataGrid" /> based on the provided <see cref="Parser" />.
    /// </summary>
    /// <param name="dataGrid">The <see cref="DataGrid" /> to initialize.</param>
    /// <param name="parser">The <see cref="Parser" /> containing the headers.</param>
    /// <param name="columnsByName">An optional dictionary to store columns indexed by their header name.</param>
    /// <param name="columnsByLetter">An optional dictionary to store columns indexed by their spreadsheet-style letter.</param>
    public static void InitializeColumns(
        DataGrid dataGrid,
        Parser parser,
        Dictionary<string, DataGridColumn>? columnsByName = null,
        Dictionary<string, DataGridColumn>? columnsByLetter = null)
    {
        dataGrid.Columns.Clear();
        columnsByName?.Clear();
        columnsByLetter?.Clear();

        var rowNumberColumn = new DataGridTextColumn
        {
            Header = Configuration.FirstRowIsHeader ? "C0: R1" : "C0: R0",
            Binding = new Binding("RowId"),
            SortMemberPath = "RowNumber",
            IsReadOnly = true,
            CanUserSort = false
        };
        dataGrid.Columns.Add(rowNumberColumn);
        columnsByName?[Parser.RowNumberKey] = rowNumberColumn;
        columnsByLetter?[""] = rowNumberColumn;

        for (var i = 0; i < parser.Headers.Count; i++)
        {
            var header = parser.Headers[i];
            var columnIdentifier = Parser.GetColumnIdentifier(i);

            var column = new DataGridTextColumn
            {
                Header = Configuration.FirstRowIsHeader ? $"{columnIdentifier}: {header}" : header,
                Binding = new Binding($"Values[{i}]"),
                SortMemberPath = $"Values[{i}]"
            };
            dataGrid.Columns.Add(column);
            columnsByName?[header] = column;
            columnsByLetter?[columnIdentifier] = column;
        }
    }

    /// <summary>
    ///     Converts a zero-based grid column index to a data column index.
    /// </summary>
    /// <param name="gridColumnIndex">The zero-based grid column index.</param>
    /// <returns>The data column index, or null if it's the row number column.</returns>
    public static int? ToDataColumnIndex(int gridColumnIndex)
    {
        return gridColumnIndex - RowNumberColumnOffset;
    }
}