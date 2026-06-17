using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSVoom.app;

namespace CSVoom.ui.ViewModels;

/// <summary>
///     The main view model for the application.
/// </summary>
public class MainWindowViewModel : ViewModelBase
{
    private string? _currentFilePath;

    /// <summary>
    ///     Gets the command history.
    /// </summary>
    public ObservableCollection<string> CommandHistory { get; } = [];

    /// <summary>
    ///     Gets the rows currently visible in the grid.
    /// </summary>
    public ObservableCollection<CsvRow> VisibleRows { get; } = [];

    /// <summary>
    ///     Gets the available column headers for navigation.
    /// </summary>
    public ObservableCollection<string> NavigateColumnOptions { get; } = [];

    /// <summary>
    ///     Gets the search results.
    /// </summary>
    public ObservableCollection<DifferenceDetail> SearchResults { get; } = [];

    /// <summary>
    ///     Gets or sets the window title.
    /// </summary>
    public string WindowTitle
    {
        get;
        set => SetField(ref field, value);
    } = "CSVoom";

    /// <summary>
    ///     Gets or sets the command text entered by the user.
    /// </summary>
    public string CommandText
    {
        get;
        set => SetField(ref field, value);
    } = "";

    /// <summary>
    ///     Gets or sets the status text displayed in the UI.
    /// </summary>
    public string StatusText
    {
        get;
        set => SetField(ref field, value);
    } = "Choose a CSV file to display its contents.";

    /// <summary>
    ///     Gets or sets the total rows count text.
    /// </summary>
    public string TotalRowsText
    {
        get;
        set => SetField(ref field, value);
    } = "";


    /// <summary>
    ///     Gets or sets the application version text.
    /// </summary>
    public string VersionText
    {
        get;
        set => SetField(ref field, value);
    }

    private bool IsBusy
    {
        get;
        set
        {
            if (!SetField(ref field, value)) return;
            OnPropertyChanged(nameof(CanRunCommand));
            OnPropertyChanged(nameof(RunButtonText));
        }
    }

    /// <summary>
    ///     Gets a value indicating whether a command can be run.
    /// </summary>
    public bool CanRunCommand => true;

    /// <summary>
    ///     Gets the text to display on the run button.
    /// </summary>
    public string RunButtonText => IsBusy ? "Cancel" : "Run";

    /// <summary>
    ///     Gets or sets a value indicating whether the inline panel is visible.
    /// </summary>
    public bool InlinePanelVisible
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    ///     Gets or sets a value indicating whether the settings panel is visible.
    /// </summary>
    public bool SettingsPanelVisible
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    ///     Gets or sets a value indicating whether the navigation panel is visible.
    /// </summary>
    public bool NavigatePanelVisible
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    ///     Gets or sets a value indicating whether the command history panel is visible.
    /// </summary>
    public bool CommandHistoryPanelVisible
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    ///     Gets or sets a value indicating whether the search results panel is visible.
    /// </summary>
    public bool SearchResultsVisible
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    ///     Gets or sets the selected column for navigation.
    /// </summary>
    public string? SelectedNavigateColumn
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    ///     Gets or sets the selected row for navigation.
    /// </summary>
    public string? SelectedNavigateRow
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    ///     Command to run the current command text.
    /// </summary>
    public AsyncRelayCommand RunCommand { get; }

    /// <summary>
    ///     Command to open a CSV file.
    /// </summary>
    public AsyncRelayCommand OpenCommand { get; }

    /// <summary>
    ///     Command to export the current view to a CSV file.
    /// </summary>
    public AsyncRelayCommand ExportCommand { get; }

    /// <summary>
    ///     Command to show the settings panel.
    /// </summary>
    public RelayCommand SettingsCommand { get; }
    public RelayCommand ComparerCommand { get; }
    public RelayCommand NavigateCommand { get; }
    public RelayCommand CommandHistoryCommand { get; }
    public RelayCommand CloseInlinePanelCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand SearchResultsCommand { get; }
    public RelayCommand NavigateToMatchCommand { get; }
    public AsyncRelayCommand NavigateGoCommand { get; }

