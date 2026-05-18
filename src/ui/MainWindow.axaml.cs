using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using CSVoom.app;
using Avalonia.Input;

namespace CSVoom;

public partial class MainWindow : Window
{
    private const int MaxVisibleRows = 10000;
    private const int RowNumberColumnOffset = 1;

    private static readonly Parser Parser = new();
    private readonly ObservableCollection<Dictionary<string, string>> _visibleRows = new();
    private readonly DataGridCollectionView _gridView;

    private string? _currentFilePath;
    private string? _currentFileName;
    private bool _isLoading;

    private readonly Dictionary<string, DataGridColumn> _columnsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DataGridColumn> _columnsByLetter = new(StringComparer.OrdinalIgnoreCase);

    public MainWindow()
    {
        InitializeComponent();
        _gridView = new DataGridCollectionView(_visibleRows);
        CsvDataGrid.ItemsSource = _gridView;
    }

    // Column discovery helper methods
    private static string GetColumnLetter(int columnIndex)
    {
        var letter = string.Empty;
        columnIndex++;

        while (columnIndex > 0)
        {
            columnIndex--;
            letter = (char)('A' + columnIndex % 26) + letter;
            columnIndex /= 26;
        }

        return letter;
    }

    private DataGridColumn? FindColumnByNameOrLetter(string searchValue)
    {
        if (string.IsNullOrWhiteSpace(searchValue))
        {
            return null;
        }

        var normalizedSearchValue = searchValue.Trim();

        if (_columnsByName.TryGetValue(normalizedSearchValue, out var columnByName))
        {
            return columnByName;
        }

        return _columnsByLetter.GetValueOrDefault(normalizedSearchValue);
    }

    private int FindColumnIndexByNameOrLetter(string searchValue)
    {
        var column = FindColumnByNameOrLetter(searchValue);
        return column is null ? -1 : CsvDataGrid.Columns.IndexOf(column);
    }

    private int ToDataColumnIndex(int gridColumnIndex)
    {
        return gridColumnIndex - RowNumberColumnOffset;
    }
    
    // Command handler methods
    private async Task ExecuteCommandAsync(string commandText)
    { // Interprets text command and executes the corresponding action
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return;
        }
        
        if (CsvDataGrid.Columns.Count == 0)
        {
            StatusTextBlock.Text = "Open a CSV file before running any commands.";
            return;
        }

