using System;
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
        if (columnsByName != null) columnsByName[Parser.RowNumberKey] = rowNumberColumn;
        if (columnsByLetter != null) columnsByLetter[""] = rowNumberColumn;

        for (int i = 0; i < parser.Headers.Count; i++)
        {
            string header = parser.Headers[i];
            string columnLetter = Parser.GetColumnLetter(i);
            var column = new DataGridTextColumn
            {
                Header = $"{columnLetter}: {header}",
                Binding = new Binding($"Values[{i}]"),
                SortMemberPath = $"Values[{i}]"
            };
            dataGrid.Columns.Add(column);
            if (columnsByName != null) columnsByName[header] = column;
            if (columnsByLetter != null) columnsByLetter[columnLetter] = column;
        }
    }

    public static int? ToDataColumnIndex(int gridColumnIndex)
    {
        return gridColumnIndex - RowNumberColumnOffset;
    }
}