    private static readonly IReadOnlyList<string> CommandSuggestions =
    [
        "load ",
        "find ",
        "hide ",
        "unhide"
    ];


    public string[]? AutoCompleteOptions => Configuration.MaxCommandHistoryItems > 0
        ? CommandSuggestions.Take(Configuration.MaxCommandHistoryItems).ToArray()
        : null;


    public event Func<Task<string?>>? RequestOpenFile;
    public event Action? RequestShowSettings;
    public event Func<Task<string?>>? RequestSaveFile;
    public event Func<Task>? RequestSaveSettings;
    public event Action? RequestShowComparer;
    public event Action<CsvRow?, string, string?>? RequestScrollToMatch;
    public event Action<Parser>? RequestColumnInitialization;
    public event Action<string[], bool, CancellationToken>? RequestSetVisibility;
    public event Func<string, List<string>>? RequestResolveHeaders;

    public Parser CurrentParser { get; } = new();
    private CancellationTokenSource? _currentOperationCts;

    public MainWindowViewModel()
    {
        RunCommand = new AsyncRelayCommand(_ => ExecuteCommandAsync(CommandText), allowConcurrent: true);
        OpenCommand = new AsyncRelayCommand(_ => OpenFileAsync());
        ExportCommand = new AsyncRelayCommand(_ => ExportFileAsync());
        SettingsCommand = new RelayCommand(_ => ShowSettings());
        ComparerCommand = new RelayCommand(_ => RequestShowComparer?.Invoke());
        NavigateCommand = new RelayCommand(_ => ShowNavigate());
        CommandHistoryCommand = new RelayCommand(_ => ShowCommandHistory());
        SearchResultsCommand = new RelayCommand(_ => ShowSearchResults());
        CloseInlinePanelCommand = new RelayCommand(_ => CloseInlinePanel());
        SaveSettingsCommand = new RelayCommand(_ => RequestSaveSettings?.Invoke());
        NavigateToMatchCommand = new RelayCommand(obj => NavigateToMatch((DifferenceDetail)obj!));
        NavigateGoCommand = new AsyncRelayCommand(_ => NavigateGoAsync());
        
        VersionText = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";
    }

    public void CloseInlinePanel()
    {
        InlinePanelVisible = false;
        SettingsPanelVisible = false;
        NavigatePanelVisible = false;
        CommandHistoryPanelVisible = false;
        SearchResultsVisible = false;
    }

    private void ShowSettings()
    {
        if (SettingsPanelVisible)
        {
            CloseInlinePanel();
        }
        else
        {
            CloseInlinePanel();
            SettingsPanelVisible = true;
            InlinePanelVisible = true;
            RequestShowSettings?.Invoke();
        }
    }


    private void ShowNavigate()
    {
        if (NavigatePanelVisible)
        {
            CloseInlinePanel();
        }
        else
        {
            CloseInlinePanel();
            NavigatePanelVisible = true;
            InlinePanelVisible = true;
        }
    }

    private void ShowCommandHistory()
    {
        if (CommandHistoryPanelVisible)
        {
            CloseInlinePanel();
        }
        else
        {
            CloseInlinePanel();
            CommandHistoryPanelVisible = true;
            InlinePanelVisible = true;
        }
    }

    private void ShowSearchResults()
    {
        if (SearchResultsVisible)
        {
            CloseInlinePanel();
        }
        else
        {
            CloseInlinePanel();
            SearchResultsVisible = true;
            InlinePanelVisible = true;
        }
    }

    private async Task OpenFileAsync()
    {
        if (RequestOpenFile != null)
        {
            var filePath = await RequestOpenFile();
            if (filePath != null)
            {
                _currentOperationCts = new CancellationTokenSource();
                var ct = _currentOperationCts.Token;
                IsBusy = true;
                try
                {
                    StatusText = $"Loading {filePath}...";
                    _currentFilePath = filePath;
                    WindowTitle = $"{System.IO.Path.GetFileName(filePath)}";
                    await CurrentParser.ReadHeadersAsync(filePath, ct);
                    NavigateColumnOptions.Clear();
                    foreach (var header in CurrentParser.Headers) NavigateColumnOptions.Add(header);
                    RequestColumnInitialization?.Invoke(CurrentParser);
                    await LoadRangeIntoViewAsync(1, Configuration.AutoLoadRows, ct);
                    StatusText = $"Loaded {filePath}.";
                }
                catch (OperationCanceledException)
                {
                    StatusText = "Operation canceled.";
                }
                catch (Exception ex)
                {
                    StatusText = $"Error: {ex.Message}";
                }
                finally
                {
                    IsBusy = false;
                    await UpdateTotalRowCountAsync();
                }
            }
        }
    }

