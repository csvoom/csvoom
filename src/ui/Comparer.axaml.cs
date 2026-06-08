using System;
using System.Collections.ObjectModel;
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
    private readonly Parser _rightParser = new();
    private string? _leftFilePath;
    private string? _rightFilePath;
    private readonly ObservableCollection<CsvRow> _leftVisibleRows = [];
    private readonly ObservableCollection<CsvRow> _rightVisibleRows = [];
    private bool _isBusy;
    private CancellationTokenSource? _comparisonCts;

    public Comparer()
    {
        InitializeComponent();
        LeftDataGrid.ItemsSource = new DataGridCollectionView(_leftVisibleRows);
        RightDataGrid.ItemsSource = new DataGridCollectionView(_rightVisibleRows);
    }

    private async void ImportLeft_Click(object? sender, RoutedEventArgs e)
    {
        _leftFilePath = await OpenFileAsync("Import Left CSV");
        if (_leftFilePath != null)
        {
            await LoadFileAsync(_leftFilePath, _leftParser, LeftDataGrid, _leftVisibleRows);
        }
    }

    private async void ImportRight_Click(object? sender, RoutedEventArgs e)
    {
        _rightFilePath = await OpenFileAsync("Import Right CSV");
        if (_rightFilePath != null)
        {
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
}