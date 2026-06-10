using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CSVoom.app;
using CSVoom.ui;

namespace CSVoom;

public partial class Comparer : Window
{
    private readonly Parser _leftParser = new();
    private readonly ObservableCollection<CsvRow> _leftVisibleRows = [];
    private readonly Parser _rightParser = new();
    private readonly ObservableCollection<CsvRow> _rightVisibleRows = [];
    private readonly ObservableCollection<DifferenceItem> _differences = [];
    private CancellationTokenSource? _comparisonCts;
    private bool _isBusy;
    private string? _leftFilePath;
    private string? _rightFilePath;

    public Comparer()
    {
        InitializeComponent();
        LeftDataGrid.ItemsSource = new DataGridCollectionView(_leftVisibleRows);
        RightDataGrid.ItemsSource = new DataGridCollectionView(_rightVisibleRows);
        DifferencesItemsControl.ItemsSource = _differences;
    }

    private async void ImportLeft_Click(object? sender, RoutedEventArgs e)
    {
        _leftFilePath = await OpenFileAsync("Import Left CSV");
        if (_leftFilePath == null) return;
        LeftFileNameTextBlock.Text = _leftFilePath;
        ToolTip.SetTip(LeftFileNameTextBlock, _leftFilePath);
        await LoadFileAsync(_leftFilePath, _leftParser, LeftDataGrid, _leftVisibleRows);
    }

    private async void ImportRight_Click(object? sender, RoutedEventArgs e)
    {
        _rightFilePath = await OpenFileAsync("Import Right CSV");
        if (_rightFilePath != null)
        {
            RightFileNameTextBlock.Text = _rightFilePath;
            ToolTip.SetTip(RightFileNameTextBlock, _rightFilePath);
            await LoadFileAsync(_rightFilePath, _rightParser, RightDataGrid, _rightVisibleRows);
        }
    }

