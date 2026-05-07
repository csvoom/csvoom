using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
namespace CSVoom;

public partial class MainWindow : Window
{
    private const int BatchSize = 500;

    private readonly Parser _parser = new();
    private readonly ObservableCollection<string> _csvLines = [];

    private IAsyncEnumerator<string>? _csvEnumerator;
    private string? _currentFileName;
    private int _loadedLineCount;
    private bool _isLoadingBatch;
    private bool _finishedLoading;

    public MainWindow()
    {
        InitializeComponent();
        CsvLinesItemsControl.ItemsSource = _csvLines;
    }

    private async void OpenCsvButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            StatusTextBlock.Text = "Unable to open the file picker.";
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open CSV file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("CSV files")
                {
                    Patterns = ["*.csv", "*.gz"]
                }
            ]
        });

        if (files.Count == 0)
        {
            return;
        }

        var filePath = files[0].Path.LocalPath;

        try
        {
            if (_csvEnumerator is not null)
            {
                await _csvEnumerator.DisposeAsync();
                _csvEnumerator = null;
            }

            _csvLines.Clear();
            _loadedLineCount = 0;
            _finishedLoading = false;
            _currentFileName = files[0].Name;

            StatusTextBlock.Text = $"Loading {_currentFileName}...";

            _csvEnumerator = _parser.ParserLineEnumerator(filePath);

            await LoadNextBatchAsync();
        }
        catch (Exception ex)
        {
            _csvLines.Clear();
            StatusTextBlock.Text = $"Failed to load file: {ex.Message}";
        }
    }

    private async void CsvScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_finishedLoading || _isLoadingBatch || _csvEnumerator is null)
        {
            return;
        }

        var scrollViewer = (ScrollViewer)sender!;

        var distanceFromBottom =
            scrollViewer.Extent.Height
            - scrollViewer.Viewport.Height
            - scrollViewer.Offset.Y;

        if (distanceFromBottom <= 100)
        {
            await LoadNextBatchAsync();
        }
    }

    private async Task LoadNextBatchAsync()
    {
        if (_csvEnumerator is null || _isLoadingBatch || _finishedLoading)
        {
            return;
        }

        _isLoadingBatch = true;

        try
        {
            var loadedThisBatch = 0;

            while (loadedThisBatch < BatchSize && await _csvEnumerator.MoveNextAsync())
            {
                _csvLines.Add(_csvEnumerator.Current);
                _loadedLineCount++;
                loadedThisBatch++;
            }

            if (loadedThisBatch == 0)
            {
                _finishedLoading = true;
                await _csvEnumerator.DisposeAsync();
                _csvEnumerator = null;

                StatusTextBlock.Text = _loadedLineCount == 0
                    ? "The selected file is empty."
                    : $"Loaded {_loadedLineCount:N0} line(s) from {_currentFileName}.";
            }
            else
            {
                StatusTextBlock.Text = $"Loaded {_loadedLineCount:N0} line(s)... scroll down to load more.";
            }
        }
        finally
        {
            _isLoadingBatch = false;
        }
    }
}