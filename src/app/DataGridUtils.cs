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
            Header = "",
            Binding = new Binding("RowNumber"),
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
            var columnIdentifier = Configuration.UseNumbersForColumns
                ? i.ToString()
                : Parser.GetColumnLetter(i);

            var column = new DataGridTextColumn
            {
                Header = $"{columnIdentifier}: {header}",
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