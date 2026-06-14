using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSVoom.app;

namespace CSVoom.ui.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private string? _currentFilePath;

    public ObservableCollection<string> CommandHistory { get; } = [];
    public ObservableCollection<CsvRow> VisibleRows { get; } = [];
    public ObservableCollection<string> NavigateColumnOptions { get; } = [];

    public string WindowTitle
    {
        get;
        set => SetField(ref field, value);
    } = "CSVoom";

    public string CommandText
    {
        get;
        set
        {
            if (SetField(ref field, value))
            {
                UpdateCommandExample(value);
            }
        }
    } = "";

    public string StatusText
    {
        get;
        set => SetField(ref field, value);
    } = "Choose a CSV file to display its contents.";

    public string TotalRowsText
    {
        get;
        set => SetField(ref field, value);
    } = "";

    public string CommandExampleText
    {
        get;
        set => SetField(ref field, value);
    } = "";

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

    public bool CanRunCommand => true;

    public string RunButtonText => IsBusy ? "Cancel" : "Run";

    public bool InlinePanelVisible
    {
        get;
        set => SetField(ref field, value);
    }

    public bool SettingsPanelVisible
    {
        get;
        set => SetField(ref field, value);
    }

    public bool NavigatePanelVisible
    {
        get;
        set => SetField(ref field, value);
    }

    public bool CommandHistoryPanelVisible
    {
        get;
        set => SetField(ref field, value);
    }

    public string? SelectedNavigateColumn
    {
        get;
        set => SetField(ref field, value);
    }

    public string? SelectedNavigateRow
    {
        get;
        set => SetField(ref field, value);
    }

    public AsyncRelayCommand RunCommand { get; }
    public AsyncRelayCommand OpenCommand { get; }
    public AsyncRelayCommand ExportCommand { get; }
    public RelayCommand SettingsCommand { get; }
    public RelayCommand ComparerCommand { get; }
    public RelayCommand NavigateCommand { get; }
    public RelayCommand CommandHistoryCommand { get; }
    public RelayCommand CloseInlinePanelCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public AsyncRelayCommand NavigateGoCommand { get; }

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
            ["load"] = "Arguments: [start(int)] [end(int)]",
            ["find"] = "Arguments: word / word columnName",
            ["hide"] = "Arguments: colum / column column",
            ["unhide"] = "Arguments: column / column column"
        };

    public string[]? AutoCompleteOptions => Configuration.MaxCommandHistoryItems > 0
        ? CommandSuggestions.Take(Configuration.MaxCommandHistoryItems).ToArray()
        : null;

    public bool ShowCommandExamples => Configuration.ShowCommandExamples;

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
        CloseInlinePanelCommand = new RelayCommand(_ => CloseInlinePanel());
        SaveSettingsCommand = new RelayCommand(_ => RequestSaveSettings?.Invoke());
        NavigateGoCommand = new AsyncRelayCommand(_ => NavigateGoAsync());
        
        VersionText = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";
    }

    public void CloseInlinePanel()
    {
        InlinePanelVisible = false;
        SettingsPanelVisible = false;
        NavigatePanelVisible = false;
        CommandHistoryPanelVisible = false;
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

    public void UpdateCommandExample(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            CommandExampleText = "";
            return;
        }
        var firstWord = text.Split(' ')[0].ToLower();
        CommandExampleText = CommandExamples.GetValueOrDefault(firstWord, "");
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
                    WindowTitle = $"CSVoom - {System.IO.Path.GetFileName(filePath)}";
                    await CurrentParser.ReadHeadersAsync(filePath, ct);
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
        if (string.IsNullOrEmpty(SelectedNavigateRow)) return;
        if (!int.TryParse(SelectedNavigateRow, out var row)) return;

        var startRow = ((row - 1) / Configuration.AutoLoadRows) * Configuration.AutoLoadRows + 1;
        var endRow = startRow + Configuration.AutoLoadRows - 1;

        await LoadRangeIntoViewAsync(startRow, endRow);
        RequestScrollToMatch?.Invoke(VisibleRows.FirstOrDefault(r => r.RowNumber == row), SelectedNavigateColumn ?? "", null);
        CloseInlinePanel();
    }

    private bool _isCanceling;

    private async Task ExecuteCommandAsync(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText)) return;

        if (IsBusy)
        {
            if (_isCanceling) return;
            _isCanceling = true;
            _currentOperationCts?.Cancel();
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
                    break;
                case "find":
                    await Command_FindAsync(arguments, ct);
                    break;
                case "hide":
                    RequestSetVisibility?.Invoke(arguments, false, ct);
                    break;
                case "show":
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
        if (CommandHistory.Contains(command)) CommandHistory.Remove(command);
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

        var searchText = arguments[0];
        var columnSearchValue = arguments.Length >= 2 ? arguments[1] : null;

        var searchDescription = Parser.IsRegexTarget(searchText) ? $"regex {searchText}" : $"\"{searchText}\"";
        
        StatusText = $"Searching for {searchDescription}...";

        var progress = new Progress<int>(count =>
        {
            StatusText = $"Searching... Found {count:N0} matches so far.";
        });

        VisibleRows.Clear();
        var foundCount = 0;

        List<string>? searchHeaders = null;
        if (columnSearchValue != null)
        {
            searchHeaders = RequestResolveHeaders?.Invoke(columnSearchValue);
        }

        var matches = new List<CsvRow>();
        await foreach (var match in CurrentParser.ReadMatchesAsyncEnumerable(
                           _currentFilePath,
                           Parser.CreateSearchMatcher(searchText),
                           searchHeaders,
                           Configuration.AutoFindRows,
                           progress,
                           ct))
        {
            if (!matches.Contains(match.Row))
            {
                matches.Add(match.Row);
            }
            foundCount++;

            if (matches.Count % 100 == 0)
            {
                await Task.Yield();
            }
        }

        VisibleRows.Clear();
        foreach (var row in matches)
        {
            VisibleRows.Add(row);
        }

        StatusText = $"Found {foundCount:N0} instance(s) of {searchDescription}.";
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
        var count = 0;
        await foreach (var row in CurrentParser.ReadRangeAsyncEnumerable(_currentFilePath, startRow, endRow, ct))
        {
            VisibleRows.Add(row);
            count++;
            if (count % 100 == 0)
            {
                await Task.Yield();
            }
        }
    }

    private async Task UpdateTotalRowCountAsync()
    {
        if (_currentFilePath == null) return;
        var count = await CurrentParser.GetRowCountAsync(_currentFilePath);
        var colCount = CurrentParser.Headers.Count;
        var colRange = colCount > 0 
            ? $" ({Parser.GetColumnLetter(0)}-{Parser.GetColumnLetter(colCount - 1)})" 
            : "";
        TotalRowsText = $"{count} rows | {colCount} columns {colRange}";
    }
}
