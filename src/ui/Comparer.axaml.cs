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
        DifferencesDataGrid.ItemsSource = new DataGridCollectionView(_differences);
    }

    private async void ImportLeft_Click(object? sender, RoutedEventArgs e)
    {
        _leftFilePath = await OpenFileAsync("Import Left CSV");
        if (_leftFilePath != null) await LoadFileAsync(_leftFilePath, _leftParser, LeftDataGrid, _leftVisibleRows);
    }

    private async void ImportRight_Click(object? sender, RoutedEventArgs e)
    {
        _rightFilePath = await OpenFileAsync("Import Right CSV");
        if (_rightFilePath != null) await LoadFileAsync(_rightFilePath, _rightParser, RightDataGrid, _rightVisibleRows);
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
            await parser.ReadHeadersAsync(filePath);
            DataGridUtils.InitializeColumns(dataGrid, parser);
            visibleRows.Clear();
            var rows = await parser.ReadRangeAsync(filePath, 1, Configuration.AutoLoadRows);
            foreach (var row in rows) visibleRows.Add(row);
            StatusTextBlock.Text = $"Loaded {filePath}.";
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

                if (result.Status == ComparisonStatus.Different && result.DifferentColumns != null)
                {
                    foreach (var colIndex in result.DifferentColumns)
                    {
                        var leftValue = colIndex < (result.LeftRow?.Values.Length ?? 0) ? result.LeftRow!.Values[colIndex] : "";
                        var rightValue = colIndex < (result.RightRow?.Values.Length ?? 0) ? result.RightRow!.Values[colIndex] : "";
                        var colHeader = colIndex < _leftParser.Headers.Count ? _leftParser.Headers[colIndex] : (colIndex + 1).ToString();

                        _differences.Add(new DifferenceItem(
                            result.RowNumber,
                            colHeader,
                            colIndex,
                            leftValue,
                            rightValue
                        ));
                    }
                }
                else if (result.Status == ComparisonStatus.LeftOnly)
                {
                    _differences.Add(new DifferenceItem(result.RowNumber, "Row", -1, "Only in Left", ""));
                }
                else if (result.Status == ComparisonStatus.RightOnly)
                {
                    _differences.Add(new DifferenceItem(result.RowNumber, "Row", -1, "", "Only in Right"));
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
            // Rows are 1-indexed in RowNumber, but 0-indexed in DataGrid
            var rowIndex = item.Row - 1;

            if (rowIndex >= 0)
            {
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

                if (item.ColumnIndex >= 0)
                {
                    if (item.ColumnIndex < LeftDataGrid.Columns.Count)
                        LeftDataGrid.ScrollIntoView(null, LeftDataGrid.Columns[item.ColumnIndex]);
                    if (item.ColumnIndex < RightDataGrid.Columns.Count)
                        RightDataGrid.ScrollIntoView(null, RightDataGrid.Columns[item.ColumnIndex]);
                }
            }
        }
    }
}

public record DifferenceItem(int Row, string Column, int ColumnIndex, string LeftValue, string RightValue);