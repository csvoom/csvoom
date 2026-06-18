using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CSVoom.app;

namespace CSVoom.ui.ViewModels;

/// <summary>
///     View model for the CSV comparison functionality.
/// </summary>
public class ComparerViewModel : ViewModelBase
{
    private CancellationTokenSource? _comparisonCts;

    private Parser LeftParser { get; } = new();
    private Parser RightParser { get; } = new();

    /// <summary>
    ///     Gets the collection of visible rows for the left CSV file.
    /// </summary>
    public ObservableCollection<CsvRow> LeftVisibleRows { get; } = [];

    /// <summary>
    ///     Gets the collection of visible rows for the right CSV file.
    /// </summary>
    public ObservableCollection<CsvRow> RightVisibleRows { get; } = [];

    /// <summary>
    ///     Gets the collection of differences found between the two files.
    /// </summary>
    public ObservableCollection<DifferenceItem> Differences { get; } = [];

    /// <summary>
    ///     Gets or sets the file path for the left CSV file.
    /// </summary>
    public string? LeftFilePath
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    ///     Gets or sets the file path for the right CSV file.
    /// </summary>
    public string? RightFilePath
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    ///     Gets or sets the status text displayed in the UI.
    /// </summary>
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

    /// <summary>
    ///     Gets a value indicating whether a file can be imported.
    /// </summary>
    public bool CanImport => !IsBusy;

    /// <summary>
    ///     Gets or sets a value indicating whether the comparison progress is visible.
    /// </summary>
    public bool ComparisonProgressVisible
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    ///     Gets or sets a value indicating whether the comparison progress is indeterminate.
    /// </summary>
    public bool ComparisonProgressIndeterminate
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    ///     Gets or sets the text for the compare button.
    /// </summary>
    public string CompareButtonText
    {
        get;
        set => SetField(ref field, value);
    } = "Compare";

    /// <summary>
    ///     Occurs when a file needs to be loaded into a data grid.
    /// </summary>
    public event Func<string, Parser, DataGrid, ObservableCollection<CsvRow>, Task>? RequestFileLoad;

    /// <summary>
    ///     Occurs when navigation to a specific difference is requested.
    /// </summary>
    public event Action<DifferenceDetail, int>? RequestNavigation;

    /// <summary>
    ///     Gets the command to start or cancel the comparison.
    /// </summary>
    public AsyncRelayCommand CompareCommand { get; }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ComparerViewModel" /> class.
    /// </summary>
    public ComparerViewModel()
    {
        CompareCommand = new AsyncRelayCommand(_ => CompareAsync(), allowConcurrent: true);
    }

    /// <summary>
    ///     Loads the left file.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="dataGrid">The data grid to load into.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task LoadLeftFileAsync(string filePath, DataGrid dataGrid)
    {
        LeftFilePath = filePath;
        if (RequestFileLoad != null)
        {
            await RequestFileLoad(filePath, LeftParser, dataGrid, LeftVisibleRows);
        }

        if (RightFilePath != null)
        {
            await CompareAsync();
        }
    }

    /// <summary>
    ///     Loads the right file.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="dataGrid">The data grid to load into.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task LoadRightFileAsync(string filePath, DataGrid dataGrid)
    {
        RightFilePath = filePath;
        if (RequestFileLoad != null)
        {
            await RequestFileLoad(filePath, RightParser, dataGrid, RightVisibleRows);
        }

        if (LeftFilePath != null)
        {
            await CompareAsync();
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
            int rowIndex = 0;

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
                        details.AddRange(result.DifferentColumns.Select(colIndex => new DifferenceDetail(colIndex, "Column not found", rowIndex)));
                        break;
                    }
                    case ComparisonStatus.Different when result.DifferentColumns != null:
                    {
                        details.AddRange(result.DifferentColumns.Select(colIndex => new DifferenceDetail(colIndex, "Value mismatch", rowIndex)));
                        break;
                    }
                    case ComparisonStatus.LeftOnly:
                        details.Add(new DifferenceDetail(-1, "Row only in left file", rowIndex));
                        break;
                    case ComparisonStatus.RightOnly:
                        details.Add(new DifferenceDetail(-1, "Row only in right file", rowIndex));
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

                rowIndex++;

                if (Differences.Count % 100 == 0)
                {
                    await Task.Yield();
                }

                if (totalDifferences >= Configuration.MaxCompare)
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

    /// <summary>
    ///     Navigates to a specific difference.
    /// </summary>
    /// <param name="detail">The difference detail.</param>
    /// <param name="row">The row index in the visible rows.</param>
    public void NavigateToDifference(DifferenceDetail detail, int row)
    {
        RequestNavigation?.Invoke(detail, row);
    }
}
