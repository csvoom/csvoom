using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CSVoom.app;

namespace CSVoom.ui.ViewModels;

public class ComparerViewModel : ViewModelBase
{
    private string? _leftFilePath;
    private string? _rightFilePath;
    private string _statusText = "Import two CSV files to compare them.";
    private bool _isBusy;
    private bool _comparisonProgressVisible;
    private bool _comparisonProgressIndeterminate;
    private string _compareButtonText = "Compare";
    private CancellationTokenSource? _comparisonCts;

    public Parser LeftParser { get; } = new();
    public Parser RightParser { get; } = new();

    public ObservableCollection<CsvRow> LeftVisibleRows { get; } = [];
    public ObservableCollection<CsvRow> RightVisibleRows { get; } = [];
    public ObservableCollection<DifferenceItem> Differences { get; } = [];

    public string? LeftFilePath
    {
        get => _leftFilePath;
        set => SetField(ref _leftFilePath, value);
    }

    public string? RightFilePath
    {
        get => _rightFilePath;
        set => SetField(ref _rightFilePath, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetField(ref _isBusy, value))
            {
                CompareButtonText = value ? "Cancel" : "Compare";
                OnPropertyChanged(nameof(CanImport));
            }
        }
    }

    public bool CanImport => !IsBusy;

    public bool ComparisonProgressVisible
    {
        get => _comparisonProgressVisible;
        set => SetField(ref _comparisonProgressVisible, value);
    }

    public bool ComparisonProgressIndeterminate
    {
        get => _comparisonProgressIndeterminate;
        set => SetField(ref _comparisonProgressIndeterminate, value);
    }

    public string CompareButtonText
    {
        get => _compareButtonText;
        set => SetField(ref _compareButtonText, value);
    }

    public event Func<string, Parser, ObservableCollection<CsvRow>, Task>? RequestFileLoad;
    public event Action<DifferenceItem>? RequestNavigation;

    public AsyncRelayCommand CompareCommand { get; }

    public ComparerViewModel()
    {
        CompareCommand = new AsyncRelayCommand(_ => CompareAsync(), allowConcurrent: true);
    }

    public async Task LoadLeftFileAsync(string filePath)
    {
        LeftFilePath = filePath;
        if (RequestFileLoad != null)
        {
            await RequestFileLoad(filePath, LeftParser, LeftVisibleRows);
        }
    }

    public async Task LoadRightFileAsync(string filePath)
    {
        RightFilePath = filePath;
        if (RequestFileLoad != null)
        {
            await RequestFileLoad(filePath, RightParser, RightVisibleRows);
        }
    }

    private async Task CompareAsync()
    {
        if (LeftFilePath == null || RightFilePath == null)
        {
            StatusText = "Please import both files first.";
            return;
        }

        if (IsBusy)
        {
            if (_comparisonCts != null)
            {
                _comparisonCts.Cancel();
            }
            return;
        }

        IsBusy = true;
        _comparisonCts = new CancellationTokenSource();
        LeftVisibleRows.Clear();
        RightVisibleRows.Clear();
        Differences.Clear();

        try
        {
            StatusText = "Comparing...";
            ComparisonProgressVisible = true;
            ComparisonProgressIndeterminate = true;

            await foreach (var result in Parser.CompareAsyncEnumerable(LeftFilePath, RightFilePath,
                               _comparisonCts.Token))
            {
                if (result.Status == ComparisonStatus.Equal) continue;

                if (result.LeftRow != null) LeftVisibleRows.Add(result.LeftRow);
                if (result.RightRow != null) RightVisibleRows.Add(result.RightRow);

                switch (result.Status)
                {
                    case ComparisonStatus.AnomalousColumn when result.DifferentColumns != null:
                    {
                        foreach (var colIndex in result.DifferentColumns)
                        {
                            var colHeader = colIndex < LeftParser.Headers.Count ? LeftParser.Headers[colIndex] : (colIndex + 1).ToString();
                            Differences.Add(new DifferenceItem(1, $"[ANOMALOUS] {colHeader}", colIndex, "Column not found"));
                        }
                        break;
                    }
                    case ComparisonStatus.Different when result.DifferentColumns != null:
                    {
                        foreach (var colIndex in result.DifferentColumns)
                        {
                            var colHeader = colIndex < LeftParser.Headers.Count ? LeftParser.Headers[colIndex] : (colIndex + 1).ToString();

                            Differences.Add(new DifferenceItem(
                                result.RowNumber,
                                colHeader,
                                colIndex,
                                "Value mismatch"
                            ));
                        }

                        break;
                    }
                    case ComparisonStatus.LeftOnly:
                        Differences.Add(new DifferenceItem(result.RowNumber, "Row", -1, "Row only in left file"));
                        break;
                    case ComparisonStatus.RightOnly:
                        Differences.Add(new DifferenceItem(result.RowNumber, "Row", -1, "Row only in right file"));
                        break;
                    case ComparisonStatus.Equal:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            StatusText = $"Comparison complete. Found {Differences.Count} differences.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Comparison canceled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error during comparison: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            ComparisonProgressVisible = false;
        }
    }

    public void NavigateToDifference(DifferenceItem item)
    {
        RequestNavigation?.Invoke(item);
    }
}
