using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CSVoom.app;

namespace CSVoom;

public partial class MainWindow : Window
{
    private const int MaxVisibleRows = 10000;
    private const int RowNumberColumnOffset = 1;
    private static readonly IReadOnlyList<string> CommandSuggestions =
    [
        "load ",
        "find ",
        "goto ",
        "filter ",
        "filter clear",
        "hide ",
        "unhide all"
    ];

    private static readonly IReadOnlyDictionary<string, string> CommandExamples =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["load"] = "Arguments: rangeStart:rangeEnd",
            ["find"] = "Arguments: word / word columnName / columnName",
            ["goto"] = "Arguments: word / word columnName / columnName",
            ["filter"] = "Arguments: word / columnName / 'clear'",
            ["hide"] = "Arguments: letter:letter / columnName1:columnName2",
            ["unhide"] = "Arguments: all"
        };

    private static readonly Parser Parser = new();

    private readonly Dictionary<string, DataGridColumn> _columnsByLetter = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DataGridColumn> _columnsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly DataGridCollectionView _gridView;
    private readonly ObservableCollection<Dictionary<string, string>> _visibleRows = new();

    private string? _currentFileName;
    private string? _currentFilePath;
    private bool _isLoading;

    private string? _lastFindSearchText;
    private string? _lastFindSearchHeader;
    private int _lastFindRowIndex = -1;
    private int _lastFindHeaderIndex = -1;

    private string? _lastGotoSearchText;
    private string? _lastGotoSearchHeader;
    private int _lastGotoRowNumber = 0;
    private string? _lastGotoHeader;

    /// <summary>
    ///     Initializes the main window and connects the visible row collection to the data grid.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        CommandTextBox.ItemsSource = CommandSuggestions;
        _gridView = new DataGridCollectionView(_visibleRows);
        CsvDataGrid.ItemsSource = _gridView;
    }

    // Utility

    /// <summary>
    ///     Clears cached find and goto positions so the next command starts from the first match again.
    /// </summary>
    private void ResetSearchState()
    {
        _lastFindSearchText = null;
        _lastFindSearchHeader = null;
        _lastFindRowIndex = -1;
        _lastFindHeaderIndex = -1;

        _lastGotoSearchText = null;
        _lastGotoSearchHeader = null;
        _lastGotoRowNumber = 0;
        _lastGotoHeader = null;
    }

    /// <summary>
    ///     Converts a zero-based data column index into its spreadsheet-style column letter.
    /// </summary>
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

    /// <summary>
    ///     Finds a data grid column by its display name or spreadsheet-style column letter.
    /// </summary>
    private DataGridColumn? FindColumnByNameOrLetter(string searchValue)
    {
        if (string.IsNullOrWhiteSpace(searchValue)) return null;
        var normalizedSearchValue = searchValue.Trim();
        if (_columnsByName.TryGetValue(normalizedSearchValue, out var columnByName)) return columnByName;
        return _columnsByLetter.GetValueOrDefault(normalizedSearchValue);
    }

    /// <summary>
    ///     Finds the data grid column index for the specified column name or letter.
    /// </summary>
    private int FindColumnIndexByNameOrLetter(string searchValue)
    {
        var column = FindColumnByNameOrLetter(searchValue);
        return column is null ? -1 : CsvDataGrid.Columns.IndexOf(column);
    }

    /// <summary>
    ///     Converts a grid column index to the corresponding parser data column index.
    /// </summary>
    private int ToDataColumnIndex(int gridColumnIndex)
    {
        return gridColumnIndex - RowNumberColumnOffset;
    }

    // Commands

    /// <summary>
    ///     Parses and executes a command entered by the user.
    /// </summary>
    private async Task ExecuteCommandAsync(string commandText)
    {
        // Interprets text command and executes the corresponding action
        if (string.IsNullOrWhiteSpace(commandText)) return;
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
            Command_Find(arguments);
            return;
        }

        if (command.Equals("goto", StringComparison.OrdinalIgnoreCase))
        {
            await Command_GotoAsync(arguments);
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

    /// <summary>
    ///     Handles the load command by parsing a row range and loading it into the view.
    /// </summary>
    private async Task Command_LoadAsync(string arguments)
    {
        // Loads a specified range of rows from the CSV file into the view
        if (string.IsNullOrWhiteSpace(arguments))
        {
            StatusTextBlock.Text = "Usage: load 1:10000";
            return;
        }

        var rangeParts =
            arguments.Split(':', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
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

    /// <summary>
    ///     Handles the find command by locating the next matching visible cell and scrolling it into view.
    /// </summary>
    private void Command_Find(string searchText)
    {
        // Searches only the rows currently loaded into the grid, plus headers and row numbers.
        if (CsvDataGrid.Columns.Count == 0)
        {
            StatusTextBlock.Text = "Open a CSV file before running find commands.";
            return;
        }

        if (string.IsNullOrWhiteSpace(searchText))
        {
            StatusTextBlock.Text = "Usage: find word or find word columnName";
            return;
        }

        var findParts = searchText.Trim()
            .Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
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

        var isSameSearch = searchText.Equals(_lastFindSearchText, StringComparison.OrdinalIgnoreCase)
                           && string.Equals(searchHeader, _lastFindSearchHeader, StringComparison.OrdinalIgnoreCase);

        if (!isSameSearch)
        {
            _lastFindSearchText = searchText;
            _lastFindSearchHeader = searchHeader;
            _lastFindRowIndex = -1;
            _lastFindHeaderIndex = -1;
        }

        StatusTextBlock.Text = string.IsNullOrWhiteSpace(searchHeader)
            ? $"Searching visible rows for \"{searchText}\"..."
            : searchHeader == Parser.RowNumberKey
                ? $"Searching visible row numbers for \"{searchText}\"..."
                : $"Searching visible column {searchHeader} for \"{searchText}\"...";

    var headersToSearch = string.IsNullOrWhiteSpace(searchHeader)
        ? Parser.Headers.Prepend(Parser.RowNumberKey).ToArray()
        : [searchHeader];

    var rowsToSearch = new List<Dictionary<string, string>>();

var headerRow = new Dictionary<string, string>
{
    [Parser.RowNumberKey] = "1"
};

foreach (var header in Parser.Headers) headerRow[header] = header;

rowsToSearch.Add(headerRow);
rowsToSearch.AddRange(_visibleRows);

var totalCells = rowsToSearch.Count * headersToSearch.Length;
var startCellIndex = isSameSearch && _lastFindRowIndex >= 0 && _lastFindHeaderIndex >= 0
    ? (_lastFindRowIndex * headersToSearch.Length) + _lastFindHeaderIndex
    : -1;

for (var offset = 1; offset <= totalCells; offset++)
{
    var absoluteCellIndex = (startCellIndex + offset) % totalCells;
    var rowIndex = absoluteCellIndex / headersToSearch.Length;
    var headerIndex = absoluteCellIndex % headersToSearch.Length;
    var row = rowsToSearch[rowIndex];
    var header = headersToSearch[headerIndex];

    if (!row.TryGetValue(header, out var value)) continue;

    if (!value.Contains(searchText, StringComparison.OrdinalIgnoreCase)) continue;

    _lastFindRowIndex = rowIndex;
    _lastFindHeaderIndex = headerIndex;

    ScrollToMatch(rowIndex == 0 ? null : row, header);

    if (rowIndex == 0)
    {
        StatusTextBlock.Text = header == Parser.RowNumberKey
            ? $"Found \"{searchText}\" in the header row, row numbers."
            : $"Found \"{searchText}\" in the header row, column {header}.";
        return;
    }

    var rowNumberText = row.TryGetValue(Parser.RowNumberKey, out var number)
        ? number
        : "?";
    var foundColumnText = header == Parser.RowNumberKey
        ? "row numbers"
        : header;
    StatusTextBlock.Text = $"Found \"{searchText}\" at visible row {rowNumberText}, column {foundColumnText}.";
    return;
}

        StatusTextBlock.Text = string.IsNullOrWhiteSpace(searchHeader)
            ? $"No visible matches found for \"{searchText}\"."
            : searchHeader == Parser.RowNumberKey
                ? $"No visible matches found for \"{searchText}\" in row numbers."
                : $"No visible matches found for \"{searchText}\" in column {searchHeader}.";
    }

    /// <summary>
    ///     Handles the goto command by locating the next matching cell in the full file and loading nearby rows.
    /// </summary>
    private async Task Command_GotoAsync(string searchText)
    {
        // Searches the full CSV file, loads rows near the next match, and scrolls it into view.
        if (CsvDataGrid.Columns.Count == 0 || _currentFilePath is null)
        {
            StatusTextBlock.Text = "Open a CSV file before running goto commands.";
            return;
        }

        if (string.IsNullOrWhiteSpace(searchText))
        {
            StatusTextBlock.Text = "Usage: goto word or goto word columnName";
            return;
        }

        var findParts = searchText.Trim()
            .Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
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

        var isSameSearch = searchText.Equals(_lastGotoSearchText, StringComparison.OrdinalIgnoreCase)
                       && string.Equals(searchHeader, _lastGotoSearchHeader, StringComparison.OrdinalIgnoreCase);

        if (!isSameSearch)
        {
            _lastGotoSearchText = searchText;
            _lastGotoSearchHeader = searchHeader;
            _lastGotoRowNumber = 0;
            _lastGotoHeader = null;
        }

        StatusTextBlock.Text = string.IsNullOrWhiteSpace(searchHeader)
            ? $"Searching file for \"{searchText}\"..."
            : searchHeader == Parser.RowNumberKey
                ? $"Searching file row numbers for \"{searchText}\"..."
                : $"Searching file column {searchHeader} for \"{searchText}\"...";

        var match = await Parser.FindNextAsync(
            _currentFilePath,
            searchText,
            searchHeader,
            _lastGotoRowNumber,
            _lastGotoHeader);

        if (match is null && isSameSearch)
        {
            match = await Parser.FindNextAsync(_currentFilePath, searchText, searchHeader);
        }

        if (match is null)
        {
            StatusTextBlock.Text = string.IsNullOrWhiteSpace(searchHeader)
            ? $"No matches found for \"{searchText}\"."
            : searchHeader == Parser.RowNumberKey
                ? $"No matches found for \"{searchText}\" in row numbers."
                : $"No matches found for \"{searchText}\" in column {searchHeader}.";
            return;
        }

        _lastGotoRowNumber = match.Value.RowNumber;
        _lastGotoHeader = match.Value.Header;

        var foundRowNumber = match.Value.RowNumber;
        var windowStart = Math.Max(1, foundRowNumber - 20);
        var windowEnd = windowStart + MaxVisibleRows - 1;
        var rows = await Parser.ReadRangeAsync(_currentFilePath, windowStart, windowEnd, MaxVisibleRows);
        ReplaceVisibleRows(rows);

        var visibleMatch = _visibleRows.FirstOrDefault(row =>
            row.TryGetValue(Parser.RowNumberKey, out var rowNumberText)
            && int.TryParse(rowNumberText, out var rowNumber)
            && rowNumber == foundRowNumber);

        ScrollToMatch(visibleMatch, match.Value.Header);

        var foundColumnText = match.Value.Header == Parser.RowNumberKey
            ? "row numbers"
            : match.Value.Header;
        StatusTextBlock.Text = foundRowNumber == 1
            ? $"Found \"{searchText}\" in the header row, column {foundColumnText}."
            : $"Found \"{searchText}\" at row {foundRowNumber:N0}, column {foundColumnText}.";
}

    /// <summary>
    ///     Handles the filter command by applying or clearing the current grid filter.
    /// </summary>
    private void Command_Filter(string arguments)
    {
        // Applies a filter to the CSV data based on user input
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

        var matchingColumnIndex = FindColumnIndexByNameOrLetter(filterText);
        if (matchingColumnIndex > 0)
        {
            var matchingHeader = Parser.Headers[ToDataColumnIndex(matchingColumnIndex)];
            _gridView.Filter = item =>
            {
                if (item is not Dictionary<string, string> row) return false;
                if (!row.TryGetValue(matchingHeader, out var value)) return false;
                return !string.IsNullOrWhiteSpace(value)
                       && !value.Trim().Equals(@"\N", StringComparison.OrdinalIgnoreCase);
            };
            _gridView.Refresh();
            StatusTextBlock.Text = $"Filtered rows where column \"{matchingHeader}\" is not empty and not \\N.";
            return;
        }

        _gridView.Filter = item =>
        {
            if (item is not Dictionary<string, string> row) return false;

            foreach (var header in Parser.Headers)
            {
                if (!row.TryGetValue(header, out var value)) continue;

                if (value.Contains(filterText, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        };

        _gridView.Refresh();
        StatusTextBlock.Text = $"Filtered rows containing \"{filterText}\".";
    }

    /// <summary>
    ///     Handles the hide command by hiding a single column or a range of columns.
    /// </summary>
    private void Command_Hide(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            StatusTextBlock.Text = "Usage: hide a:x or hide columnName1:columnName2";
            return;
        }

        var rangeParts =
            arguments.Split(':', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
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

        if (startIndex > endIndex) (startIndex, endIndex) = (endIndex, startIndex);
        for (var i = startIndex; i <= endIndex; i++) CsvDataGrid.Columns[i].IsVisible = false;
        StatusTextBlock.Text =
            $"Hidden columns {GetColumnLetter(ToDataColumnIndex(startIndex))}:{GetColumnLetter(ToDataColumnIndex(endIndex))}.";
    }

    /// <summary>
    ///     Handles the unhide command and restores hidden columns when requested.
    /// </summary>
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

    // UI interaction

    /// <summary>
    ///     Runs the command currently entered in the command text box.
    /// </summary>
    private async void RunCommandButton_Click(object? sender, RoutedEventArgs e)
    {
        await ExecuteCommandAsync(CommandTextBox.Text ?? string.Empty);
    }

    /// <summary>
    ///     Runs the entered command when the user presses Enter in the command text box.
    /// </summary>
    private async void CommandTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        await ExecuteCommandAsync(CommandTextBox.Text ?? string.Empty);
        e.Handled = true;
    }

    /// <summary>
    ///     Shows an argument example for a recognized command without changing the user's input.
    /// </summary>
    private void CommandTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var commandText = CommandTextBox.Text ?? string.Empty;
        var command = commandText.TrimStart()
            .Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        CommandExampleTextBlock.Text = command is not null
                                       && CommandExamples.TryGetValue(command, out var example)
            ? example
            : string.Empty;
    }

    /// <summary>
    ///     Opens a file picker, loads the selected CSV or GZIP file, and initializes the data grid columns.
    /// </summary>
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
        if (files.Count == 0) return;
        _currentFilePath = files[0].Path.LocalPath;
        _currentFileName = files[0].Name;
        ResetSearchState();
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

    // UI actions

    /// <summary>
    ///     Makes all data grid columns visible.
    /// </summary>
    private void ShowAllColumns()
    {
        foreach (var column in CsvDataGrid.Columns) column.IsVisible = true;
    }

    /// <summary>
    ///     Scrolls the data grid to the supplied row and column, making the column visible first.
    /// </summary>
    private void ScrollToMatch(Dictionary<string, string>? row, string header)
    {
        var column = header == Parser.RowNumberKey
            ? CsvDataGrid.Columns[0]
            : FindColumnByNameOrLetter(header);

        if (column is null) return;

        column.IsVisible = true;

        if (row is not null && _visibleRows.Contains(row))
        {
            CsvDataGrid.SelectedItem = row;
            CsvDataGrid.ScrollIntoView(row, column);
        }
        else
        {
            CsvDataGrid.ScrollIntoView(_visibleRows.FirstOrDefault(), column);
        }

        CsvDataGrid.Focus();
    }

    /// <summary>
    ///     Loads the requested row range from the current file into the visible grid collection.
    /// </summary>
    private async Task LoadRangeIntoViewAsync(int startRow, int endRow)
    {
        if (_currentFilePath is null || _isLoading) return;
        if (startRow <= 0 || endRow < startRow)
        {
            StatusTextBlock.Text = "Invalid row range.";
            return;
        }

        if (endRow - startRow > MaxVisibleRows) endRow = startRow + MaxVisibleRows;
        _isLoading = true;
        try
        {
            StatusTextBlock.Text = $"Loading rows {startRow:N0}:{endRow:N0}...";
            var rows = await Parser.ReadRangeAsync(_currentFilePath, startRow, endRow, MaxVisibleRows);
            ReplaceVisibleRows(rows);
            _lastFindSearchText = null;
            _lastFindSearchHeader = null;
            _lastFindRowIndex = -1;
            _lastFindHeaderIndex = -1;

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

    /// <summary>
    ///     Replaces the currently visible rows and refreshes the grid view.
    /// </summary>
    private void ReplaceVisibleRows(IEnumerable<Dictionary<string, string>> rows)
    {
        _visibleRows.Clear();
        foreach (var row in rows) _visibleRows.Add(row);
        _gridView.Refresh();
    }
}