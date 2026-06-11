using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CSVoom.app;
using CSVoom.ui;
using CSVoom.ui.ViewModels;

namespace CSVoom;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Dictionary<string, string> _editedSettings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DataGridColumn> _columnsByLetter = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DataGridColumn> _columnsByName = new(StringComparer.OrdinalIgnoreCase);

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;

        _viewModel.RequestOpenFile += async () =>
        {
            var topLevel = GetTopLevel(this);
            if (topLevel == null) return null;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import CSV",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("CSV files") { Patterns = Configuration.GetCsvFilePatterns() }]
            });
            return files.Count > 0 ? files[0].Path.LocalPath : null;
        };

        _viewModel.RequestSaveFile += async () =>
        {
            var topLevel = GetTopLevel(this);
            if (topLevel == null) return null;
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export CSV",
                FileTypeChoices = [new FilePickerFileType("CSV files") { Patterns = Configuration.GetCsvFilePatterns() }]
            });
            return file?.Path.LocalPath;
        };

        _viewModel.RequestShowSettings += PopulateSettingsPanel;
        _viewModel.RequestSaveSettings += async () =>
        {
            Configuration.Save(_editedSettings);
            ApplyConfigurationToUi();
            if (Application.Current is App app) app.UpdateTheme();
            _viewModel.CloseInlinePanel();
        };

        _viewModel.RequestShowComparer += () =>
        {
            var comparerWindow = new Comparer();
            comparerWindow.Show();
        };

        _viewModel.RequestColumnInitialization += (parser) =>
        {
            DataGridUtils.InitializeColumns(CsvDataGrid, parser, _columnsByName, _columnsByLetter);
        };

        _viewModel.RequestScrollToMatch += ScrollToMatch;

        _viewModel.RequestSetVisibility += (arguments, state) =>
        {
            Command_SetVisibility(arguments, state, CancellationToken.None);
        };

        _viewModel.RequestResolveHeaders += FindHeadersByNameLetterOrRegex;

        Closed += (_, _) =>
        {
            _viewModel.CloseInlinePanel();
        };
    }

    private void PopulateSettingsPanel()
    {
        _editedSettings.Clear();
        SettingsFieldsContainer.Children.Clear();

        foreach (var setting in Configuration.Settings)
        {
            var currentValue = Configuration.GetRawValue(setting.Key);

            SettingsFieldsContainer.Children.Add(new TextBlock
            {
                Text = $"{setting.Key} ({setting.Type})",
                FontWeight = FontWeight.Bold
            });

            SettingsFieldsContainer.Children.Add(new TextBlock
            {
                Text = setting.Description,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7
            });

            if (setting.Type.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
            {
                var checkBox = new CheckBox
                {
                    IsChecked = bool.TryParse(currentValue, out var boolValue) && boolValue,
                    Content = setting.Key
                };

                checkBox.IsCheckedChanged += (_, _) =>
                {
                    _editedSettings[setting.Key] = (checkBox.IsChecked == true).ToString();
                };

                _editedSettings[setting.Key] = (checkBox.IsChecked == true).ToString();
                SettingsFieldsContainer.Children.Add(checkBox);
            }
            else
            {
                var textBox = new TextBox
                {
                    Text = currentValue,
                    PlaceholderText = setting.DefaultValue
                };

                textBox.TextChanged += (_, _) => { _editedSettings[setting.Key] = textBox.Text ?? string.Empty; };
                SettingsFieldsContainer.Children.Add(textBox);
            }
        }
    }

    private void CommandHistoryListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is string command)
        {
            _viewModel.CommandText = command;
            _viewModel.CloseInlinePanel();
        }
    }

    private void ApplyConfigurationToUi()
    {
        if (_viewModel.ShowCommandExamples)
        {
            _viewModel.UpdateCommandExample(CommandTextBox.Text ?? "");
        }
    }

    private void CommandTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _viewModel.RunCommand.Execute(null);
    }


    private void ScrollToMatch(CsvRow? row, string header, string? columnLetter = null)
    {
        if (row == null) return;

        CsvDataGrid.SelectedItem = row;
        CsvDataGrid.ScrollIntoView(row, null);

        if (!string.IsNullOrEmpty(header) || !string.IsNullOrEmpty(columnLetter))
        {
            var column = FindColumnByNameOrLetter(header ?? columnLetter ?? "");
            if (column != null) CsvDataGrid.ScrollIntoView(row, column);
        }

        CsvDataGrid.Focus();
    }

    private void Command_SetVisibility(string[] arguments, bool state, CancellationToken cancellationToken)
    {
        var startIndex = 1;
        var endIndex = CsvDataGrid.Columns.Count;
        if (arguments.Length is < 1 or > 2) return;

        if (arguments[0] == "all")
        {
            for (var i = startIndex; i <= endIndex; i++)
            {
                if (cancellationToken.IsCancellationRequested) return;
                CsvDataGrid.Columns[i - 1].IsVisible = state;
            }
        }
        else
        {
            var startCol = FindColumnByNameOrLetter(arguments[0]);
            if (startCol == null) return;
            var startColIndex = CsvDataGrid.Columns.IndexOf(startCol);

            if (arguments.Length == 2)
            {
                var endCol = FindColumnByNameOrLetter(arguments[1]);
                if (endCol == null) return;
                var endColIndex = CsvDataGrid.Columns.IndexOf(endCol);

                var start = Math.Min(startColIndex, endColIndex);
                var end = Math.Max(startColIndex, endColIndex);

                for (var i = start; i <= end; i++)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    CsvDataGrid.Columns[i].IsVisible = state;
                }
            }
            else
            {
                CsvDataGrid.Columns[startColIndex].IsVisible = state;
            }
        }
    }

    private DataGridColumn? FindColumnByNameOrLetter(string searchValue)
    {
        if (string.IsNullOrWhiteSpace(searchValue)) return null;
        if (_columnsByName.TryGetValue(searchValue, out var columnByName)) return columnByName;
        if (_columnsByLetter.TryGetValue(searchValue, out var columnByLetter)) return columnByLetter;
        return null;
    }

    private List<string> FindHeadersByNameLetterOrRegex(string searchValue)
    {
        var columns = FindColumnsByNameLetterOrRegex(searchValue, true);
        List<string> headers = new(columns.Count);
        foreach (var columnIndex in columns.Select(column => CsvDataGrid.Columns.IndexOf(column)))
            switch (columnIndex)
            {
                case -1:
                    continue;
                case 0:
                    headers.Add(Parser.RowNumberKey);
                    break;
                default:
                {
                    var dataIndex = DataGridUtils.ToDataColumnIndex(columnIndex);
                    if (dataIndex is >= 0 && dataIndex < _viewModel.CurrentParser.Headers.Count)
                        headers.Add(_viewModel.CurrentParser.Headers[dataIndex.Value]);
                    break;
                }
            }

        return headers;
    }

    private List<DataGridColumn> FindColumnsByNameLetterOrRegex(string searchValue, bool includeHidden = false)
    {
        if (string.IsNullOrWhiteSpace(searchValue)) return [];

        if (Parser.IsRegexTarget(searchValue) && Configuration.RegexSearch)
        {
            var pattern = searchValue[1..^1];
            var regexOptions = RegexOptions.CultureInvariant;
            if (Configuration.CaseInsensitiveSearch) regexOptions |= RegexOptions.IgnoreCase;

            try
            {
                var regex = new Regex(pattern, regexOptions, TimeSpan.FromMilliseconds(Configuration.RegexTimeoutMilliseconds));
                return CsvDataGrid.Columns
                    .Where(c => (includeHidden || c.IsVisible) && regex.IsMatch(c.Header?.ToString() ?? ""))
                    .ToList();
            }
            catch { }
        }

        var column = FindColumnByNameOrLetter(searchValue);
        return column != null && (includeHidden || column.IsVisible) ? [column] : [];
    }
}
