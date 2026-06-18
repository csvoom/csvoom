using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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

    /// <summary>
    ///     Initializes a new instance of the <see cref="MainWindow" /> class.
    /// </summary>
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
        _viewModel.RequestSaveSettings += () =>
        {
            try
            {
                Configuration.Save(_editedSettings);
                if (Application.Current is App app) app.UpdateTheme();
                _viewModel.CloseInlinePanel();
                return Task.CompletedTask;
            }
            catch (Exception exception)
            {
                return Task.FromException(exception);
            }
        };

        _viewModel.RequestShowComparer += () =>
        {
            var comparerWindow = new Comparer();
            comparerWindow.Show();
        };

        _viewModel.RequestColumnInitialization += (parser) =>
        {
            DataGridUtils.InitializeColumns(CsvDataGrid, parser, _columnsByName, _columnsByLetter);
            Dispatcher.UIThread.Post(() => DataGridUtils.ApplyFrozenColumn(CsvDataGrid), DispatcherPriority.Background);
        };

        _viewModel.RequestScrollToMatch += ScrollToMatch;

        _viewModel.RequestSetVisibility += Command_SetVisibility;

        _viewModel.RequestResolveHeaders += FindHeadersByNameLetterOrRegex;

        Dispatcher.UIThread.Post(() =>
        {
            var scrollBars = CsvDataGrid.GetVisualDescendants().OfType<ScrollBar>();
            var verticalScrollBar = scrollBars.FirstOrDefault(sb => sb.Orientation == Avalonia.Layout.Orientation.Vertical);
            if (verticalScrollBar != null)
            {
                verticalScrollBar.Scroll += async (_, _) =>
                {
                    // Check if we are near the bottom
                    if (verticalScrollBar.Value >= verticalScrollBar.Maximum)
                    {
                        await _viewModel.LoadMoreRowsAsync();
                    }
                    // Check if we are near the top
                    else if (verticalScrollBar.Value <= verticalScrollBar.Minimum && _viewModel.VisibleRows.Count > 0 && _viewModel.VisibleRows[0].RowNumber > 1)
                    {
                        var topRow = _viewModel.VisibleRows.FirstOrDefault();
                        await _viewModel.LoadPreviousRowsAsync();
                        if (topRow != null) CsvDataGrid.ScrollIntoView(topRow, null);
                    }
                };
            }

            // Support for scroll gesture (pointer wheel)
            CsvDataGrid.PointerWheelChanged += async (_, e) =>
            {
                if (verticalScrollBar == null) return;

                // Check if we are at the bottom and scrolling down
                if (e.Delta.Y < 0 && verticalScrollBar.Value >= verticalScrollBar.Maximum)
                {
                    await _viewModel.LoadMoreRowsAsync();
                }
                // Check if we are at the top and scrolling up
                else if (e.Delta.Y > 0 && verticalScrollBar.Value <= verticalScrollBar.Minimum && _viewModel.VisibleRows.Count > 0 && _viewModel.VisibleRows[0].RowNumber > 1)
                {
                    var topRow = _viewModel.VisibleRows.FirstOrDefault();
                    await _viewModel.LoadPreviousRowsAsync();
                    if (topRow != null) CsvDataGrid.ScrollIntoView(topRow, null);
                }
            };
        }, DispatcherPriority.Background);

        Closed += (_, _) =>
        {
            _viewModel.CloseInlinePanel();
        };
    }

    /// <summary>
    ///     Populates the settings panel with controls for each configuration option.
    /// </summary>
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
            else if (setting.Type.Equals("Select", StringComparison.OrdinalIgnoreCase))
            {
                var options = setting.Options ?? new[] { setting.DefaultValue };
                var combo = new ComboBox
                {
                    ItemsSource = options,
                    SelectedItem = options.Contains(currentValue) ? currentValue : setting.DefaultValue
                };

                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is string selected)
                    {
                        _editedSettings[setting.Key] = selected;
                    }
                };

                _editedSettings[setting.Key] = combo.SelectedItem as string ?? setting.DefaultValue;
                SettingsFieldsContainer.Children.Add(combo);
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


    private void NavigateToMatch_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: DifferenceDetail detail })
        {
            _viewModel.NavigateToMatchCommand.Execute(detail);
        }
    }


    private void NavigateControl_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.NavigateGoCommand.Execute(null);
        }
    }

    private void FindControl_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (_viewModel.ExecuteFindCommand.CanExecute(null))
            {
                _viewModel.ExecuteFindCommand.Execute(null);
            }
        }
    }


    /// <summary>
    ///     Scrolls the data grid to a specific match.
    /// </summary>
    /// <param name="row">The row to scroll to.</param>
    /// <param name="header">The header to scroll to.</param>
    /// <param name="columnLetter">The column letter to scroll to (optional).</param>
    private void ScrollToMatch(CsvRow? row, string? header, string? columnLetter = null)
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

    /// <summary>
    ///     Sets the visibility of columns based on a search pattern.
    /// </summary>
    /// <param name="arguments">The search arguments.</param>
    /// <param name="state">True to show, false to hide.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    private void Command_SetVisibility(string[] arguments, bool state, CancellationToken cancellationToken)
    {
        var startIndex = 1;
        var endIndex = CsvDataGrid.Columns.Count;
        if (arguments.Length is < 1 or > 2) return;

        if (arguments[0].Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = startIndex; i <= endIndex; i++)
            {
                if (cancellationToken.IsCancellationRequested) return;
                CsvDataGrid.Columns[i - 1].IsVisible = state;
                if (i % 100 == 0) Thread.Yield();
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
                    if ((i - start) % 100 == 0) Thread.Yield();
                }
            }
            else
            {
                CsvDataGrid.Columns[startColIndex].IsVisible = state;
            }
        }
    }

    /// <summary>
    ///     Finds a data grid column by its name or column letter.
    /// </summary>
    /// <param name="searchValue">The name or letter to search for.</param>
    /// <returns>The matching column, or null if not found.</returns>
    private DataGridColumn? FindColumnByNameOrLetter(string searchValue)
    {
        if (string.IsNullOrWhiteSpace(searchValue)) return null;
        if (_columnsByName.TryGetValue(searchValue, out var columnByName)) return columnByName;
        if (_columnsByLetter.TryGetValue(searchValue, out var columnByLetter)) return columnByLetter;

        if (int.TryParse(searchValue, out var index) && index >= 0 && index < CsvDataGrid.Columns.Count)
        {
            return CsvDataGrid.Columns[index];
        }

        return null;
    }

    /// <summary>
    ///     Finds header names by name, letter, or regex pattern.
    /// </summary>
    /// <param name="searchValue">The search pattern.</param>
    /// <returns>A list of matching header names.</returns>
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

    /// <summary>
    ///     Finds data grid columns by name, letter, or regex pattern.
    /// </summary>
    /// <param name="searchValue">The search pattern.</param>
    /// <param name="includeHidden">True to include hidden columns in the search.</param>
    /// <returns>A list of matching data grid columns.</returns>
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
                var regex = new Regex(pattern, regexOptions, TimeSpan.FromMilliseconds(Configuration.RegexTimeout));
                return CsvDataGrid.Columns
                    .Where(c => (includeHidden || c.IsVisible) && regex.IsMatch(c.Header?.ToString() ?? ""))
                    .ToList();
            }
            catch
            {
                // ignored
            }
        }

        var column = FindColumnByNameOrLetter(searchValue);
        return column != null && (includeHidden || column.IsVisible) ? [column] : [];
    }
}
