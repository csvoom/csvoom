using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
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
    private const int AutoVisibleRows = 10000;
    private const int RowNumberColumnOffset = 1;

    private static readonly IReadOnlyList<string> CommandSuggestions =
    [
        "load ",
        "find ",
        "filter ",
        "filter clear",
        "hide ",
        "unhide all"
    ];

    private static readonly IReadOnlyDictionary<string, string> CommandExamples =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["load"] = "Arguments: start(int) / start(int) end(int)",
            ["find"] = "Arguments: word / word columnName / columnName",
            ["filter"] = "Arguments: word / columnName / 'clear'",
            ["hide"] = "Arguments: letter letter / columnName1 columnName2",
            ["unhide"] = "Arguments: all"
        };

    private static readonly Parser Parser = new();

    private readonly Dictionary<string, DataGridColumn> _columnsByLetter = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DataGridColumn> _columnsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly DataGridCollectionView _gridView;
    private readonly ObservableCollection<Dictionary<string, string>> _visibleRows = new();

    private string? _currentFileName;
    private string? _currentFilePath;
    private bool _isBusy;
    private Window? _findResultsWindow;
    
    /// <summary>
    ///     Initializes the main window and connects the visible row collection to the data grid.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        CommandTextBox.ItemsSource = CommandSuggestions;
        _gridView = new DataGridCollectionView(_visibleRows);
        CsvDataGrid.ItemsSource = _gridView;
        Closed += (_, _) => CloseFindResultsWindow();
    }
    
    // Commands

    /// <summary>
    ///     Parses and executes a command entered by the user.
    /// </summary>
    private async Task ExecuteCommandAsync(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText) || CsvDataGrid.Columns.Count == 0 || _isBusy) return;

        CloseFindResultsWindow();
        SetIsBusy(true);

        try
        {
            var parts = commandText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0];
            var arguments = parts[1..]; // Get the remaining parts as arguments
            switch (command.ToLower())
            {
                // Check for command type and pass to intended recipient
                case "load":
                    await Command_LoadAsync(arguments);
                    return;
                case "find":
                    Command_Find(arguments);
                    return;
                case "filter":
                    Command_Filter(arguments);
                    return;
                case "hide":
                    Command_Hide(arguments);
                    return;
                case "unhide":
                    Command_Unhide(arguments);
                    return;
                default:
                    StatusTextBlock.Text = $"Unknown command: {command}";
                    return;
            }
        }
        finally
        {
            SetIsBusy(false);
        }
    }

    /// <summary>
    ///     Handles the load command by parsing a row range and loading it into the view.
    /// </summary>
    private async Task Command_LoadAsync(string[] arguments)
    {
        const string errorMessage = "Usage: load (int) / load (int) (int)";
        switch (arguments.Length)
        {
            case 0:
                StatusTextBlock.Text = errorMessage;
                break;
            case 1: // Load from argument [0] row
                if (!int.TryParse(arguments[0], out var startRow) || startRow <= 0)
                {
                    StatusTextBlock.Text = errorMessage;
                    break;
                }

                await LoadRangeIntoViewAsync(startRow, startRow + AutoVisibleRows);
                break;
            case 2: // Load between arguments [0] and [1]
                if (!int.TryParse(arguments[0], out startRow) || !int.TryParse(arguments[1], out var endRow) ||
                    startRow <= 0 || endRow <= startRow)
                {
                    StatusTextBlock.Text = errorMessage;
                    break;
                }

                await LoadRangeIntoViewAsync(startRow, endRow);
                break;
            default:
                StatusTextBlock.Text = errorMessage;
                break;
        }
    }

    /// <summary>
    ///     Handles the find command by locating all matching visible cells and showing them in a popup window.
    /// </summary>
    private void Command_Find(string[] arguments)
    {
        string searchText;
        string? searchHeader = null;

        switch (arguments.Length)
        {
            case 0:
                StatusTextBlock.Text = "Usage: find word or find word columnName";
                return;
            case 1:
                searchText = arguments[0];
                break;
            case 2:
                searchText = arguments[0];
                var columnSearchValue = arguments[1];
                var columnIndex = FindColumnIndexByNameOrLetter(columnSearchValue);
                if (columnIndex < 0)
                {
                    StatusTextBlock.Text = $"Column not found: {columnSearchValue}";
                    return;
                }

                if (!CsvDataGrid.Columns[columnIndex].IsVisible)
                {
                    StatusTextBlock.Text = $"Column is hidden: {columnSearchValue}";
                    return;
                }

                searchHeader = columnIndex == 0
                    ? Parser.RowNumberKey
                    : Parser.Headers[ToDataColumnIndex(columnIndex)];
                break;
            default:
                StatusTextBlock.Text = "Usage: find word or find word columnName";
                return;
        }

        StatusTextBlock.Text = string.IsNullOrWhiteSpace(searchHeader)
            ? $"Searching visible cells for \"{searchText}\"..."
            : searchHeader == Parser.RowNumberKey
                ? $"Searching visible row numbers for \"{searchText}\"..."
                : $"Searching visible column {searchHeader} for \"{searchText}\"...";

        var visibleHeadersToSearch = new List<string>();

        if (string.IsNullOrWhiteSpace(searchHeader))
            foreach (var column in CsvDataGrid.Columns.Where(column => column.IsVisible))
            {
                var columnIndex = CsvDataGrid.Columns.IndexOf(column);
                visibleHeadersToSearch.Add(columnIndex == 0
                    ? Parser.RowNumberKey
                    : Parser.Headers[ToDataColumnIndex(columnIndex)]);
            }
        else
            visibleHeadersToSearch.Add(searchHeader);

        var visibleRowsToSearch = _gridView
            .Cast<Dictionary<string, string>>()
            .ToList();

        var foundInstances = new List<FindResult>();

        foreach (var row in visibleRowsToSearch)
        foreach (var header in visibleHeadersToSearch)
        {
            if (!row.TryGetValue(header, out var value)) continue;
            if (!value.Contains(searchText, StringComparison.OrdinalIgnoreCase)) continue;

            var rowNumberText = row.GetValueOrDefault(Parser.RowNumberKey, "?");

            foundInstances.Add(new FindResult
            {
                Row = row,
                Header = header,
                Value = value,
                RowNumber = rowNumberText
            });
        }

        if (foundInstances.Count == 0)
        {
            StatusTextBlock.Text = string.IsNullOrWhiteSpace(searchHeader)
                ? $"No visible matches found for \"{searchText}\"."
                : searchHeader == Parser.RowNumberKey
                    ? $"No visible matches found for \"{searchText}\" in visible row numbers."
                    : $"No visible matches found for \"{searchText}\" in visible column {searchHeader}.";
            return;
        }

        ShowFindResultsWindow(searchText, foundInstances);

        var firstMatch = foundInstances[0];
        ScrollToMatch(firstMatch.Row, firstMatch.Header);

        var foundColumnText = firstMatch.Header == Parser.RowNumberKey
            ? "row numbers"
            : firstMatch.Header;

        StatusTextBlock.Text =
            $"Found {foundInstances.Count:N0} visible instance(s) of \"{searchText}\". First match at visible row {firstMatch.RowNumber}, column {foundColumnText}.";
    }

    /// <summary>
    ///     Handles the filter command by applying or clearing the current grid filter.
    /// </summary>
    private void Command_Filter(string[] arguments)
    {
        switch (arguments.Length)
        {
            case 0:
                StatusTextBlock.Text = "Usage: filter word, filter columnName, or filter clear";
                break;
            case 1:
                // Remove the filter
                if (arguments[0].Equals("clear", StringComparison.OrdinalIgnoreCase))
                {
                    _gridView.Filter = null!;
                    _gridView.Refresh();
                    StatusTextBlock.Text = "Filter cleared.";
                    break;
                }

                // Applies a filter to clean a column of non-important data
                var matchingColumnIndex = FindColumnIndexByNameOrLetter(arguments[0]);
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
                    break;
                }

                // Applies a filter to only show matching words
                _gridView.Filter = item =>
                {
                    if (item is not Dictionary<string, string> row) return false;
                    foreach (var header in Parser.Headers)
                    {
                        if (!row.TryGetValue(header, out var value)) continue;

                        if (value.Contains(arguments[0], StringComparison.OrdinalIgnoreCase)) return true;
                    }

                    return false;
                };
                _gridView.Refresh();
                StatusTextBlock.Text = $"Filtered rows containing \"{arguments[0]}\".";
                break;
            case 2:
                // Much like the find command, the user can specify both a column name and a value to filter by.
                Console.WriteLine("NOT IMPLEMENTED");
                break;
            default:
                StatusTextBlock.Text = "Usage: filter columnName value";
                break;
        }
    }

    /// <summary>
    ///     Handles the hide command by hiding a single column or a range of columns.
    /// </summary>
    private void Command_Hide(string[] arguments)
    {
        switch (arguments.Length)
        {
            case 0: // No arguments provided
                StatusTextBlock.Text = "Usage: hide a:x or hide columnName1:columnName2";
                break;
            case 1: // Hide single column
                var column = FindColumnByNameOrLetter(arguments[0]);
                if (column is null)
                {
                    StatusTextBlock.Text = $"Column not found: {arguments[0]}";
                    break;
                }

                column.IsVisible = false;
                StatusTextBlock.Text = $"Hidden column {column.Header}.";
                break;
            case 2: // Hide range of columns
                var startIndex = FindColumnIndexByNameOrLetter(arguments[0]);
                var endIndex = FindColumnIndexByNameOrLetter(arguments[1]);
                if (startIndex < 0)
                {
                    StatusTextBlock.Text = $"Column not found: {arguments[0]}";
                    return;
                }

                if (endIndex < 0)
                {
                    StatusTextBlock.Text = $"Column not found: {arguments[1]}";
                    return;
                }

                if (startIndex > endIndex) (startIndex, endIndex) = (endIndex, startIndex);
                for (var i = startIndex; i <= endIndex; i++) CsvDataGrid.Columns[i].IsVisible = false;
                StatusTextBlock.Text =
                    $"Hidden columns {GetColumnLetter(ToDataColumnIndex(startIndex))}:{GetColumnLetter(ToDataColumnIndex(endIndex))}.";
                break;
            default:
                StatusTextBlock.Text = "Usage: hide a b / hide columnName1 columnName2";
                break;
        }
    }

    /// <summary>
    ///     Handles the unhide command and restores hidden columns when requested.
    /// </summary>
    private void Command_Unhide(string[] arguments)
    {
        switch (arguments.Length)
        {
            case 0:
                StatusTextBlock.Text = "Usage: unhide all / unhide a:b / unhide columnName1:columnName2";
                break;
            case 1:
                if (arguments[0].Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    ShowAllColumns();
                    StatusTextBlock.Text = "All columns are visible.";
                    break;
                }

                Console.WriteLine("NOT IMPLEMENTED"); // The user should be capable of selectively unhiding columns
                StatusTextBlock.Text = "Usage: unhide all / unhide a:b / unhide columnName1:columnName2";
                break;
            default:
                StatusTextBlock.Text = "Usage: unhide all / unhide a:b / unhide columnName1:columnName2";
                break;
        }
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
        if (topLevel is null || _isBusy)
        {
            StatusTextBlock.Text = "Unable to open the file picker.";
            return;
        }
        
        SetIsBusy(true);

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
        CsvDataGrid.FrozenColumnCount = 0;
        _currentFilePath = files[0].Path.LocalPath;
        _currentFileName = files[0].Name;
        _gridView.Filter = null;
        _visibleRows.Clear();
        _columnsByName.Clear();
        _columnsByLetter.Clear();
        CsvDataGrid.Columns.Clear();
        StatusTextBlock.Text = $"Loading {_currentFileName}...";
        
        await Parser.ReadHeadersAsync(_currentFilePath);
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

        _ = LoadRangeIntoViewAsync(1, AutoVisibleRows);
        
        SetIsBusy(false);
    }

    // Utility

    ///     Shows all visible find results in a popup window. Selecting a result scrolls the main grid to that cell.
    /// </summary>
    private void ShowFindResultsWindow(string searchText, IReadOnlyList<FindResult> foundInstances)
    {
        CloseFindResultsWindow();

        var resultsListBox = new ListBox
        {
            ItemsSource = foundInstances,
            Margin = new Thickness(8)
        };

        resultsListBox.SelectionChanged += (_, _) =>
        {
            if (resultsListBox.SelectedItem is not FindResult selectedResult) return;

            ScrollToMatch(selectedResult.Row, selectedResult.Header);

            var foundColumnText = selectedResult.Header == Parser.RowNumberKey
                ? "row numbers"
                : selectedResult.Header;

            StatusTextBlock.Text =
                $"Selected \"{searchText}\" at visible row {selectedResult.RowNumber}, column {foundColumnText}.";
        };

        _findResultsWindow = new Window
        {
            Title = $"Find results for \"{searchText}\"",
            Width = 700,
            Height = 500,
            Content = resultsListBox
        };

        _findResultsWindow.Closed += (_, _) => _findResultsWindow = null;
        _findResultsWindow.Show(this);
    }

    /// <summary>
    ///     Closes the find results popup if it is currently open.
    /// </summary>
    private void CloseFindResultsWindow()
    {
        if (_findResultsWindow is null) return;

        var window = _findResultsWindow;
        _findResultsWindow = null;
        window.Close();
    }

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

        if (column is null || !column.IsVisible) return;

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
        if (_currentFilePath is null) return;

        if (startRow <= 0 || endRow < startRow)
        {
            StatusTextBlock.Text = "Invalid row range.";
            return;
        }

        try
        {
            StatusTextBlock.Text = $"Loading rows {startRow:N0}:{endRow:N0}...";
            var rows = await Parser.ReadRangeAsync(_currentFilePath, startRow, endRow);
            ReplaceVisibleRows(rows);

            if (!rows.Any())
            {
                StatusTextBlock.Text = $"No rows found in range {startRow:N0}:{endRow:N0}.";
                return;
            }

            StatusTextBlock.Text = $"Showing {rows.Count:N0} rows from range {startRow:N0}:{endRow:N0}.";
        }
        finally
        {
            CsvDataGrid.FrozenColumnCount = CsvDataGrid.Columns.Count > 0 ? 1 : 0;
        }
    }

    /// <summary>
    ///     Convenience method to set the status of the UI
    /// </summary>
    /// <param name="toStatus">Sets the UI to busy state if true, sets to available state if false</param>
    private void SetIsBusy(bool toStatus)
    {
        RunButton.IsEnabled = !toStatus;
        OpenCsvButton.IsEnabled = !toStatus;
        _isBusy = toStatus;
        Console.WriteLine($": {toStatus}");
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
    /// <param name="searchValue">Value to search by</param>
    /// <returns></returns>
    private DataGridColumn? FindColumnByNameOrLetter(string searchValue)
    {
        if (string.IsNullOrWhiteSpace(searchValue)) return null;

        var normalizedSearchValue = searchValue.Trim();
        if (_columnsByName.TryGetValue(normalizedSearchValue, out var columnByName)) return columnByName;

        return _columnsByLetter.GetValueOrDefault(normalizedSearchValue);
    }

    /// <summary>
    ///     Searches for a data grid column by its display name or spreadsheet-style column letter. <br/>
    ///     Derives from FindColumnByNameOrLetter.
    /// </summary>
    /// <param name="searchValue">Value to search by</param>
    /// <returns></returns>
    private int FindColumnIndexByNameOrLetter(string searchValue)
    {
        var column = FindColumnByNameOrLetter(searchValue);
        return column is null ? -1 : CsvDataGrid.Columns.IndexOf(column);
    }
    
    /// <summary>
    ///     Converts a grid column index to the corresponding parser data column index.
    /// </summary>
    /// <param name="gridColumnIndex">Value to convert</param>
    /// <returns></returns>
    private int ToDataColumnIndex(int gridColumnIndex)
    {
        return gridColumnIndex - RowNumberColumnOffset;
    }

    private sealed class FindResult
    {
        public required Dictionary<string, string> Row { get; init; }
        public required string Header { get; init; }
        public required string Value { get; init; }
        public required string RowNumber { get; init; }

        public override string ToString()
        {
            var columnText = Header == Parser.RowNumberKey ? "row numbers" : Header;
            return $"Row {RowNumber}, Column {columnText}: {Value}";
        }
    }
}