using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSVoom.app;

namespace CSVoom.ui.ViewModels;

public class FindCriterion : ViewModelBase
{
    private string _searchText = string.Empty;
    private string? _column;

    public string SearchText
    {
        get => _searchText;
        set => SetField(ref _searchText, value);
    }

    public string? Column
    {
        get => _column;
        set => SetField(ref _column, value);
    }
}

/// <summary>
///     The main view model for the application.
/// </summary>
public class MainWindowViewModel : ViewModelBase
{
    private string? _currentFilePath;

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
        }
    }

    /// <summary>
    ///     Gets a value indicating whether a command can be run.
    /// </summary>
    public bool CanRunCommand => true;

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
    ///     Gets or sets a value indicating whether the search results panel is visible.
    /// </summary>
    public bool SearchResultsVisible
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    ///     Gets or sets a value indicating whether the visibility panel is visible.
    /// </summary>
    public bool VisibilityPanelVisible
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    ///     Gets the find criteria.
    /// </summary>
    public ObservableCollection<FindCriterion> FindCriteria { get; } = [];

    /// <summary>
    ///     Gets or sets a value indicating whether the find panel is visible.
    /// </summary>
    public bool FindPanelVisible
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    ///     Gets or sets the search text for the find panel.
    /// </summary>
    public string? FindSearchText
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    ///     Gets or sets the column for the find panel.
    /// </summary>
    public string? FindColumn
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    ///     Gets or sets the start column for the visibility range.
    /// </summary>
    public string? VisibilityStartColumn
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    ///     Gets or sets the end column for the visibility range.
    /// </summary>
    public string? VisibilityEndColumn
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
    public RelayCommand CloseInlinePanelCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand SearchResultsCommand { get; }
    public RelayCommand NavigateToMatchCommand { get; }
    public RelayCommand VisibilityCommand { get; }
    public RelayCommand VisibilityApplyCommand { get; }
    public RelayCommand FindCommand { get; }
    public RelayCommand AddFindCriterionCommand { get; }
    public RelayCommand RemoveFindCriterionCommand { get; }
    public AsyncRelayCommand ExecuteFindCommand { get; }
    public AsyncRelayCommand NavigateGoCommand { get; }

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
        RunCommand = new AsyncRelayCommand(_ => ExecuteCommandAsync(""), allowConcurrent: true);
        OpenCommand = new AsyncRelayCommand(_ => OpenFileAsync());
        ExportCommand = new AsyncRelayCommand(_ => ExportFileAsync());
        SettingsCommand = new RelayCommand(_ => ShowSettings());
        ComparerCommand = new RelayCommand(_ => RequestShowComparer?.Invoke());
        NavigateCommand = new RelayCommand(_ => ShowNavigate());
        SearchResultsCommand = new RelayCommand(_ => ShowSearchResults());
        CloseInlinePanelCommand = new RelayCommand(_ => CloseInlinePanel());
        SaveSettingsCommand = new RelayCommand(_ => RequestSaveSettings?.Invoke());
        NavigateToMatchCommand = new RelayCommand(obj => NavigateToMatch((DifferenceDetail)obj!));
        VisibilityCommand = new RelayCommand(_ => ShowVisibility());
        VisibilityApplyCommand = new RelayCommand(obj => VisibilityApply((bool)obj!));
        FindCommand = new RelayCommand(_ => ShowFind());
        AddFindCriterionCommand = new RelayCommand(_ => AddFindCriterion());
        RemoveFindCriterionCommand = new RelayCommand(criterion => RemoveFindCriterion((FindCriterion)criterion!));
        ExecuteFindCommand = new AsyncRelayCommand(_ => ExecuteFindAsync());
        NavigateGoCommand = new AsyncRelayCommand(_ => NavigateGoAsync());
        
        VersionText = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";
    }

    public void CloseInlinePanel()
    {
        InlinePanelVisible = false;
        SettingsPanelVisible = false;
        NavigatePanelVisible = false;
        SearchResultsVisible = false;
        VisibilityPanelVisible = false;
        FindPanelVisible = false;
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

    private void ShowVisibility()
    {
        if (VisibilityPanelVisible)
        {
            CloseInlinePanel();
        }
        else
        {
            CloseInlinePanel();
            VisibilityPanelVisible = true;
            InlinePanelVisible = true;
        }
    }

    private void ShowFind()
    {
        if (FindPanelVisible)
        {
            CloseInlinePanel();
        }
        else
        {
            CloseInlinePanel();
            if (FindCriteria.Count == 0)
            {
                AddFindCriterion();
            }
            FindPanelVisible = true;
            InlinePanelVisible = true;
        }
    }

    private void AddFindCriterion()
    {
        FindCriteria.Add(new FindCriterion());
    }

    private void RemoveFindCriterion(FindCriterion criterion)
    {
        FindCriteria.Remove(criterion);
        if (FindCriteria.Count == 0)
        {
            AddFindCriterion();
        }
    }

    private async Task ExecuteFindAsync()
    {
        if (FindCriteria.All(c => string.IsNullOrWhiteSpace(c.SearchText))) return;

        var args = new List<string>();
        foreach (var criterion in FindCriteria)
        {
            if (string.IsNullOrWhiteSpace(criterion.SearchText)) continue;
            
            args.Add(criterion.SearchText);
            args.Add(criterion.Column ?? string.Empty);
        }

        _currentOperationCts = new CancellationTokenSource();
        IsBusy = true;
        _isCanceling = false;
        try
        {
            await Command_FindAsync(args.ToArray(), _currentOperationCts.Token);
            FindPanelVisible = false;
            if (SearchResults.Count == 0)
            {
                InlinePanelVisible = false;
            }
        }
        catch (OperationCanceledException)
        {
            // Handled in Command_FindAsync to allow partial results
            FindPanelVisible = false;
            if (SearchResults.Count == 0)
            {
                InlinePanelVisible = false;
            }
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

    private void VisibilityApply(bool state)
    {
        if (string.IsNullOrWhiteSpace(VisibilityStartColumn) && string.IsNullOrWhiteSpace(VisibilityEndColumn))
        {
            return;
        }

        var arguments = new List<string>();
        if (!string.IsNullOrWhiteSpace(VisibilityStartColumn))
        {
            arguments.Add(VisibilityStartColumn);
        }

        if (!string.IsNullOrWhiteSpace(VisibilityEndColumn))
        {
            arguments.Add(VisibilityEndColumn);
        }

        RequestSetVisibility?.Invoke(arguments.ToArray(), state, CancellationToken.None);
        CloseInlinePanel();
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
                    await LoadRangeIntoViewAsync(1, Configuration.MaxItems, ct);
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
                    _isCanceling = false;
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
            if (targetRow == null && _currentFilePath != null)
            {
                // Move to a row not loaded - load it
                int start = Math.Max(1, row.Value - Configuration.MaxItems / 2);
                int end = start + Configuration.MaxItems - 1;
                await LoadRangeIntoViewAsync(start, end);
                targetRow = VisibleRows.FirstOrDefault(r => r.RowNumber == row.Value);
            }

            if (targetRow != null)
            {
                RequestScrollToMatch?.Invoke(targetRow, column, null);
            }
            else
            {
                StatusText = $"Row {row.Value} could not be loaded.";
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
        if (IsBusy)
        {
            if (_isCanceling) return;
            _isCanceling = true;
            await _currentOperationCts?.CancelAsync()!;
            StatusText = "Canceling...";
            return;
        }

        if (string.IsNullOrWhiteSpace(commandText)) return;

        _currentOperationCts = new CancellationTokenSource();
        var ct = _currentOperationCts.Token;

        IsBusy = true;
        _isCanceling = false;
        string command = "";
        try
        {
            var parts = Commands.SplitCommand(commandText);
            if (parts.Length == 0) return;
            command = parts[0];
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
            if (!string.Equals(command, "find", StringComparison.OrdinalIgnoreCase))
            {
                StatusText = "Operation canceled.";
            }
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

    private async Task Command_LoadAsync(string[] arguments, CancellationToken ct)
    {
        if (_currentFilePath == null) throw new Exception("No file imported. Use 'Import file' button first.");

        int startRow = 1;
        int endRow = Configuration.MaxItems;

        if (arguments.Length >= 1 && int.TryParse(arguments[0], out var start))
        {
            startRow = start;
            endRow = start + Configuration.MaxItems - 1;
        }

        if (arguments.Length >= 2 && int.TryParse(arguments[1], out var end))
        {
            endRow = end;
        }

        StatusText = $"Loading Rows {startRow}-{endRow}...";
        
        await LoadRangeIntoViewAsync(startRow, endRow, ct);
        StatusText = $"Loaded Rows {startRow}-{endRow}.";
    }

    private int _lastLoadedEndRow = 0;

    private async Task Command_FindAsync(string[] arguments, CancellationToken ct)
    {
        if (arguments.Length == 0) throw new Exception("Usage: find <search_text> [column] ...");
        if (_currentFilePath == null) return;

        var criteria = new List<(Func<string, bool> Matcher, List<string>? SearchHeaders)>();
        var searchDescriptions = new List<string>();

        for (int i = 0; i < arguments.Length; i += 2)
        {
            string searchText = arguments[i];
            string? columnSearchValue = i + 1 < arguments.Length ? arguments[i + 1] : null;

            if (string.IsNullOrWhiteSpace(columnSearchValue)) columnSearchValue = null;

            var matcher = Parser.CreateSearchMatcher(searchText);
            List<string>? searchHeaders = null;
            if (columnSearchValue != null)
            {
                searchHeaders = RequestResolveHeaders?.Invoke(columnSearchValue);
            }

            criteria.Add((matcher, searchHeaders));
            searchDescriptions.Add(Parser.IsRegexTarget(searchText) ? $"regex {searchText}" : $"\"{searchText}\"");
        }

        var fullDescription = string.Join(" AND ", searchDescriptions);
        StatusText = $"Searching for {fullDescription}...";

        var progress = new Progress<int>(count =>
        {
            StatusText = $"Searching... Found {count:N0} matches so far.";
        });

        // find should not load the matching rows to the UI
        SearchResults.Clear();

        var details = new List<DifferenceDetail>();
        var headers = CurrentParser.Headers;
        var filePath = _currentFilePath;
        var autoFindRows = Configuration.AutoFindRows;

        try
        {
            await Task.Run(async () =>
            {
                await foreach (var row in CurrentParser.ReadRowsAsyncEnumerable(filePath, ct))
                {
                    bool allMatch = true;
                    var matchDetailsForRow = new List<DifferenceDetail>();

                    foreach (var criterion in criteria)
                    {
                        bool criterionMatched = false;
                        if (criterion.SearchHeaders != null)
                        {
                            foreach (var header in criterion.SearchHeaders)
                            {
                                var val = row[header, headers];
                                if (criterion.Matcher(val))
                                {
                                    criterionMatched = true;
                                    var columnIndex = headers.IndexOf(header);
                                    matchDetailsForRow.Add(new DifferenceDetail(columnIndex, header, row.RowNumber));
                                    break;
                                }
                            }
                        }
                        else
                        {
                            // Search all columns
                            for (int j = 0; j < headers.Count; j++)
                            {
                                if (criterion.Matcher(row[j]))
                                {
                                    criterionMatched = true;
                                    matchDetailsForRow.Add(new DifferenceDetail(j, headers[j], row.RowNumber));
                                    break;
                                }
                            }
                        }

                        if (!criterionMatched)
                        {
                            allMatch = false;
                            break;
                        }
                    }

                    if (allMatch)
                    {
                        details.AddRange(matchDetailsForRow);
                        ((IProgress<int>)progress).Report(details.Count);
                        
                        if (autoFindRows > 0 && details.Count >= autoFindRows)
                            break;
                    }
                }
            }, ct);
        }
        catch (OperationCanceledException)
        {
            // Allow showing partial results on cancellation
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

        StatusText = $"Found {SearchResults.Count:N0} instance(s) of {fullDescription}.";
        if (ct.IsCancellationRequested)
        {
            StatusText += " (Cancelled)";
        }
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
        
        var filePath = _currentFilePath;
        var rows = new List<CsvRow>();

        await Task.Run(async () =>
        {
            await foreach (var row in CurrentParser.ReadRangeAsyncEnumerable(filePath, startRow, endRow, ct))
            {
                rows.Add(row);
            }
        }, ct);

        if (rows.Count == 0) return;

        VisibleRows.Clear();
        foreach (var row in rows)
        {
            VisibleRows.Add(row);
        }
        _lastLoadedEndRow = rows[^1].RowNumber;
        _lastLoadedStartRow = rows[0].RowNumber;
    }

    private int _lastLoadedStartRow = 0;

    public async Task LoadMoreRowsAsync()
    {
        if (IsBusy || _currentFilePath == null) return;
        
        int startRow = _lastLoadedEndRow + 1;
        int endRow = startRow + Configuration.MaxItems / 2; // Load some more

        IsBusy = true;
        try
        {
            var filePath = _currentFilePath;
            var newRows = new List<CsvRow>();
            await Task.Run(async () =>
            {
                await foreach (var row in CurrentParser.ReadRangeAsyncEnumerable(filePath, startRow, endRow))
                {
                    newRows.Add(row);
                }
            });

            if (newRows.Count > 0)
            {
                foreach (var row in newRows)
                {
                    VisibleRows.Add(row);
                }
                _lastLoadedEndRow = newRows[^1].RowNumber;

                // Enforce MaxItems
                while (VisibleRows.Count > Configuration.MaxItems)
                {
                    VisibleRows.RemoveAt(0);
                }
                _lastLoadedStartRow = VisibleRows[0].RowNumber;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadPreviousRowsAsync()
    {
        if (IsBusy || _currentFilePath == null || _lastLoadedStartRow <= 1) return;

        int endRow = _lastLoadedStartRow - 1;
        int startRow = Math.Max(1, endRow - Configuration.MaxItems / 2 + 1);

        IsBusy = true;
        try
        {
            var filePath = _currentFilePath;
            var prevRows = new List<CsvRow>();
            await Task.Run(async () =>
            {
                await foreach (var row in CurrentParser.ReadRangeAsyncEnumerable(filePath, startRow, endRow))
                {
                    prevRows.Add(row);
                }
            });

            if (prevRows.Count > 0)
            {
                // Insert at the beginning in reverse order or just prepend
                for (int i = prevRows.Count - 1; i >= 0; i--)
                {
                    VisibleRows.Insert(0, prevRows[i]);
                }
                _lastLoadedStartRow = VisibleRows[0].RowNumber;

                // Enforce MaxItems
                while (VisibleRows.Count > Configuration.MaxItems)
                {
                    VisibleRows.RemoveAt(VisibleRows.Count - 1);
                }
                _lastLoadedEndRow = VisibleRows[^1].RowNumber;
            }
        }
        finally
        {
            IsBusy = false;
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
