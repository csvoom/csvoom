using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CSVoom.app;

namespace CSVoom;

public partial class MainWindow : Window
{
    private const int RowNumberColumnOffset = 1;

    private static readonly IReadOnlyList<string> CommandSuggestions =
    [
        "load ",
        "find ",
        "hide ",
        "unhide all"
    ];

    private static readonly IReadOnlyDictionary<string, string> CommandExamples =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["load"] = "Arguments: start(int) / start(int) end(int)",
            ["find"] = "Arguments: word / word columnName",
            ["hide"] = "Arguments: letter / columnName / /columnRegex/",
            ["unhide"] = "Arguments: all / letter / columnName / /columnRegex/"
        };

    private static readonly Parser Parser = new();

    private readonly Dictionary<string, DataGridColumn> _columnsByLetter = [];
    private readonly Dictionary<string, DataGridColumn> _columnsByName = [];
    private readonly Dictionary<string, string> _editedSettings = new(StringComparer.OrdinalIgnoreCase);
    private readonly DataGridCollectionView _gridView;
    private readonly ObservableCollection<Dictionary<string, string>> _visibleRows = [];
    private readonly ObservableCollection<string> _commandHistory = [];
    private CancellationTokenSource? _commandCancellationTokenSource = new();

    private string? _currentFileName;
    private string? _currentFilePath;
    private bool _isBusy;

    /// <summary>
    ///     Initializes the main window and connects the visible row collection to the data grid.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        CommandTextBox.ItemsSource = Configuration.MaxCommandHistoryItems > 0
            ? CommandSuggestions.Take(Configuration.MaxCommandHistoryItems).ToArray()
            : null;
        CommandExampleTextBlock.IsVisible = Configuration.ShowCommandExamples;
        _gridView = new DataGridCollectionView(_visibleRows);
        CsvDataGrid.ItemsSource = _gridView;
        CommandHistoryListBox.ItemsSource = _commandHistory;
        Closed += (_, _) =>
        {
            CancelCurrentOperation();
            CloseInlinePanel();
        };
    }

    /// <summary>
    ///     Toggles the inline settings panel.
    /// </summary>
    private void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SettingsPanel.IsVisible)
        {
            CloseInlinePanel();
            return;
        }

        ShowInlinePanel(SettingsPanel);
        _editedSettings.Clear();
        SettingsFieldsContainer.Children.Clear();

        foreach (var setting in Configuration.Settings)
        {
            var currentValue = Configuration.GetRawValue(setting.Key);

            SettingsFieldsContainer.Children.Add(new TextBlock
            {
                Text = $"{setting.Key} ({setting.Type})",
                FontWeight = FontWeight.Bold
            });

            SettingsFieldsContainer.Children.Add(new TextBlock
            {
                Text = setting.Description,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7
            });

            if (setting.Type.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
            {
                var checkBox = new CheckBox
                {
                    IsChecked = bool.TryParse(currentValue, out var boolValue) && boolValue,
                    Content = setting.Key
                };

                checkBox.IsCheckedChanged += (_, _) =>
                {
                    _editedSettings[setting.Key] = (checkBox.IsChecked == true).ToString();
                };

                _editedSettings[setting.Key] = (checkBox.IsChecked == true).ToString();
                SettingsFieldsContainer.Children.Add(checkBox);
            }
            else
            {
                var textBox = new TextBox
                {
                    Text = currentValue,
                    PlaceholderText = setting.DefaultValue
                };

                textBox.TextChanged += (_, _) => { _editedSettings[setting.Key] = textBox.Text ?? string.Empty; };

                _editedSettings[setting.Key] = textBox.Text ?? string.Empty;
                SettingsFieldsContainer.Children.Add(textBox);
            }
        }
    }

    private void CommandHistoryButton_Click(object? sender, RoutedEventArgs e)
    {
        if (CommandHistoryPanel.IsVisible)
        {
            CloseInlinePanel();
            return;
        }

        ShowInlinePanel(CommandHistoryPanel);
    }

    private void CommandHistoryListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CommandHistoryListBox.SelectedItem is string command)
        {
            CommandTextBox.Text = command;
            CloseInlinePanel();
            CommandHistoryListBox.SelectedItem = null;
        }
    }

    private void SaveSettings_Click(object? sender, RoutedEventArgs e)
    {
        Configuration.Save(_editedSettings);
        ApplyConfigurationToUi();
        CloseInlinePanel();
    }

    private void NavigateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (NavigatePanel.IsVisible)
        {
            CloseInlinePanel();
            return;
        }

        ShowInlinePanel(NavigatePanel);
    }

    private void NavigateGo_Click(object? sender, RoutedEventArgs e)
    {
        var targetRow = (int)(NavigateRowNumeric.Value ?? 1);
        var targetColumnInput = NavigateColumnBox.Text;

        // Determine header to search for
        string targetHeader;
        if (string.IsNullOrWhiteSpace(targetColumnInput))
        {
            targetHeader = Parser.RowNumberKey;
        }
        else
        {
            var headers = FindHeadersByNameLetterOrRegex(targetColumnInput);
            if (headers.Count == 0)
            {
                StatusTextBlock.Text = $"No matching column found for {targetColumnInput}";
                return;
            }
            targetHeader = headers[0];
        }

        // Check if row is already loaded
        var rowInView = _visibleRows.FirstOrDefault(r =>
            r.TryGetValue(Parser.RowNumberKey, out var val) && int.TryParse(val, out var num) && num == targetRow);

        if (rowInView != null)
        {
            ScrollToMatch(rowInView, targetHeader);
        }
        else
        {
            StatusTextBlock.Text = $"Row {targetRow} is not currently loaded in the view.";
        }
    }

    private void UpdateNavigationRange()
    {
        if (_visibleRows.Count == 0)
        {
            NavigateRowNumeric.Minimum = 1;
            NavigateRowNumeric.Maximum = 1;
            NavigateRowNumeric.Value = 1;
            return;
        }

        var rowNumbers = _visibleRows
            .Select(r => r.TryGetValue(Parser.RowNumberKey, out var val) && int.TryParse(val, out var num) ? num : (int?)null)
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .ToList();

        if (rowNumbers.Count > 0)
        {
            var min = rowNumbers.Min();
            var max = rowNumbers.Max();
            NavigateRowNumeric.Minimum = min;
            NavigateRowNumeric.Maximum = max;
            // Snap current value to range if needed
            if (NavigateRowNumeric.Value < min) NavigateRowNumeric.Value = min;
            if (NavigateRowNumeric.Value > max) NavigateRowNumeric.Value = max;
        }
    }
    
    private void CloseInlinePanel_Click(object? sender, RoutedEventArgs e)
    {
        CloseInlinePanel();
    }

    private void ShowInlinePanel(Control panel)
    {
        InlinePanelContainer.IsVisible = true;
        SettingsPanel.IsVisible = panel == SettingsPanel;
        NavigatePanel.IsVisible = panel == NavigatePanel;
        CommandHistoryPanel.IsVisible = panel == CommandHistoryPanel;
    }

    private void CloseInlinePanel()
    {
        InlinePanelContainer.IsVisible = false;
        SettingsPanel.IsVisible = false;
        NavigatePanel.IsVisible = false;
        CommandHistoryPanel.IsVisible = false;
    }

    /// <summary>
    ///     Applies configuration values that affect the already-created main window controls.
    /// </summary>
    private void ApplyConfigurationToUi()
    {
        CommandTextBox.ItemsSource = Configuration.MaxCommandHistoryItems > 0
            ? CommandSuggestions.Take(Configuration.MaxCommandHistoryItems).ToArray()
            : null;

        CommandExampleTextBlock.IsVisible = Configuration.ShowCommandExamples;

        if (!Configuration.ShowCommandExamples)
            CommandExampleTextBlock.Text = string.Empty;
        else
            CommandTextBox_TextChanged(CommandTextBox, new TextChangedEventArgs(TextBox.TextChangedEvent));
    }

    // Commands

    /// <summary>
    ///     Parses and executes a command entered by the user.
    /// </summary>
    private async Task ExecuteCommandAsync(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText) || CsvDataGrid.Columns.Count == 0 || _isBusy ||
            _currentFilePath == null) return;

        CloseInlinePanel();
        SetIsBusy(true);

        using var cancellationTokenSource = new CancellationTokenSource();
        _commandCancellationTokenSource = cancellationTokenSource;
        var cancellationToken = cancellationTokenSource.Token;

        try
        {
            var parts = commandText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0];
            var arguments = parts[1..];
            if (arguments.Length == 0)
            {
                StatusTextBlock.Text = $"\"{command}\" requires arguments";
                return;
            }

            bool isValid = false;
            if (command.Equals("load", StringComparison.OrdinalIgnoreCase))
            {
                await Command_LoadAsync(arguments, cancellationToken);
                isValid = true;
            }
            else if (command.Equals("find", StringComparison.OrdinalIgnoreCase))
            {
                await Command_FindAsync(arguments, cancellationToken);
                isValid = true;
            }
            else if (command.Equals("hide", StringComparison.OrdinalIgnoreCase))
            {
                Command_Hide(arguments, cancellationToken);
                isValid = true;
            }
            else if (command.Equals("unhide", StringComparison.OrdinalIgnoreCase))
            {
                Command_Unhide(arguments, cancellationToken);
                isValid = true;
            }
            else
            {
                StatusTextBlock.Text = $"Unknown command: {command}";
            }

            if (isValid)
            {
                LogCommand(commandText.Trim());
            }
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "Operation canceled.";
        }
        finally
        {
            if (ReferenceEquals(_commandCancellationTokenSource, cancellationTokenSource))
                _commandCancellationTokenSource = null;

            SetIsBusy(false);
        }
    }

    private void LogCommand(string command)
    {
        var maxItems = Configuration.MaxCommandHistoryItems;
        if (maxItems <= 0) return;

        _commandHistory.Remove(command);
        _commandHistory.Insert(0, command);

        while (_commandHistory.Count > maxItems)
        {
            _commandHistory.RemoveAt(_commandHistory.Count - 1);
        }
    }

    /// <summary>
    ///     Handles the load command by parsing a row range and loading it into the view.
    /// </summary>
    private async Task Command_LoadAsync(string[] arguments, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;
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

                await LoadRangeIntoViewAsync(startRow, startRow + Configuration.AutoLoadRows, cancellationToken);
                break;
            case 2: // Load between arguments [0] and [1]
                if (!int.TryParse(arguments[0], out startRow) || !int.TryParse(arguments[1], out var endRow) ||
                    startRow <= 0 || endRow <= startRow)
                {
                    StatusTextBlock.Text = errorMessage;
                    break;
                }

                await LoadRangeIntoViewAsync(startRow, endRow, cancellationToken);
                break;
            default:
                StatusTextBlock.Text = errorMessage;
                break;
        }
    }

    /// <summary>
    ///     Handles the find command by locating all matching cells in the current file and showing them in a popup window.
    /// </summary>
    private async Task Command_FindAsync(string[] arguments, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;
        var searchText = arguments[0];
        var searchDescription = IsRegexTarget(searchText) ? $"regex {searchText}" : $"\"{searchText}\"";
        
        var columnSearchValue = arguments.Length >= 2 ? arguments[1] : null;
        var searchHeaders = columnSearchValue is null ? null : FindHeadersByNameLetterOrRegex(columnSearchValue);
        var searchHeader = columnSearchValue is null ? null : searchHeaders?[0];

        if (columnSearchValue is not null && searchHeader is null)
        {
            StatusTextBlock.Text = $"No matching column found for {columnSearchValue}";
            return;
        }

        var searchMatcher = CreateSearchMatcher(searchText);
        var searchBaseText = searchHeader switch
        {
            null => $"Searching file for {searchDescription}...",
            Parser.RowNumberKey => $"Searching file row numbers for {searchDescription}...",
            _ => $"Searching file column {searchHeader} for {searchDescription}..."
        };

        if (_currentFilePath != null)
        {
            StatusTextBlock.Text = searchBaseText;

            var progress = new Progress<int>(count =>
            {
                StatusTextBlock.Text = $"{searchBaseText} Found {count:N0} match(es) so far.";
            });

            _visibleRows.Clear();
            var foundResults = new ObservableCollection<FindResult>();
            var rowsToShow = new HashSet<Dictionary<string, string>>(ReferenceEqualityComparer.Instance);
            

            await foreach (var match in Parser.ReadMatchesAsyncEnumerable(
                _currentFilePath,
                searchMatcher,
                searchHeaders,
                Configuration.AutoFindRows,
                progress,
                cancellationToken))
            {
                var result = new FindResult
                {
                    Row = match.Row,
                    Header = match.Header,
                    Value = match.Value,
                    RowNumber = match.RowNumber.ToString()
                };
                foundResults.Add(result);

                if (rowsToShow.Add(match.Row))
                {
                    _visibleRows.Add(match.Row);
                }

                if (foundResults.Count % 10 == 0)
                {
                    _gridView.Refresh();
                }
            }
            _gridView.Refresh();
            UpdateNavigationRange();

            if (foundResults.Count == 0)
            {
                CloseInlinePanel();
                StatusTextBlock.Text = searchHeader switch
                {
                    null => $"No matches found for {searchDescription}.",
                    Parser.RowNumberKey => $"No matches found for {searchDescription} in row numbers.",
                    _ => $"No matches found for {searchDescription} in column {searchHeader}."
                };
                return;
            }
            
            StatusTextBlock.Text =
                $"Found {foundResults.Count:N0} instance(s) of {searchDescription}.";
        }
        CsvDataGrid.Focus();
    }

    /// <summary>
    ///     Handles the hide command by hiding a single column or a range of columns.
    /// </summary>
    private void Command_Hide(string[] arguments, CancellationToken cancellationToken)
    {
        const string errorMessage =
            "Error hiding columns. Please check your input and try again.\nUsage: \"hide a b\" or \"hide columnName1 columnName2\"";
        if (arguments.Length is < 1 or > 2)
        {
            StatusTextBlock.Text = errorMessage;
            return;
        }
        var startIndex = FindColumnIndexByNameOrLetter(arguments[0]);
        var endIndex = arguments.Length == 2 ? FindColumnIndexByNameOrLetter(arguments[1]) : startIndex;
        
        if (startIndex == -1 || endIndex == -1)
        {
            var missing = new List<string>();
            if (startIndex == -1) missing.Add(arguments[0]);
            if (endIndex == -1 && arguments.Length == 2 && arguments[1] != arguments[0]) missing.Add(arguments[1]);
            
            StatusTextBlock.Text = $"Column(s) not found: {string.Join(", ", missing)}";
            return;
        }
        
        if (cancellationToken.IsCancellationRequested) return;
        if (startIndex > endIndex) (startIndex, endIndex) = (endIndex, startIndex);
        
        for (var i = startIndex; i <= endIndex; i++)
        { 
            if (cancellationToken.IsCancellationRequested) return; 
            CsvDataGrid.Columns[i].IsVisible = false;
        }
        StatusTextBlock.Text =
            $"Hidden columns: {GetColumnLetter(ToDataColumnIndex(startIndex))} -> {GetColumnLetter(ToDataColumnIndex(endIndex))}.";
    }

    /// <summary>
    ///     Handles the unhide command and restores hidden columns when requested.
    /// </summary>
    private void Command_Unhide(string[] arguments, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;
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

                var columnsToUnhide = FindColumnsByNameLetterOrRegex(arguments[0], true);
                if (cancellationToken.IsCancellationRequested) return;
                if (columnsToUnhide.Count == 0)
                {
                    StatusTextBlock.Text = $"Column target not found: {arguments[0]}";
                    break;
                }

                foreach (var columnToUnhide in columnsToUnhide)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    columnToUnhide.IsVisible = true;
                }

                StatusTextBlock.Text = columnsToUnhide.Count == 1
                    ? $"Unhidden column {columnsToUnhide[0].Header}."
                    : $"Unhidden {columnsToUnhide.Count} columns matching {arguments[0]}.";
                break;
            default:
                StatusTextBlock.Text = "Usage: unhide all / unhide a b / unhide columnName1 columnName2";
                break;
        }
    }

    // UI interaction

    /// <summary>
    ///     Runs the command currently entered in the command text box or cancels the current operation while busy.
    /// </summary>
    private async void RunCommandButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            CancelCurrentOperation();
            return;
        }

        await ExecuteCommandAsync(CommandTextBox.Text ?? string.Empty);
    }

    /// <summary>
    ///     Runs the entered command when the user presses Enter in the command text box.
    /// </summary>
    private async void CommandTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (_isBusy)
        {
            CancelCurrentOperation();
            e.Handled = true;
            return;
        }

        await ExecuteCommandAsync(CommandTextBox.Text ?? string.Empty);
        e.Handled = true;
    }

    /// <summary>
    ///     Shows an argument example for a recognized command without changing the user's input.
    /// </summary>
    private void CommandTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!Configuration.ShowCommandExamples)
        {
            CommandExampleTextBlock.Text = string.Empty;
            return;
        }

        var commandText = CommandTextBox.Text ?? string.Empty;
        var trimmedCommandText = commandText.TrimStart();
        var separatorIndex = trimmedCommandText.IndexOf(' ');
        var command = separatorIndex < 0
            ? trimmedCommandText
            : trimmedCommandText[..separatorIndex];

        CommandExampleTextBlock.Text = command.Length > 0 && CommandExamples.TryGetValue(command, out var example)
            ? example
            : string.Empty;
    }

    /// <summary>
    ///     Opens a file picker, loads the selected CSV or GZIP file, and initializes the data grid columns.
    /// </summary>
    private async void OpenButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel is null || _isBusy)
        {
            StatusTextBlock.Text = "Unable to open the file picker.";
            return;
        }

        SetIsBusy(true);

        using var cancellationTokenSource = new CancellationTokenSource();
        _commandCancellationTokenSource = cancellationTokenSource;
        var cancellationToken = cancellationTokenSource.Token;

        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open CSV file",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("CSV files")
                    {
                        Patterns = GetCsvFilePatterns()
                    }
                ]
            });

            if (files.Count == 0) return;
            CsvDataGrid.FrozenColumnCount = 0;
            _currentFilePath = files[0].Path.LocalPath;
            _currentFileName = files[0].Name;
            MainWindowElement.Title = $"{_currentFileName}";
            _gridView.Filter = null!;
            _visibleRows.Clear();
            _columnsByName.Clear();
            _columnsByLetter.Clear();
            CsvDataGrid.Columns.Clear();
            StatusTextBlock.Text = $"Loading {_currentFileName}...";

            await Parser.ReadHeadersAsync(_currentFilePath, cancellationToken);
            
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
            
            NavigateColumnBox.ItemsSource = Parser.Headers;

            await LoadRangeIntoViewAsync(1, Configuration.AutoLoadRows, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "Operation canceled.";
        }
        finally
        {
            if (ReferenceEquals(_commandCancellationTokenSource, cancellationTokenSource))
                _commandCancellationTokenSource = null;

            SetIsBusy(false);
        }
    }

    // Utility

    /// <summary>
    ///     Cancels the currently running command or parser operation.
    /// </summary>
    private void CancelCurrentOperation()
    {
        if (!_isBusy) return;

        StatusTextBlock.Text = "Canceling operation...";
        _commandCancellationTokenSource?.Cancel();
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
    private void ScrollToMatch(Dictionary<string, string>? row, string header, string? columnLetter = null)
    {
        var column = header == Parser.RowNumberKey
            ? CsvDataGrid.Columns[0]
            : columnLetter is not null ? FindColumnByNameOrLetter(columnLetter) : FindColumnByNameOrLetter(header);

        if (column is null || !column.IsVisible) return;

        if (row is not null && VisibleRowsContainsReference(row))
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

    private bool VisibleRowsContainsReference(Dictionary<string, string> row)
    {
        return _visibleRows.Any(visibleRow => ReferenceEquals(visibleRow, row));
    }

    /// <summary>
    ///     Loads the requested row range from the current file into the visible grid collection.
    /// </summary>
    private async Task LoadRangeIntoViewAsync(
        int startRow,
        int endRow,
        CancellationToken cancellationToken = default)
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
            _visibleRows.Clear();
            var rowCount = 0;
            await foreach (var row in Parser.ReadRangeAsyncEnumerable(_currentFilePath, startRow, endRow, cancellationToken))
            {
                _visibleRows.Add(row);
                rowCount++;
                if (rowCount % 100 == 0)
                {
                    StatusTextBlock.Text = $"Loading rows {startRow:N0}:{endRow:N0}... Loaded {rowCount:N0} rows.";
                    _gridView.Refresh();
                }
            }
            _gridView.Refresh();
            UpdateNavigationRange();

            if (rowCount == 0)
            {
                StatusTextBlock.Text = $"No rows found in range {startRow:N0} {endRow:N0}.";
                return;
            }

            StatusTextBlock.Text = $"Showing {rowCount:N0} rows from range {startRow:N0}:{endRow:N0}.";
        }
        finally
        {
            CsvDataGrid.FrozenColumnCount = CsvDataGrid.Columns.Count > 0
                ? 1
                : 0;
        }
    }

    /// <summary>
    ///     Convenience method to set the status of the UI
    /// </summary>
    /// <param name="toStatus">Sets the UI to busy state if true, sets to available state if false</param>
    private void SetIsBusy(bool toStatus)
    {
        RunButton.Content = toStatus ? "Cancel" : "Run";
        OpenButton.IsEnabled = !toStatus;
        _isBusy = toStatus;
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
        
        // Exact match first
        if (_columnsByName.TryGetValue(normalizedSearchValue, out var columnByName))
        {
            return columnByName;
        }

        // Case-insensitive name match
        var caseInsensitiveMatch = _columnsByName.FirstOrDefault(kvp => 
            kvp.Key.Equals(normalizedSearchValue, StringComparison.OrdinalIgnoreCase)).Value;
        
        if (caseInsensitiveMatch is not null)
        {
            return caseInsensitiveMatch;
        }

        var upperSearchValue = normalizedSearchValue.ToUpperInvariant();
        return _columnsByLetter.GetValueOrDefault(upperSearchValue);
    }

    /// <summary>
    ///     Finds columns by exact column name, spreadsheet-style letter, or a slash-delimited regex.
    ///     Regex targets are matched against both raw CSV headers and displayed column headers.
    /// </summary>
    private List<DataGridColumn> FindColumnsByNameLetterOrRegex(
        string searchValue,
        bool includeHidden = false)
    {
        if (string.IsNullOrWhiteSpace(searchValue)) return [];

        var normalizedSearchValue = searchValue.Trim();

        var matchingColumns = new List<DataGridColumn>();

        var isRegex = TryCreateRegexTarget(normalizedSearchValue, out var regex);

        if (!isRegex)
        {
            var exactColumn = FindColumnByNameOrLetter(normalizedSearchValue);
            if (exactColumn is not null && (includeHidden || exactColumn.IsVisible))
            {
                matchingColumns.Add(exactColumn);
            }
        }

        for (var columnIndex = 0; columnIndex < CsvDataGrid.Columns.Count; columnIndex++)
        {
            var column = CsvDataGrid.Columns[columnIndex];

            if (!includeHidden && !column.IsVisible) continue;
            if (!isRegex && matchingColumns.Contains(column)) continue;

            var dataHeader = columnIndex == 0
                ? Parser.RowNumberKey
                : Parser.Headers[ToDataColumnIndex(columnIndex)];

            var displayHeader = column.Header?.ToString() ?? string.Empty;

            if (isRegex)
            {
                if (regex.IsMatch(dataHeader) || regex.IsMatch(displayHeader)) matchingColumns.Add(column);
            }
            else
            {
                if (dataHeader.Contains(normalizedSearchValue, StringComparison.OrdinalIgnoreCase) ||
                    displayHeader.Contains(normalizedSearchValue, StringComparison.OrdinalIgnoreCase))
                {
                    matchingColumns.Add(column);
                }
            }
        }
        return matchingColumns;
    }

    /// <summary>
    ///     Finds parser headers by exact column name, spreadsheet-style letter, or a slash-delimited regex.
    /// </summary>
    private List<string> FindHeadersByNameLetterOrRegex(string searchValue)
    {
        var columns = FindColumnsByNameLetterOrRegex(searchValue, true);
        var headers = new List<string>(columns.Count);
        headers.AddRange(columns.Select(column => CsvDataGrid.Columns.IndexOf(column))
            .Select(columnIndex => columnIndex == 0
                ? Parser.RowNumberKey
                : Parser.Headers[ToDataColumnIndex(columnIndex)]));

        return headers;
    }

    /// <summary>
    ///     Creates a regex from slash-delimited command target syntax, for example /name|email/.
    /// </summary>
    private bool TryCreateRegexTarget(string searchValue, out Regex regex)
    {
        regex = null!;

        if (searchValue.Length < 2 || searchValue[0] != '/' || searchValue[^1] != '/' ||
            !Configuration.RegexSearch) return false;

        var pattern = searchValue[1..^1];
        var regexOptions = RegexOptions.CultureInvariant;

        if (Configuration.CaseInsensitiveSearch)
            regexOptions |= RegexOptions.IgnoreCase;

        try
        {
            regex = new Regex(
                pattern,
                regexOptions,
                TimeSpan.FromMilliseconds(Configuration.RegexTimeoutMilliseconds));

            return true;
        }
        catch (ArgumentException exception)
        {
            StatusTextBlock.Text = $"Invalid regex target: {exception.Message}";
            return false;
        }
    }

    /// <summary>
    ///     Searches for a data grid column by its display name or spreadsheet-style column letter. <br />
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
    private static int ToDataColumnIndex(int gridColumnIndex)
    {
        return gridColumnIndex - RowNumberColumnOffset;
    }

    private static bool IsRegexTarget(string searchValue)
    {
        return searchValue is ['/', _, ..] && searchValue[^1] == '/';
    }

    /// <summary>
    ///     Creates a reusable matcher for plain text or slash-delimited regex command targets.
    /// </summary>
    private Func<string, bool> CreateSearchMatcher(string searchTarget)
    {
        return TryCreateRegexTarget(searchTarget, out var regex)
            ? regex.IsMatch
            : value => value.Contains(
                searchTarget,
                Configuration.CaseInsensitiveSearch
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
    }

    private static string[] GetCsvFilePatterns()
    {
        return Configuration.CsvFilePatterns
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private sealed class FindResult
    {
        public required Dictionary<string, string>? Row
        {
            get;
            init;
        }
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