    private async Task ExportFileAsync()
    {
        if (RequestSaveFile != null)
        {
            var filePath = await RequestSaveFile();
            if (filePath != null)
            {
                await ExecuteCommandAsync($"export \"{filePath}\"");
            }
        }
    }

    private async Task NavigateGoAsync()
    {
        int? row = null;
        if (int.TryParse(SelectedNavigateRow, out var r))
        {
            row = r;
        }

        await NavigateToRowAndColumn(row, SelectedNavigateColumn ?? "");
        CloseInlinePanel();
    }

    private async Task NavigateToRowAndColumn(int? row, string column)
    {
        if (row.HasValue)
        {
            var targetRow = VisibleRows.FirstOrDefault(r => r.RowNumber == row.Value);
            if (targetRow != null)
            {
                RequestScrollToMatch?.Invoke(targetRow, column, null);
            }
            else
            {
                StatusText = $"Row {row.Value} is not currently loaded.";
            }
        }
        else if (!string.IsNullOrEmpty(column))
        {
            // Only column navigation
            var targetRow = VisibleRows.FirstOrDefault();
            RequestScrollToMatch?.Invoke(targetRow, column, null);
        }

        await Task.CompletedTask;
    }

    private void NavigateToMatch(DifferenceDetail detail)
    {
        _ = NavigateToRowAndColumn(detail.Row, detail.Description);
    }

    private bool _isCanceling;

