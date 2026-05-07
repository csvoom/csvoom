using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CSVoom.app;

namespace CSVoom;

public partial class MainWindow : Window
{
    private readonly Parser _parser = new();
    private readonly ObservableCollection<string> _csvLines = [];

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
            _csvLines.Clear();
            StatusTextBlock.Text = $"Loading {files[0].Name}...";

            var loadedLineCount = 0;
            var uiUpdateCounter = 0;

            await using var parser = _parser.ParserLineEnumerator(filePath);
            while (await parser.MoveNextAsync())
            {
                _csvLines.Add(parser.Current);

                loadedLineCount++;
                uiUpdateCounter++;

                if (uiUpdateCounter >= 500)
                {
                    StatusTextBlock.Text = $"Loaded {loadedLineCount:N0} line(s)...";
                    uiUpdateCounter = 0;

                    await Task.Yield();
                }
            }

            StatusTextBlock.Text = loadedLineCount == 0
                ? "The selected file is empty."
                : $"Loaded {loadedLineCount:N0} line(s) from {files[0].Name}.";
        }
        catch (Exception ex)
        {
            _csvLines.Clear();
            StatusTextBlock.Text = $"Failed to load file: {ex.Message}";
        }
    }
}