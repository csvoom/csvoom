using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CSVoom.app;
using CSVoom.ui;
using CSVoom.ui.ViewModels;

namespace CSVoom;

public partial class Comparer : Window
{
    private readonly ComparerViewModel _viewModel;
    private readonly Dictionary<string, DataGridColumn> _leftColumnsByLetter = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DataGridColumn> _leftColumnsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DataGridColumn> _rightColumnsByLetter = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DataGridColumn> _rightColumnsByName = new(StringComparer.OrdinalIgnoreCase);

    public Comparer()
    {
        InitializeComponent();
        _viewModel = new ComparerViewModel();
        DataContext = _viewModel;

        _viewModel.RequestFileLoad += async (path, parser, dataGrid, rows) =>
        {
            await LoadFileAsync(path, parser, dataGrid, rows);
        };

        _viewModel.RequestNavigation += NavigateToDifference;
    }

    private async void ImportLeft_Click(object? sender, RoutedEventArgs e)
    {
        var filePath = await OpenFileAsync("Import Left CSV");
        if (filePath != null)
        {
            await _viewModel.LoadLeftFileAsync(filePath, LeftDataGrid);
        }
    }

    private async void ImportRight_Click(object? sender, RoutedEventArgs e)
    {
        var filePath = await OpenFileAsync("Import Right CSV");
        if (filePath != null)
        {
            await _viewModel.LoadRightFileAsync(filePath, RightDataGrid);
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
            _viewModel.StatusText = $"Loading {filePath}...";
            await parser.ReadHeadersAsync(filePath);

            if (dataGrid == LeftDataGrid)
            {
                DataGridUtils.InitializeColumns(dataGrid, parser, _leftColumnsByName, _leftColumnsByLetter);
            }
            else
            {
                DataGridUtils.InitializeColumns(dataGrid, parser, _rightColumnsByName, _rightColumnsByLetter);
            }

            Dispatcher.UIThread.Post(() => DataGridUtils.ApplyFrozenColumn(dataGrid), DispatcherPriority.Background);

            visibleRows.Clear();
            var rows = await parser.ReadRangeAsync(filePath, 1, Configuration.AutoLoadRows);
            foreach (var row in rows) visibleRows.Add(row);
            _viewModel.StatusText = $"Loaded {filePath}.";
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Error loading file: {ex.Message}";
        }
    }

    private void NavigateToDifference_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DifferenceDetail detail })
        {
            _viewModel.NavigateToDifference(detail, detail.Row);
        }
    }

    private void NavigateToDifference(DifferenceDetail detail, int rowNumber)
    {
        // Rows are 1-indexed in the file.
        // DataGrid items are 0-indexed. Index 0 corresponds to the first row of data.
        // If the first row is a header (RowNumber 1), then RowNumber 2 is index 0.
        // If the first row is NOT a header (RowNumber 1), then RowNumber 1 is index 0.
        var rowIndex = Configuration.FirstRowIsHeader ? rowNumber - 2 : rowNumber - 1;

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

        if (detail.ColumnIndex < 0) return;

        var leftColumn = _leftColumnsByLetter.GetValueOrDefault(Parser.GetColumnIdentifier(detail.ColumnIndex));
        if (leftColumn != null)
        {
            LeftDataGrid.ScrollIntoView(null, leftColumn);
        }

        var rightColumn = _rightColumnsByLetter.GetValueOrDefault(Parser.GetColumnIdentifier(detail.ColumnIndex));
        if (rightColumn != null)
        {
            RightDataGrid.ScrollIntoView(null, rightColumn);
        }
    }
}
