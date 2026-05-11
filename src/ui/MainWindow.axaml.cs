using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Platform.Storage;
using CSVoom.app;

namespace CSVoom;

public partial class MainWindow : Window
{
    private static readonly Parser Parser = new();

    private string? _currentFilePath;
    private string? _currentFileName;
    private bool _finishedLoading;
    private bool _isLoadingBatch;
    private bool _columnsCreated;

    public MainWindow()
    {
        InitializeComponent();
        CsvDataGrid.ItemsSource = Parser.Rows;
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

        _currentFilePath = files[0].Path.LocalPath;
        _currentFileName = files[0].Name;
        _finishedLoading = false;
        _columnsCreated = false;
        CsvDataGrid.Columns.Clear();

        try
        {
            StatusTextBlock.Text = $"Loading {_currentFileName}...";
            await LoadNextBatchAsync();
        }
        catch (Exception ex)
        {
            _currentFilePath = null;
            _currentFileName = null;
            _finishedLoading = true;
            StatusTextBlock.Text = $"Failed to load file: {ex.Message}";
        }
    }

    private async void CsvScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_finishedLoading || _currentFilePath is null || sender is not ScrollViewer scrollViewer)
        {
            return;
        }

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
        if (_currentFilePath is null || _isLoadingBatch || _finishedLoading)
        {
            return;
        }

        _isLoadingBatch = true;

        try
        {
            var rowCountBeforeLoad = Parser.Rows.Count;

            await Parser.ReadBatchAsync(_currentFilePath);

            if (!_columnsCreated && Parser.Headers.Count > 0)
            {
                CsvDataGrid.Columns.Clear();

                foreach (var header in Parser.Headers)
                {
                    CsvDataGrid.Columns.Add(new DataGridTextColumn
                    {
                        Header = header,
                        Binding = new Binding($"[{header}]")
                    });
                }

                _columnsCreated = true;
            }

            if (Parser.Rows.Count == rowCountBeforeLoad)
            {
                _finishedLoading = true;
                StatusTextBlock.Text = $"Finished loading {_currentFileName}.";
                return;
            }

            StatusTextBlock.Text = $"Loaded {Parser.Rows.Count:N0} rows from {_currentFileName}.";
        }
        finally
        {
            _isLoadingBatch = false;
        }
    }
}