    private async Task<string?> OpenFileAsync(string title)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("CSV files")
                {
                    Patterns = Configuration.GetCsvFilePatterns()
                }
            ]
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private async Task LoadFileAsync(string filePath, Parser parser, DataGrid dataGrid,
        ObservableCollection<CsvRow> visibleRows)
    {
        try
        {
            StatusTextBlock.Text = $"Loading {filePath}...";
            ToolTip.SetTip(StatusTextBlock, filePath);
            await parser.ReadHeadersAsync(filePath);
            DataGridUtils.InitializeColumns(dataGrid, parser);
            visibleRows.Clear();
            var rows = await parser.ReadRangeAsync(filePath, 1, Configuration.AutoLoadRows);
            foreach (var row in rows) visibleRows.Add(row);
            StatusTextBlock.Text = $"Loaded {filePath}.";
            ToolTip.SetTip(StatusTextBlock, filePath);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Error loading file: {ex.Message}";
        }
    }

    private async void Compare_Click(object? sender, RoutedEventArgs e)
    {
        if (_leftFilePath == null || _rightFilePath == null)
        {
            StatusTextBlock.Text = "Please import both files first.";
            return;
        }

        if (_isBusy)
        {
            await _comparisonCts?.CancelAsync()!;
            return;
        }

        SetIsBusy(true);
        _comparisonCts = new CancellationTokenSource();
        _leftVisibleRows.Clear();
        _rightVisibleRows.Clear();
        _differences.Clear();

        try
        {
            StatusTextBlock.Text = "Comparing...";
            ComparisonProgressBar.IsVisible = true;
            ComparisonProgressBar.IsIndeterminate = true;

            await foreach (var result in Parser.CompareAsyncEnumerable(_leftFilePath, _rightFilePath,
                               _comparisonCts.Token))
            {
                if (result.LeftRow != null) _leftVisibleRows.Add(result.LeftRow);
                if (result.RightRow != null) _rightVisibleRows.Add(result.RightRow);

                switch (result.Status)
                {
                    case ComparisonStatus.AnomalousColumn when result.DifferentColumns != null:
                    {
                        foreach (var colIndex in result.DifferentColumns)
                        {
                            var colHeader = colIndex < _leftParser.Headers.Count ? _leftParser.Headers[colIndex] : (colIndex + 1).ToString();
                            _differences.Add(new DifferenceItem(1, $"[ANOMALOUS] {colHeader}", colIndex, "Column not found"));
                        }
                        break;
                    }
                    case ComparisonStatus.Different when result.DifferentColumns != null:
                    {
                        foreach (var colIndex in result.DifferentColumns)
                        {
                            var colHeader = colIndex < _leftParser.Headers.Count ? _leftParser.Headers[colIndex] : (colIndex + 1).ToString();

                            _differences.Add(new DifferenceItem(
                                result.RowNumber,
                                colHeader,
                                colIndex,
                                "Value mismatch"
                            ));
                        }

                        break;
                    }
                    case ComparisonStatus.LeftOnly:
                        _differences.Add(new DifferenceItem(result.RowNumber, "Row", -1, "Row only in left file"));
                        break;
                    case ComparisonStatus.RightOnly:
                        _differences.Add(new DifferenceItem(result.RowNumber, "Row", -1, "Row only in right file"));
                        break;
                    case ComparisonStatus.Equal:
                        break;
                    default:
                        var exception = new ArgumentOutOfRangeException
                        {
                            HelpLink = null,
                            HResult = 0,
                            Source = null
                        };
                        throw exception;
                }
            }

            StatusTextBlock.Text = $"Comparison complete. Found {_leftVisibleRows.Count} differences.";
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "Comparison canceled.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Error during comparison: {ex.Message}";
        }
        finally
        {
            SetIsBusy(false);
            ComparisonProgressBar.IsVisible = false;
        }
    }

    private void SetIsBusy(bool isBusy)
    {
        _isBusy = isBusy;
        CompareButton.Content = isBusy ? "Cancel" : "Compare";
        ImportLeftButton.IsEnabled = !isBusy;
        ImportRightButton.IsEnabled = !isBusy;
    }

    private void NavigateToDifference_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DifferenceItem item })
        {
            // Rows are 1-indexed in the file.
            // DataGrid items are 0-indexed. Index 0 corresponds to the first row of data.
            // If the first row is a header (RowNumber 1), then RowNumber 2 is index 0.
            // If the first row is NOT a header (RowNumber 1), then RowNumber 1 is index 0.
            var rowIndex = Configuration.FirstRowIsHeader ? item.Row - 2 : item.Row - 1;

            if (rowIndex < 0) return;
            var leftItems = LeftDataGrid.ItemsSource?.Cast<object>().ToList();
            if (leftItems != null && rowIndex < leftItems.Count)
            {
                LeftDataGrid.SelectedIndex = rowIndex;
                LeftDataGrid.ScrollIntoView(leftItems[rowIndex], null);
            }

            var rightItems = RightDataGrid.ItemsSource?.Cast<object>().ToList();
            if (rightItems != null && rowIndex < rightItems.Count)
            {
                RightDataGrid.SelectedIndex = rowIndex;
                RightDataGrid.ScrollIntoView(rightItems[rowIndex], null);
            }

            if (item.ColumnIndex < 0) return;
            var gridColumnIndex = item.ColumnIndex + DataGridUtils.RowNumberColumnOffset;
            if (gridColumnIndex < LeftDataGrid.Columns.Count)
                LeftDataGrid.ScrollIntoView(null, LeftDataGrid.Columns[gridColumnIndex]);
            if (gridColumnIndex < RightDataGrid.Columns.Count)
                RightDataGrid.ScrollIntoView(null, RightDataGrid.Columns[gridColumnIndex]);
        }
    }
}

public record DifferenceItem(int Row, string Column, int ColumnIndex, string Description)
{
    public string DisplayLabel => ColumnIndex >= 0
        ? $"Row: {Row} - Col: {Parser.GetColumnLetter(ColumnIndex)} ({Description})"
        : $"Row: {Row} - {Description}";
}