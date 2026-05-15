using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CSVoom.app;
using Avalonia.Input;
using System.Reflection;

namespace CSVoom;

public partial class MainWindow : Window
{
    private static readonly Parser Parser = new();
    private string? _currentFilePath;
    private string? _currentFileName;
    private bool _finishedLoading;
    private bool _isLoadingBatch;

    private readonly Dictionary<string, DataGridColumn> _columnsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DataGridColumn> _columnsByLetter = new(StringComparer.OrdinalIgnoreCase);

    private static string GetColumnLetter(int columnIndex)
    {
        var letter = string.Empty;
        columnIndex++;

        while (columnIndex > 0)
        {
            columnIndex--;
            letter = (char)('A' + columnIndex % 26) + letter;
            columnIndex /= 26;
        }

        return letter;
    }

    private DataGridColumn? FindColumnByNameOrLetter(string searchValue)
    {
        if (string.IsNullOrWhiteSpace(searchValue))
        {
            return null;
        }

        var normalizedSearchValue = searchValue.Trim();

        if (_columnsByName.TryGetValue(normalizedSearchValue, out var columnByName))
        {
            return columnByName;
        }

        return _columnsByLetter.GetValueOrDefault(normalizedSearchValue);
    }

    private int FindColumnIndexByNameOrLetter(string searchValue)
    {
        var column = FindColumnByNameOrLetter(searchValue);
        return column is null ? -1 : CsvDataGrid.Columns.IndexOf(column);
    }

    private void ExecuteCommand(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return;
        }

        var parts = commandText.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0];

        if (command.Equals("hide", StringComparison.OrdinalIgnoreCase))
        {
            ExecuteHideCommand(parts.Length > 1 ? parts[1] : string.Empty);
            return;
        }

        if (command.Equals("unhide", StringComparison.OrdinalIgnoreCase))
        {
            ExecuteUnhideCommand(parts.Length > 1 ? parts[1] : string.Empty);
            return;
        }

        StatusTextBlock.Text = $"Unknown command: {command}";
    }

    private void ExecuteHideCommand(string arguments)
    {
        if (CsvDataGrid.Columns.Count == 0)
        {
            StatusTextBlock.Text = "Open a CSV file before running column commands.";
            return;
        }

        if (string.IsNullOrWhiteSpace(arguments))
        {
            StatusTextBlock.Text = "Usage: hide a:x or hide columnName";
            return;
        }

        var rangeParts = arguments.Split(':', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (rangeParts.Length == 1)
        {
            var column = FindColumnByNameOrLetter(rangeParts[0]);
            if (column is null)
            {
                StatusTextBlock.Text = $"Column not found: {rangeParts[0]}";
                return;
            }

            column.IsVisible = false;
            StatusTextBlock.Text = $"Hidden column {column.Header}.";
            return;
        }

        var startIndex = FindColumnIndexByNameOrLetter(rangeParts[0]);
        var endIndex = FindColumnIndexByNameOrLetter(rangeParts[1]);

        if (startIndex < 0)
        {
            StatusTextBlock.Text = $"Column not found: {rangeParts[0]}";
            return;
        }

        if (endIndex < 0)
        {
            StatusTextBlock.Text = $"Column not found: {rangeParts[1]}";
            return;
        }

        if (startIndex > endIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        for (var i = startIndex; i <= endIndex; i++)
        {
            CsvDataGrid.Columns[i].IsVisible = false;
        }

        StatusTextBlock.Text = $"Hidden columns {GetColumnLetter(startIndex)}:{GetColumnLetter(endIndex)}.";
    }

    private void ExecuteUnhideCommand(string arguments)
    {
        if (arguments.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            ShowAllColumns();
            StatusTextBlock.Text = "All columns are visible.";
            return;
        }

        StatusTextBlock.Text = "Usage: unhide all";
    }

    private void ShowAllColumns()
    {
        foreach (var column in CsvDataGrid.Columns)
        {
            column.IsVisible = true;
        }
    }

    private void RunCommandButton_Click(object? sender, RoutedEventArgs e)
    {
        ExecuteCommand(CommandTextBox.Text ?? string.Empty);
        CommandTextBox.SelectAll();
    }

    private void CommandTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ExecuteCommand(CommandTextBox.Text ?? string.Empty);
        CommandTextBox.SelectAll();
        e.Handled = true;
    }

    private static DataGridColumn? GetColumnFromHeader(DataGridColumnHeader header)
    { // REMOVE
        var owningColumnProperty = typeof(DataGridColumnHeader).GetProperty(
            "OwningColumn",
            BindingFlags.Instance | BindingFlags.NonPublic);

        return owningColumnProperty?.GetValue(header) as DataGridColumn;
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
    
    public MainWindow()
    {
        InitializeComponent();
        var gridView = new DataGridCollectionView(Parser.Rows);
        CsvDataGrid.ItemsSource = gridView;

        CsvDataGrid.AddHandler(
            ContextRequestedEvent,
            CsvDataGrid_ContextRequested,
            RoutingStrategies.Tunnel);
    }
    
    private void CsvDataGrid_ContextRequested(object? sender, ContextRequestedEventArgs e)
    { 
        if (e.Source is not Visual sourceVisual)
            return;
        
        var header = sourceVisual as DataGridColumnHeader
            ?? sourceVisual.GetVisualAncestors().OfType<DataGridColumnHeader>().FirstOrDefault();
    }
    
    private void CsvDataGrid_Loaded(object? sender, RoutedEventArgs e)
    {
        var verticalScrollBar = CsvDataGrid
            .GetVisualDescendants()
            .OfType<ScrollBar>()
            .FirstOrDefault(scrollBar => scrollBar.Orientation == Orientation.Vertical);

        verticalScrollBar?.PropertyChanged += CsvVerticalScrollBar_PropertyChanged;
    }
    
    private void CsvDataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    { 
        if (e.Row.DataContext is Dictionary<string, string> row && row.TryGetValue(Parser.RowNumberKey, out var rowNumber)) 
        { 
            e.Row.Header = rowNumber;
        }
    }
    
    private async void CsvVerticalScrollBar_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    { 
        if (e.Property != RangeBase.ValueProperty || _finishedLoading || _currentFilePath is null || sender is not ScrollBar scrollBar)
        {
            return;
        }
        var distanceFromBottom = scrollBar.Maximum - scrollBar.Value;
        if (distanceFromBottom <= 100)
        {
            await LoadNextBatchAsync();
        }
    }

    private async void OpenCsvButton_Click(object? sender, RoutedEventArgs e)
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
        CsvDataGrid.Columns.Clear();
        _columnsByName.Clear();
        _columnsByLetter.Clear();

        StatusTextBlock.Text = $"Loading {_currentFileName}...";

        await LoadNextBatchAsync();

        CsvDataGrid.Columns.Clear();
        _columnsByName.Clear();
        _columnsByLetter.Clear();

        for (var i = 0; i < Parser.Headers.Count; i++)
        {
            var header = Parser.Headers[i];
            var columnLetter = GetColumnLetter(i);

            var column = new DataGridTextColumn
            {
                Header = $"{columnLetter}: {header}",
                Binding = new Binding($"[{header}]"),
                SortMemberPath = $"[{header}]"
            };

            CsvDataGrid.Columns.Add(column);
            _columnsByName[header] = column;
            _columnsByLetter[columnLetter] = column;
        }
    }

    private void UnhideAllColumnsButton_Click(object? sender, RoutedEventArgs e)
    {
        ShowAllColumns();
    }
}