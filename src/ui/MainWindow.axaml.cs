using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using CSVoom.app;
using Avalonia.Collections;
using System.Collections.Specialized;

namespace CSVoom;

public partial class MainWindow : Window
{
    private static readonly Parser Parser = new();

    private DataGridCollectionView? _gridView;

    private string? _currentFilePath;
    private string? _currentFileName;
    private bool _finishedLoading;
    private bool _isLoadingBatch;
    private bool _columnsCreated;
    private bool _isSorting;

    public MainWindow()
    {
        InitializeComponent();

        _gridView = new DataGridCollectionView(Parser.Rows);
        _gridView.SortDescriptions.CollectionChanged += SortDescriptions_CollectionChanged;

        CsvDataGrid.ItemsSource = _gridView;
    }

    private void CsvDataGrid_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var verticalScrollBar = CsvDataGrid
            .GetVisualDescendants()
            .OfType<ScrollBar>()
            .FirstOrDefault(scrollBar => scrollBar.Orientation == Orientation.Vertical);

        verticalScrollBar?.PropertyChanged += CsvVerticalScrollBar_PropertyChanged;
        _isSorting = false;
    }

    private void SortDescriptions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_gridView is null)
            return;

        _isSorting = _gridView.SortDescriptions.Count != 0;
    }
    
    private void CsvDataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
        {
            if (e.Row.DataContext is Dictionary<string, string> row &&
                row.TryGetValue(Parser.RowNumberKey, out var rowNumber))
            {
                e.Row.Header = rowNumber;
            }
        }
    
    private async void CsvVerticalScrollBar_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != RangeBase.ValueProperty ||
                _finishedLoading ||
                _currentFilePath is null ||
                _isSorting ||
                sender is not ScrollBar scrollBar)
        {
            return;
        }

        var distanceFromBottom = scrollBar.Maximum - scrollBar.Value;

        if (distanceFromBottom <= 100)
        {
            await LoadNextBatchAsync();
        }
    }

    private async void OpenCsvButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
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
                        Binding = new Binding($"[{header}]"),
                        SortMemberPath = $"[{header}]"
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