        var parts = commandText.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0];
        var arguments = parts.Length > 1 ? parts[1] : string.Empty;

        if (command.Equals("load", StringComparison.OrdinalIgnoreCase))
        {
            await Command_LoadAsync(arguments);
            return;
        }
        
        if (command.Equals("find", StringComparison.OrdinalIgnoreCase))
        {
            await Command_FindAsync(arguments);
            return;
        }

        if (command.Equals("filter", StringComparison.OrdinalIgnoreCase))
        {
            Command_Filter(arguments);
            return;
        }

        if (command.Equals("hide", StringComparison.OrdinalIgnoreCase))
        {
            Command_Hide(arguments);
            return;
        }

        if (command.Equals("unhide", StringComparison.OrdinalIgnoreCase))
        {
            Command_Unhide(arguments);
            return;
        }

        StatusTextBlock.Text = $"Unknown command: {command}";
    }

    private async Task Command_LoadAsync(string arguments)
    { // Loads a specified range of rows from the CSV file into the view
        if (string.IsNullOrWhiteSpace(arguments))
        {
            StatusTextBlock.Text = "Usage: load 1:10000";
            return;
        }

        var rangeParts = arguments.Split(':', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (rangeParts.Length != 2
            || !int.TryParse(rangeParts[0], out var startRow)
            || !int.TryParse(rangeParts[1], out var endRow)
            || startRow <= 0
            || endRow < startRow)
        {
            StatusTextBlock.Text = "Usage: load 1:10000";
            return;
        }

        await LoadRangeIntoViewAsync(startRow, endRow);
    }

    private async Task Command_FindAsync(string searchText)
    { // Searches for the specified text within the CSV data and highlights the first match
        if (CsvDataGrid.Columns.Count == 0 || _currentFilePath is null)
        {
            StatusTextBlock.Text = "Open a CSV file before running find commands.";
            return;
        }

        if (string.IsNullOrWhiteSpace(searchText))
        {
            StatusTextBlock.Text = "Usage: find word or find word columnName";
            return;
        }

        var findParts = searchText.Trim().Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        searchText = findParts[0];
        var columnSearchValue = findParts.Length > 1 ? findParts[1] : string.Empty;
        string? searchHeader = null;

        if (!string.IsNullOrWhiteSpace(columnSearchValue))
        {
            var columnIndex = FindColumnIndexByNameOrLetter(columnSearchValue);

            if (columnIndex < 0)
            {
                StatusTextBlock.Text = $"Column not found: {columnSearchValue}";
                return;
            }

            searchHeader = columnIndex == 0
                ? Parser.RowNumberKey
                : Parser.Headers[ToDataColumnIndex(columnIndex)];
        }

        StatusTextBlock.Text = string.IsNullOrWhiteSpace(searchHeader)
            ? $"Searching for \"{searchText}\"..."
            : searchHeader == Parser.RowNumberKey
                ? $"Searching for \"{searchText}\" in row numbers..."
                : $"Searching for \"{searchText}\" in column {searchHeader}...";

        var match = await Parser.FindFirstAsync(_currentFilePath, searchText, searchHeader);

        if (match is null)
        {
            StatusTextBlock.Text = string.IsNullOrWhiteSpace(searchHeader)
            ? $"No matches found for \"{searchText}\"."
            : searchHeader == Parser.RowNumberKey
                ? $"No matches found for \"{searchText}\" in row numbers."
                : $"No matches found for \"{searchText}\" in column {searchHeader}.";
            return;
        }

        var foundRowNumber = match.Value.RowNumber;
        var windowStart = Math.Max(1, foundRowNumber - 20);
        var windowEnd = windowStart + MaxVisibleRows - 1;

        var rows = await Parser.ReadRangeAsync(_currentFilePath, windowStart, windowEnd, MaxVisibleRows);
        ReplaceVisibleRows(rows);

        var visibleMatch = _visibleRows.FirstOrDefault(row =>
            row.TryGetValue(Parser.RowNumberKey, out var rowNumberText)
            && int.TryParse(rowNumberText, out var rowNumber)
            && rowNumber == foundRowNumber);

        var column = match.Value.Header == Parser.RowNumberKey
            ? CsvDataGrid.Columns[0]
            : FindColumnByNameOrLetter(match.Value.Header);

        if (column is not null)
        {
            column.IsVisible = true;

            if (visibleMatch is not null)
            {
                CsvDataGrid.SelectedItem = visibleMatch;
                CsvDataGrid.ScrollIntoView(visibleMatch, column);
            }
            else
            {
                CsvDataGrid.ScrollIntoView(_visibleRows.FirstOrDefault(), column);
            }

            CsvDataGrid.Focus();
        }

        var foundColumnText = match.Value.Header == Parser.RowNumberKey
            ? "row numbers"
            : match.Value.Header;

        StatusTextBlock.Text = foundRowNumber == 1
            ? $"Found \"{searchText}\" in the header row, column {foundColumnText}."
            : $"Found \"{searchText}\" at row {foundRowNumber:N0}, column {foundColumnText}.";
    }
    
    private void Command_Filter(string arguments)
    { // Applies a filter to the CSV data based on user input
        if (string.IsNullOrWhiteSpace(arguments))
        {
            StatusTextBlock.Text = "Usage: filter word, filter columnName, or filter clear";
            return;
        }

        var filterText = arguments.Trim();

        if (filterText.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            _gridView.Filter = null;
            _gridView.Refresh();
            StatusTextBlock.Text = "Filter cleared.";
            return;
        }

        var matchingHeader = Parser.Headers.FirstOrDefault(header =>
            header.Equals(filterText, StringComparison.OrdinalIgnoreCase));

        if (matchingHeader is not null)
        {
            _gridView.Filter = item =>
            {
                if (item is not Dictionary<string, string> row)
                {
                    return false;
                }

                if (!row.TryGetValue(matchingHeader, out var value))
                {
                    return false;
                }

                return !string.IsNullOrWhiteSpace(value)
                    && !value.Trim().Equals(@"\N", StringComparison.OrdinalIgnoreCase);
            };

            _gridView.Refresh();
            StatusTextBlock.Text = $"Filtered rows where column \"{matchingHeader}\" is not empty and not \\N.";
            return;
        }

        _gridView.Filter = item =>
        {
            if (item is not Dictionary<string, string> row)
            {
                return false;
            }

            foreach (var header in Parser.Headers)
            {
                if (!row.TryGetValue(header, out var value))
                {
                    continue;
                }

                if (value.Contains(filterText, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        };

        _gridView.Refresh();
        StatusTextBlock.Text = $"Filtered rows containing \"{filterText}\".";
    }

    private void Command_Hide(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            StatusTextBlock.Text = "Usage: hide a:x or hide columnName1:columnName2";
            return;
        }

        var rangeParts = arguments.Split(':', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (rangeParts.Length == 1)
        {
            var column = FindColumnByNameOrLetter(rangeParts[0]);
            if (column is null)
            {
                StatusTextBlock.Text = $"Column not found: {rangeParts[0]}";
                return;
            }

            column.IsVisible = false;
            StatusTextBlock.Text = $"Hidden column {column.Header}.";
            return;
        }

        var startIndex = FindColumnIndexByNameOrLetter(rangeParts[0]);
        var endIndex = FindColumnIndexByNameOrLetter(rangeParts[1]);

        if (startIndex < 0)
        {
            StatusTextBlock.Text = $"Column not found: {rangeParts[0]}";
            return;
        }

        if (endIndex < 0)
        {
            StatusTextBlock.Text = $"Column not found: {rangeParts[1]}";
            return;
        }

        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        for (var i = startIndex; i <= endIndex; i++)
        {
            CsvDataGrid.Columns[i].IsVisible = false;
        }

        StatusTextBlock.Text =
            $"Hidden columns {GetColumnLetter(ToDataColumnIndex(startIndex))}:{GetColumnLetter(ToDataColumnIndex(endIndex))}.";
    }

    private void Command_Unhide(string arguments)
    {
        if (arguments.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            ShowAllColumns();
            StatusTextBlock.Text = "All columns are visible.";
            return;
        }

        StatusTextBlock.Text = "Usage: unhide all / unhide a:b / unhide columnName1:columnName2";
    }
    
    // UI interaction methods
    private async void RunCommandButton_Click(object? sender, RoutedEventArgs e)
    {
        await ExecuteCommandAsync(CommandTextBox.Text ?? string.Empty);
        CommandTextBox.SelectAll();
    }

    private async void CommandTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        await ExecuteCommandAsync(CommandTextBox.Text ?? string.Empty);
        CommandTextBox.SelectAll();
        e.Handled = true;
    }

    private async void OpenCsvButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel is null)
        {
            StatusTextBlock.Text = "Unable to open the file picker.";
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open CSV file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("CSV files")
                {
                    Patterns = ["*.csv", "*.gz"]
                }
            ]
        });

        if (files.Count == 0)
        {
            return;
        }

        _currentFilePath = files[0].Path.LocalPath;
        _currentFileName = files[0].Name;
        _gridView.Filter = null;
        _visibleRows.Clear();
        CsvDataGrid.Columns.Clear();
        _columnsByName.Clear();
        _columnsByLetter.Clear();

        StatusTextBlock.Text = $"Loading {_currentFileName}...";

        await Parser.ReadHeadersAsync(_currentFilePath);

        CsvDataGrid.Columns.Clear();
        _columnsByName.Clear();
        _columnsByLetter.Clear();

        var rowNumberColumn = new DataGridTextColumn
        {
            Header = "1",
            Binding = new Binding($"[{Parser.RowNumberKey}]"),
            SortMemberPath = $"[{Parser.RowNumberKey}]",
            IsReadOnly = true,
            CanUserSort = false
        };

        CsvDataGrid.Columns.Add(rowNumberColumn);
        _columnsByName[Parser.RowNumberKey] = rowNumberColumn;
        _columnsByLetter["1"] = rowNumberColumn;

        for (var i = 0; i < Parser.Headers.Count; i++)
        {
            var header = Parser.Headers[i];
            var columnLetter = GetColumnLetter(i);

            var column = new DataGridTextColumn
            {
                Header = $"{columnLetter}: {header}",
                Binding = new Binding($"[{header}]"),
                SortMemberPath = $"[{header}]"
            };

            CsvDataGrid.Columns.Add(column);
            _columnsByName[header] = column;
            _columnsByLetter[columnLetter] = column;
        }

        await LoadRangeIntoViewAsync(1, MaxVisibleRows);
    }

    private void UnhideAllColumnsButton_Click(object? sender, RoutedEventArgs e)
    {
        ShowAllColumns();
    }

    // DataGrid handling methods
    private void ShowAllColumns()
    {
        foreach (var column in CsvDataGrid.Columns)
        {
            column.IsVisible = true;
        }
    }
    
    private async Task LoadRangeIntoViewAsync(int startRow, int endRow)
    {
        if (_currentFilePath is null || _isLoading)
        {
            return;
        }

        if (startRow <= 0 || endRow < startRow)
        {
            StatusTextBlock.Text = "Invalid row range.";
            return;
        }

        if (endRow - startRow > MaxVisibleRows)
        {
            endRow = startRow + MaxVisibleRows;
        }

        _isLoading = true;

        try
        {
            StatusTextBlock.Text = $"Loading rows {startRow:N0}:{endRow:N0}...";

            var rows = await Parser.ReadRangeAsync(_currentFilePath, startRow, endRow, MaxVisibleRows);
            ReplaceVisibleRows(rows);

            if (rows.Count == 0)
            {
                StatusTextBlock.Text = $"No rows found in range {startRow:N0}:{endRow:N0}.";
                return;
            }

            StatusTextBlock.Text = $"Showing {rows.Count:N0} rows from range {startRow:N0}:{endRow:N0}.";
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void ReplaceVisibleRows(IEnumerable<Dictionary<string, string>> rows)
    {
        _visibleRows.Clear();

        foreach (var row in rows)
        {
            _visibleRows.Add(row);
        }

        _gridView.Refresh();
    }
}