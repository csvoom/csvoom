using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Data;
using CSVoom.app;

namespace CSVoom.ui;

public static class DataGridUtils
{
    public const int RowNumberColumnOffset = 1;

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

    public static int? ToDataColumnIndex(int gridColumnIndex)
    {
        return gridColumnIndex - RowNumberColumnOffset;
    }
}