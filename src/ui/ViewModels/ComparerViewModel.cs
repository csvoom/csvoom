using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSVoom.app;

namespace CSVoom.ui.ViewModels;

public class ComparerViewModel : ViewModelBase
{
    private CancellationTokenSource? _comparisonCts;

    private Parser LeftParser { get; } = new();
    private Parser RightParser { get; } = new();

    public ObservableCollection<CsvRow> LeftVisibleRows { get; } = [];
    public ObservableCollection<CsvRow> RightVisibleRows { get; } = [];
    public ObservableCollection<DifferenceItem> Differences { get; } = [];

    public string? LeftFilePath
    {
        get;
        set => SetField(ref field, value);
    }

    public string? RightFilePath
    {
        get;
        set => SetField(ref field, value);
    }

    public string StatusText
    {
        get;
        set => SetField(ref field, value);
    } = "Import two CSV files to compare them.";

    private bool IsBusy
    {
        get;
        set
        {
            if (SetField(ref field, value))
            {
                CompareButtonText = value ? "Cancel" : "Compare";
                OnPropertyChanged(nameof(CanImport));
            }
        }
    }

    public bool CanImport => !IsBusy;

    public bool ComparisonProgressVisible
    {
        get;
        set => SetField(ref field, value);
    }

    public bool ComparisonProgressIndeterminate
    {
        get;
        set => SetField(ref field, value);
    }

    public string CompareButtonText
    {
        get;
        set => SetField(ref field, value);
    } = "Compare";

    public event Func<string, Parser, ObservableCollection<CsvRow>, Task>? RequestFileLoad;
    public event Action<DifferenceDetail, int>? RequestNavigation;

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

    private bool _isCanceling;

    private async Task CompareAsync()
    {
        if (LeftFilePath == null || RightFilePath == null)
        {
            StatusText = "Please import both files first.";
            return;
        }

        if (IsBusy)
        {
            if (_isCanceling) return;
            _isCanceling = true;
            await _comparisonCts?.CancelAsync()!;
            StatusText = "Canceling...";
            return;
        }

        IsBusy = true;
        _isCanceling = false;
        _comparisonCts = new CancellationTokenSource();
        LeftVisibleRows.Clear();
        RightVisibleRows.Clear();
        Differences.Clear();

        try
        {
            StatusText = "Comparing...";
            ComparisonProgressVisible = true;
            ComparisonProgressIndeterminate = true;

            int totalDifferences = 0;

            await foreach (var result in Parser.CompareAsyncEnumerable(LeftFilePath, RightFilePath,
                               _comparisonCts.Token))
            {
                if (result.Status == ComparisonStatus.Equal) continue;

                if (result.LeftRow != null) LeftVisibleRows.Add(result.LeftRow);
                if (result.RightRow != null) RightVisibleRows.Add(result.RightRow);

                var details = new List<DifferenceDetail>();

                switch (result.Status)
                {
                    case ComparisonStatus.AnomalousColumn when result.DifferentColumns != null:
                    {
                        details.AddRange(result.DifferentColumns.Select(colIndex => new DifferenceDetail(colIndex, "Column not found")));
                        break;
                    }
                    case ComparisonStatus.Different when result.DifferentColumns != null:
                    {
                        details.AddRange(result.DifferentColumns.Select(colIndex => new DifferenceDetail(colIndex, "Value mismatch")));
                        break;
                    }
                    case ComparisonStatus.LeftOnly:
                        details.Add(new DifferenceDetail(-1, "Row only in left file"));
                        break;
                    case ComparisonStatus.RightOnly:
                        details.Add(new DifferenceDetail(-1, "Row only in right file"));
                        break;
                    case ComparisonStatus.Equal:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (details.Count > 0)
                {
                    Differences.Add(new DifferenceItem(result.RowNumber, details));
                    totalDifferences += details.Count;
                }

                if (Differences.Count % 100 == 0)
                {
                    await Task.Yield();
                }

                if (totalDifferences >= Configuration.CompareLimit)
                {
                    StatusText = $"Comparison stopped. Found maximum {totalDifferences} differences.";
                    return;
                }
            }

            StatusText = $"Comparison complete. Found {totalDifferences} differences in {Differences.Count} rows.";
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
            _isCanceling = false;
            ComparisonProgressVisible = false;
        }
    }

    public void NavigateToDifference(DifferenceDetail detail, int row)
    {
        RequestNavigation?.Invoke(detail, row);
    }
}
