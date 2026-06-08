using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CSVoom.app;
using CSVoom.ui;

namespace CSVoom;

public partial class MainWindow : Window
{
    private static readonly IReadOnlyList<string> CommandSuggestions =
    [
        "load ",
        "find ",
        "hide ",
        "unhide"
    ];

    private static readonly IReadOnlyDictionary<string, string> CommandExamples =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["load"] = "Arguments: start(int) / start(int) end(int)",
            ["find"] = "Arguments: word / word columnName",
            ["hide"] = "Arguments: colum / column column",
            ["unhide"] = "Arguments: column / column column"
        };

    private static readonly Parser Parser = new();

    private readonly Dictionary<string, DataGridColumn> _columnsByLetter = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DataGridColumn> _columnsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<string> _commandHistory = [];
    private readonly Dictionary<string, string> _editedSettings = new(StringComparer.OrdinalIgnoreCase);
    private readonly DataGridCollectionView _gridView;
    private readonly ObservableCollection<CsvRow> _visibleRows = [];
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

    /// <summary>
    ///     Opens the Comparer window.
    /// </summary>
    private void ComparerButton_Click(object? sender, RoutedEventArgs e)
    {
        var comparerWindow = new Comparer();
        comparerWindow.Show();
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
        if (CommandHistoryListBox.SelectedItem is not string command) return;
        CommandTextBox.Text = command;
        CloseInlinePanel();
        CommandHistoryListBox.SelectedItem = null;
    }

    private void SaveSettings_Click(object? sender, RoutedEventArgs e)
    {
        Configuration.Save(_editedSettings);
        ApplyConfigurationToUi();
        if (Application.Current is App app) app.UpdateTheme();
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
        var targetRowInput = NavigateRowNumeric.Text;
        var targetColumnInput = NavigateColumnBox.Text;

        if (_visibleRows.Count == 0)
        {
            StatusTextBlock.Text = "No data loaded to navigate.";
            return;
        }

        // Determine target row
        int targetRow;
        if (string.IsNullOrWhiteSpace(targetRowInput))
        {
            targetRow = _visibleRows[0].RowNumber;
        }
        else if (!int.TryParse(targetRowInput, out targetRow))
        {
            StatusTextBlock.Text = "Please enter a valid row number.";
            return;
        }

        // Determine header to search for
        string targetHeader;
        if (string.IsNullOrWhiteSpace(targetColumnInput))
        {
            // Default to the first visible column (index 0 is RowNumberKey)
            var firstVisibleColumn = CsvDataGrid.Columns.FirstOrDefault(c => c.IsVisible);
            if (firstVisibleColumn != null)
            {
                var columnIndex = CsvDataGrid.Columns.IndexOf(firstVisibleColumn);
                if (columnIndex == 0)
                {
                    targetHeader = Parser.RowNumberKey;
                }
                else
                {
                    var dataIndex = ToDataColumnIndex(columnIndex);
                    targetHeader = dataIndex is >= 0 && dataIndex.Value < Parser.Headers.Count
                        ? Parser.Headers[dataIndex.Value]
                        : Parser.RowNumberKey;
                }
            }
            else
            {
                targetHeader = Parser.RowNumberKey;
            }
        }
        else
        {
            var headers = FindHeadersByNameLetterOrRegex(targetColumnInput);
            if (headers.Count == 0)
            {
                StatusTextBlock.Text = $"No matching column found for {targetColumnInput}";
                return;
            }

            targetHeader = headers.FirstOrDefault() ?? Parser.RowNumberKey;
        }

        // Check if row is already loaded
        var rowInView = _visibleRows.FirstOrDefault(r => r.RowNumber == targetRow);

        if (rowInView != null)
            ScrollToMatch(rowInView, targetHeader);
        else
            StatusTextBlock.Text = $"Row {targetRow} is not currently loaded in the view.";
    }

    private void UpdateNavigationRange()
    {
        if (_visibleRows.Count == 0)
        {
            NavigateRowNumeric.ItemsSource = null;
            return;
        }

        var rowNumbers = _visibleRows
            .Select(r => r.RowNumber.ToString())
            .ToList();

        NavigateRowNumeric.ItemsSource = rowNumbers;
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
    ///     Executes a command entered by the user.
    /// </summary>
    private async Task ExecuteCommandAsync(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText) || CsvDataGrid.Columns.Count == 0 || _isBusy ||
            _currentFilePath == null) return;

        CloseInlinePanel();
        SetIsBusy(true);

        using CancellationTokenSource cancellationTokenSource = new();
        _commandCancellationTokenSource = cancellationTokenSource;
        var cancellationToken = cancellationTokenSource.Token;

        try
        {
            var parts = Commands.SplitCommand(commandText.Trim());
            var command = parts[0];
            var arguments = parts[1..];
            if (arguments.Length == 0)
            {
                StatusTextBlock.Text = $"\"{command}\" requires arguments";
                return;
            }

            var isValid = true;
            switch (command.ToLowerInvariant())
            {
                case "load": await Command_LoadAsync(arguments, cancellationToken); break;
                case "find": await Command_FindAsync(arguments, cancellationToken); break;
                case "hide": Command_SetVisibility(arguments, false, cancellationToken); break;
                case "unhide": Command_SetVisibility(arguments, true, cancellationToken); break;
                default:
                    StatusTextBlock.Text = $"Unknown command: {command}";
                    isValid = false;
                    break;
            }

            if (isValid) LogCommand(commandText.Trim());
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

        while (_commandHistory.Count > maxItems) _commandHistory.RemoveAt(_commandHistory.Count - 1);
    }

    /// <summary>
    ///     Handles the load command by parsing a row range and loading it into the view.
    /// </summary>
    private async Task Command_LoadAsync(string[] arguments, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;
        const string errorMessage = "Usage: load (int) / load (int) (int)";
        int startRow;
        switch (arguments.Length)
        {
            case 0:
                StatusTextBlock.Text = errorMessage;
                break;
            case 1: // Load from argument [0] row
                if (!int.TryParse(arguments[0], out startRow) || startRow <= 0)
                {
                    StatusTextBlock.Text = errorMessage;
                    break;
                }

                await LoadRangeIntoViewAsync(startRow, startRow + Configuration.AutoLoadRows - 1, cancellationToken);
                break;
            case 2: // Load between arguments [0] and [1]
                if (!int.TryParse(arguments[0], out startRow) || !int.TryParse(arguments[1], out var endRow) ||
                    startRow <= 0 || endRow < startRow)
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
        var searchHeader = columnSearchValue is null ? null : searchHeaders?.FirstOrDefault();

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

            Progress<int> progress = new(count =>
            {
                StatusTextBlock.Text = $"{searchBaseText} Found {count:N0} match(es) so far.";
            });

            _visibleRows.Clear();
            ObservableCollection<FindResult> foundResults = [];
            HashSet<CsvRow> rowsToShow = [];


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

                if (rowsToShow.Add(match.Row)) _visibleRows.Add(match.Row);

                if (foundResults.Count % 10 == 0) _gridView.Refresh();
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
    private void Command_SetVisibility(string[] arguments, bool state, CancellationToken cancellationToken)
    {
        var errorMessage = $"Error {(state ? "Showing" : "Hiding")} columns. Please check your input and try again.";
        var startIndex = 1;
        var endIndex = CsvDataGrid.Columns.Count;
        if (arguments.Length is < 1 or > 2)
        {
            StatusTextBlock.Text = errorMessage;
            return;
        }

        if (arguments[0] == "all")
        {
            for (var i = startIndex; i <= endIndex; i++)
            {
                if (cancellationToken.IsCancellationRequested) return;
                CsvDataGrid.Columns[i - 1].IsVisible = state;
            }
        }
        else
        {
            var startColIndex = FindColumnIndexByNameOrLetter(arguments[0]);
            if (startColIndex == null)
            {
                StatusTextBlock.Text = $"Column {arguments[0]} not found.";
                return;
            }

            startIndex = startColIndex.Value;

            var endColIndex = arguments.Length == 2 ? FindColumnIndexByNameOrLetter(arguments[1]) : startColIndex;
            if (endColIndex == null)
            {
                StatusTextBlock.Text = $"Column {arguments[1]} not found.";
                return;
            }

            endIndex = endColIndex.Value;

            if (cancellationToken.IsCancellationRequested) return;
            if (startIndex > endIndex) (startIndex, endIndex) = (endIndex, startIndex);

            for (var i = startIndex; i <= endIndex; i++)
            {
                if (cancellationToken.IsCancellationRequested) return;
                CsvDataGrid.Columns[i].IsVisible = state;
            }
        }

        StatusTextBlock.Text =
            $"{(state ? "Showing" : "Hiding")} column(s): {Parser.GetColumnLetter(ToDataColumnIndex(startIndex) ?? 0)} -> {Parser.GetColumnLetter(ToDataColumnIndex(endIndex) ?? 0)}.";
    }

    // UI interaction

    /// <summary>
    ///     Runs the command currently entered in the command text box or cancels the current operation while busy.
    /// </summary>
    private void RunCommandButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            CancelCurrentOperation();
            return;
        }

        _ = ExecuteCommandAsync(CommandTextBox.Text ?? string.Empty);
    }

    /// <summary>
    ///     Runs the entered command when the user presses Enter in the command text box.
    /// </summary>
    private void CommandTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (_isBusy)
        {
            CancelCurrentOperation();
            e.Handled = true;
            return;
        }

        _ = ExecuteCommandAsync(CommandTextBox.Text ?? string.Empty);
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
                        Patterns = Configuration.GetCsvFilePatterns()
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

            StatusTextBlock.Text = $"Loading {_currentFileName}...";

            await Parser.ReadHeadersAsync(_currentFilePath, cancellationToken);

            DataGridUtils.InitializeColumns(CsvDataGrid, Parser, _columnsByName, _columnsByLetter);

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

    /// <summary>
    ///     Exports all visible rows and columns into a csv file.
    /// </summary>
    private async void ExportButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel is null || _isBusy || _currentFilePath is null) return;

        SetIsBusy(true);

        using var cancellationTokenSource = new CancellationTokenSource();
        _commandCancellationTokenSource = cancellationTokenSource;
        var cancellationToken = cancellationTokenSource.Token;

        try
        {
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export CSV",
                DefaultExtension = "csv",
                SuggestedFileName = "export.csv",
                FileTypeChoices =
                [
                    new FilePickerFileType("CSV files")
                    {
                        Patterns = ["*.csv"]
                    }
                ]
            });

            if (file is null) return;

            StatusTextBlock.Text = "Exporting...";

            // Find visible headers (excluding RowNumberKey)
            List<string> visibleHeaders = [];
            foreach (var header in Parser.Headers)
                if (_columnsByName.TryGetValue(header, out var column) && column.IsVisible)
                    visibleHeaders.Add(header);

            // Visible rows
            var rowsToExport = _visibleRows.ToList();

            await Parser.ExportToCsvAsync(file.Path.LocalPath, rowsToExport, visibleHeaders, cancellationToken);
            StatusTextBlock.Text = $"Exported to {file.Name}";
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "Export canceled.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Export failed: {ex.Message}";
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
    ///     Scrolls the data grid to the supplied row and column, making the column visible first.
    /// </summary>
    private void ScrollToMatch(CsvRow? row, string header, string? columnLetter = null)
    {
        var column = header == Parser.RowNumberKey
            ? CsvDataGrid.Columns[0]
            : columnLetter is not null
                ? FindColumnByNameOrLetter(columnLetter)
                : FindColumnByNameOrLetter(header);

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

    private bool VisibleRowsContainsReference(CsvRow row)
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
            List<CsvRow> batch = [];
            await foreach (var row in Parser.ReadRangeAsyncEnumerable(_currentFilePath, startRow, endRow, cancellationToken))
            {
                batch.Add(row);
                rowCount++;
                if (rowCount % 100 != 0) continue;
                StatusTextBlock.Text = $"Loading rows {startRow:N0}:{endRow:N0}... Loaded {rowCount:N0} rows.";
                foreach (var r in batch) _visibleRows.Add(r);
                batch.Clear();
                _gridView.Refresh();
            }

            foreach (var r in batch) _visibleRows.Add(r);
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
    ///     Sets the UI to a busy state or available state.
    /// </summary>
    private void SetIsBusy(bool toStatus)
    {
        RunButton.Content = toStatus ? "Cancel" : "Run";
        OpenButton.IsEnabled = !toStatus;
        _isBusy = toStatus;

        if (!toStatus && _currentFilePath is not null) _ = UpdateTotalRowCountAsync();
    }

    private async Task UpdateTotalRowCountAsync()
    {
        try
        {
            if (_currentFilePath is null) return;
            var rowCount = await Parser.GetRowCountAsync(_currentFilePath);
            var columnCount = Parser.Headers.Count;
            var columnRange = columnCount > 0
                ? $" - Columns: {columnCount} (A-{Parser.GetColumnLetter(columnCount - 1)})"
                : string.Empty;

            TotalRowsTextBlock.Text = $"Rows: {rowCount}{columnRange}";
        }
        catch (Exception ex)
        {
            TotalRowsTextBlock.Text = "Error counting rows";
            StatusTextBlock.Text = $"Error counting rows: {ex.Message}";
        }
    }


    /// <summary>
    ///     Finds a data grid column by its display name or letter.
    /// </summary>
    private DataGridColumn? FindColumnByNameOrLetter(string searchValue)
    {
        if (string.IsNullOrWhiteSpace(searchValue)) return null;

        var normalized = searchValue.Trim();

        if (_columnsByName.TryGetValue(normalized, out var column)) return column;

        var caseInsensitiveMatch = _columnsByName
            .FirstOrDefault(kvp => kvp.Key.Equals(normalized, StringComparison.OrdinalIgnoreCase)).Value;
        if (caseInsensitiveMatch is not null) return caseInsensitiveMatch;

        if (_columnsByLetter.TryGetValue(normalized.ToUpperInvariant(), out var columnByLetter)) return columnByLetter;

        // If numeric, try by index
        if (!int.TryParse(normalized, out var index)) return null;
        long gridIndex = index;
        if (gridIndex >= 0 && gridIndex < CsvDataGrid.Columns.Count)
            return CsvDataGrid.Columns[(int)gridIndex];

        return null;
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

        List<DataGridColumn> matchingColumns = [];

        var isRegex = TryCreateRegexTarget(normalizedSearchValue, out var regex);

        if (!isRegex)
        {
            var exactColumn = FindColumnByNameOrLetter(normalizedSearchValue);
            if (exactColumn is not null && (includeHidden || exactColumn.IsVisible))
            {
                matchingColumns.Add(exactColumn);
                return matchingColumns; // Return only the exact match if found
            }
        }

        for (var columnIndex = 0; columnIndex < CsvDataGrid.Columns.Count; columnIndex++)
        {
            var column = CsvDataGrid.Columns[columnIndex];

            if (!includeHidden && !column.IsVisible) continue;
            if (!isRegex && matchingColumns.Contains(column)) continue;

            var dataColumnIndex = ToDataColumnIndex(columnIndex);
            var dataHeader = columnIndex == 0
                ? Parser.RowNumberKey
                : dataColumnIndex is >= 0 && dataColumnIndex < Parser.Headers.Count
                    ? Parser.Headers[dataColumnIndex.Value]
                    : string.Empty;

            var displayHeader = column.Header?.ToString() ?? string.Empty;

            if (isRegex)
            {
                if (regex.IsMatch(dataHeader) || regex.IsMatch(displayHeader)) matchingColumns.Add(column);
            }
            else
            {
                if (dataHeader.Contains(normalizedSearchValue, StringComparison.OrdinalIgnoreCase) ||
                    displayHeader.Contains(normalizedSearchValue, StringComparison.OrdinalIgnoreCase))
                    matchingColumns.Add(column);
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
        List<string> headers = new(columns.Count);
        foreach (var columnIndex in columns.Select(column => CsvDataGrid.Columns.IndexOf(column)))
            switch (columnIndex)
            {
                case -1:
                    continue;
                case 0:
                    headers.Add(Parser.RowNumberKey);
                    break;
                default:
                {
                    var dataIndex = DataGridUtils.ToDataColumnIndex(columnIndex);
                    if (dataIndex is >= 0 && dataIndex < Parser.Headers.Count)
                        headers.Add(Parser.Headers[dataIndex.Value]);
                    break;
                }
            }

        return headers;
    }

    /// <summary>
    ///     Creates a regex from slash-delimited command target syntax, for example /name|email/.
    /// </summary>
    private bool TryCreateRegexTarget(string searchValue, out Regex regex)
    {
        regex = null!;

        if (!Parser.IsRegexTarget(searchValue) || !Configuration.RegexSearch) return false;

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
    private int? FindColumnIndexByNameOrLetter(string searchValue)
    {
        var column = FindColumnByNameOrLetter(searchValue);
        try
        {
            return column is null ? null : CsvDataGrid.Columns.IndexOf(column);
        }
        catch (Exception e)
        {
            StatusTextBlock.Text = $"Error finding column index: {e.Message}";
            return null;
        }
    }

    /// <summary>
    ///     Converts a grid column index to the corresponding parser data column index.
    /// </summary>
    /// <param name="gridColumnIndex">Value to convert</param>
    /// <returns></returns>
    private static int? ToDataColumnIndex(int gridColumnIndex)
    {
        return DataGridUtils.ToDataColumnIndex(gridColumnIndex);
    }

    private static bool IsRegexTarget(string searchValue)
    {
        return Parser.IsRegexTarget(searchValue);
    }

    /// <summary>
    ///     Creates a reusable matcher for plain text or slash-delimited regex command targets.
    /// </summary>
    private Func<string, bool> CreateSearchMatcher(string searchTarget)
    {
        return Parser.CreateSearchMatcher(searchTarget);
    }

    private sealed class FindResult
    {
        public required CsvRow Row { get; init; }

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