    private async Task ExecuteCommandAsync(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText)) return;

        if (IsBusy)
        {
            if (_isCanceling) return;
            _isCanceling = true;
            await _currentOperationCts?.CancelAsync()!;
            StatusText = "Canceling...";
            return;
        }

        _currentOperationCts = new CancellationTokenSource();
        var ct = _currentOperationCts.Token;

        IsBusy = true;
        _isCanceling = false;
        try
        {
            LogCommand(commandText);
            var parts = Commands.SplitCommand(commandText);
            if (parts.Length == 0) return;
            var command = parts[0];
            var arguments = parts.Skip(1).ToArray();

            switch (command.ToLower())
            {
                case "load":
                    await Command_LoadAsync(arguments, ct);
                    NavigateColumnOptions.Clear();
                    foreach (var header in CurrentParser.Headers) NavigateColumnOptions.Add(header);
                    break;
                case "find":
                    await Command_FindAsync(arguments, ct);
                    break;
                case "hide":
                    RequestSetVisibility?.Invoke(arguments, false, ct);
                    break;
                case "unhide":
                    RequestSetVisibility?.Invoke(arguments, true, ct);
                    break;
                case "export":
                    await Command_ExportAsync(arguments, ct);
                    break;
                default:
                    StatusText = $"Unknown command: {command}";
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Operation canceled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _isCanceling = false;
            await UpdateTotalRowCountAsync();
        }
    }

    private void LogCommand(string command)
    {
        CommandHistory.Remove(command);
        CommandHistory.Insert(0, command);
        if (CommandHistory.Count > 100) CommandHistory.RemoveAt(CommandHistory.Count - 1);
    }

    private async Task Command_LoadAsync(string[] arguments, CancellationToken ct)
    {
        if (_currentFilePath == null) throw new Exception("No file imported. Use 'Import file' button first.");

        int startRow = 1;
        int endRow = Configuration.AutoLoadRows;

        if (arguments.Length >= 1 && int.TryParse(arguments[0], out var start))
        {
            startRow = start;
            endRow = start + Configuration.AutoLoadRows - 1;
        }

        if (arguments.Length >= 2 && int.TryParse(arguments[1], out var end))
        {
            endRow = end;
        }

        StatusText = $"Loading Rows {startRow}-{endRow}...";
        
        await LoadRangeIntoViewAsync(startRow, endRow, ct);
        StatusText = $"Loaded Rows {startRow}-{endRow}.";
    }

    private async Task Command_FindAsync(string[] arguments, CancellationToken ct)
    {
        if (arguments.Length == 0) throw new Exception("Usage: find <search_text> [column]");
        if (_currentFilePath == null) return;

        string searchText = arguments[0];
        string? columnSearchValue = arguments.Length >= 2 ? arguments[1] : null;

        var searchDescription = Parser.IsRegexTarget(searchText) ? $"regex {searchText}" : $"\"{searchText}\"";

        StatusText = $"Searching for {searchDescription}...";

        var progress = new Progress<int>(count =>
        {
            StatusText = $"Searching... Found {count:N0} matches so far.";
        });

        VisibleRows.Clear();
        SearchResults.Clear();

        List<string>? searchHeaders = null;
        if (columnSearchValue != null)
        {
            searchHeaders = RequestResolveHeaders?.Invoke(columnSearchValue);
        }

        var matches = new List<CsvRow>();
        var details = new List<DifferenceDetail>();
        var headers = CurrentParser.Headers;
        var filePath = _currentFilePath;
        var matcher = Parser.CreateSearchMatcher(searchText);
        var autoFindRows = Configuration.AutoFindRows;

        await Task.Run(async () =>
        {
            await foreach (var match in CurrentParser.ReadMatchesAsyncEnumerable(
                               filePath,
                               matcher,
                               searchHeaders,
                               autoFindRows,
                               progress,
                               ct))
            {
                if (!matches.Contains(match.Row))
                {
                    matches.Add(match.Row);
                }

                var columnIndex = headers.IndexOf(match.Header);
                details.Add(new DifferenceDetail(columnIndex, match.Header, match.RowNumber));
            }
        }, ct);

        foreach (var row in matches)
        {
            VisibleRows.Add(row);
        }

        foreach (var detail in details)
        {
            SearchResults.Add(detail);
        }

        if (SearchResults.Count > 0)
        {
            SearchResultsVisible = true;
            InlinePanelVisible = true;
        }

        StatusText = $"Found {SearchResults.Count:N0} instance(s) of {searchDescription}.";
    }

    private async Task Command_ExportAsync(string[] arguments, CancellationToken ct)
    {
        if (arguments.Length == 0) throw new Exception("Usage: export <file_path>");
        if (_currentFilePath == null) return;
        var filePath = arguments[0];
        StatusText = $"Exporting to {filePath}...";
        // Note: ExportToCsvAsync needs visible headers and rows. 
        // This is a simplification, might need more logic if we want to export only visible rows.
        await CurrentParser.ExportToCsvAsync(filePath, VisibleRows, CurrentParser.Headers, ct);
        StatusText = $"Exported to {filePath}.";
    }

    private async Task LoadRangeIntoViewAsync(int startRow, int endRow, CancellationToken ct = default)
    {
        if (_currentFilePath == null) return;
        VisibleRows.Clear();
        
        var filePath = _currentFilePath;
        var rows = new List<CsvRow>();

        await Task.Run(async () =>
        {
            await foreach (var row in CurrentParser.ReadRangeAsyncEnumerable(filePath, startRow, endRow, ct))
            {
                rows.Add(row);
            }
        }, ct);

        foreach (var row in rows)
        {
            VisibleRows.Add(row);
        }
    }

    private async Task UpdateTotalRowCountAsync()
    {
        if (_currentFilePath == null) return;
        var count = await CurrentParser.GetRowCountAsync(_currentFilePath);
        var colCount = CurrentParser.Headers.Count;
        var colRange = colCount > 0 
            ? $" ({Parser.GetColumnIdentifier(0)}-{Parser.GetColumnIdentifier(colCount - 1)})"
            : "";
        TotalRowsText = $"{count} rows | {colCount} columns {colRange}";
    }
}
