using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CSVoom.app;

/// <summary>
///     Represents a row in a CSV file.
/// </summary>
/// <param name="values">The field values of the row.</param>
/// <param name="rowNumber">The 1-based row number.</param>
public class CsvRow(string[] values, int rowNumber)
{
    /// <summary>
    ///     Gets the field values of the row.
    /// </summary>
    public string[] Values { get; } = values;

    /// <summary>
    ///     Gets the 1-based row number.
    /// </summary>
    public int RowNumber { get; } = rowNumber;

    public string RowId => $"R{RowNumber}";

    /// <summary>
    ///     Gets the field value at the specified index.
    /// </summary>
    public string this[int index] => index >= 0 && index < Values.Length ? Values[index] : string.Empty;

    /// <summary>
    ///     Gets the field value for the specified header key.
    /// </summary>
    public string this[string key, List<string> headers]
    {
        get
        {
            if (key == Parser.RowNumberKey) return RowNumber.ToString();
            var index = headers.IndexOf(key);
            return index >= 0 && index < Values.Length ? Values[index] : string.Empty;
        }
    }
}

/// <summary>
///     Provides CSV parsing and export functionality.
/// </summary>
public class Parser
{
    /// <summary>
    ///     Special header key used for the row number column.
    /// </summary>
    public const string RowNumberKey = "__CsvRowNumber";

    private string[] _csvFilePatterns = [];

    private char _delimiter = ',';

    /// <summary>
    ///     Gets the headers of the parsed CSV file.
    /// </summary>
    public List<string> Headers { get; private set; } = [];

    /// <summary>
    ///     Creates a reusable matcher for plain text or slash-delimited regex command targets.
    /// </summary>
    public static Func<string, bool> CreateSearchMatcher(string searchTarget)
    {
        if (string.IsNullOrWhiteSpace(searchTarget)) return _ => false;

        var trimmed = searchTarget.Trim();
        if (trimmed.StartsWith("!=") || trimmed.StartsWith("<=") || trimmed.StartsWith(">="))
        {
            var op = trimmed[..2];
            var val = trimmed[2..].Trim();
            return CreateComparisonMatcher(op, val);
        }

        if (trimmed.StartsWith('<') || trimmed.StartsWith('>') || trimmed.StartsWith('='))
        {
            var op = trimmed[..1];
            var val = trimmed[1..].Trim();
            return CreateComparisonMatcher(op, val);
        }

        if (!IsRegexTarget(searchTarget) || !Configuration.RegexSearch)
            return value => value.Contains(
                searchTarget,
                Configuration.CaseInsensitiveSearch
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        var pattern = searchTarget[1..^1];
        var regexOptions = RegexOptions.CultureInvariant;

        if (Configuration.CaseInsensitiveSearch)
            regexOptions |= RegexOptions.IgnoreCase;

        try
        {
            var regex = new Regex(
                pattern,
                regexOptions,
                TimeSpan.FromMilliseconds(Configuration.RegexTimeoutMilliseconds));

            return regex.IsMatch;
        }
        catch (ArgumentException)
        {
            // Fallback to plain text search if regex is invalid
        }

        return value => value.Contains(
            searchTarget,
            Configuration.CaseInsensitiveSearch
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    /// <summary>
    ///     Checks if the search value is a regex target.
    /// </summary>
    public static bool IsRegexTarget(string searchValue)
    {
        return searchValue is ['/', _, ..] && searchValue[^1] == '/';
    }

    private static Func<string, bool> CreateComparisonMatcher(string op, string targetValue)
    {
        var isInteger = int.TryParse(targetValue, out var targetInt);
        var isNumeric = double.TryParse(targetValue, out var targetNum);

        return value =>
        {
            if (op is "=" or "!=")
            {
                if (isNumeric && double.TryParse(value, out var valNum))
                {
                    return op == "="
                        ? Math.Abs(valNum - targetNum) < 0.000001
                        : Math.Abs(valNum - targetNum) > 0.000001;
                }

                return op == "="
                    ? string.Equals(value, targetValue, StringComparison.OrdinalIgnoreCase)
                    : !string.Equals(value, targetValue, StringComparison.OrdinalIgnoreCase);
            }

            if (isInteger && int.TryParse(value, out var valInt))
            {
                return op switch
                {
                    "<" => valInt < targetInt,
                    ">" => valInt > targetInt,
                    "<=" => valInt <= targetInt,
                    ">=" => valInt >= targetInt,
                    _ => false
                };
            }

            return false;
        };
    }

    /// <summary>
    ///     Builds a <see cref="StreamReader" /> for the specified file path, handling compression if necessary.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    /// <returns>A <see cref="StreamReader" /> for the file.</returns>
    /// <exception cref="ArgumentException">Thrown when the file path is invalid or the extension is not supported.</exception>
    private StreamReader BuildReader(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or whitespace", nameof(filePath));

        if (_csvFilePatterns.Length == 0)
            _csvFilePatterns = Configuration.CsvFilePatterns.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries);

        var stream = File.OpenRead(filePath);
        try
        {
            if (!_csvFilePatterns.Contains("*" + Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"Unsupported file format: {Path.GetExtension(filePath)}");

            return filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                ? new StreamReader(new GZipStream(stream, CompressionMode.Decompress), Encoding.UTF8, true)
                : new StreamReader(stream, Encoding.UTF8, true);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Builds an async enumerator that reads lines from the specified CSV file.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    /// <param name="cancel">A cancellation token.</param>
    /// <returns>An async enumerator that reads lines from the file.</returns>
    private async IAsyncEnumerator<string> BuildParserEnumerator(string filePath, CancellationToken cancel = default)
    {
        if (!File.Exists(filePath)) yield break;

        using var reader = BuildReader(filePath);
        while (await reader.ReadLineAsync(cancel) is { } line) yield return line;
    }

    /// <summary>
    ///     Parses a single CSV line into a list of fields.
    /// </summary>
    /// <param name="line">The CSV line to parse.</param>
    /// <returns>A list of field values.</returns>
    private List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>(Headers.Count > 0 ? Headers.Count : Math.Max(1, line.Length / 8));
        if (line.Length == 0)
        {
            fields.Add(string.Empty);
            return fields;
        }

        ReadOnlySpan<char> lineSpan = line;
        var inQuotes = false;
        var start = 0;

        for (var i = 0; i < lineSpan.Length; i++)
        {
            var c = lineSpan[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < lineSpan.Length && lineSpan[i + 1] == '"')
                    i++;
                else
                    inQuotes = !inQuotes;
            }
            else if (c == _delimiter && !inQuotes)
            {
                fields.Add(UnescapeField(lineSpan[start..i]));
                start = i + 1;
            }
        }

        fields.Add(UnescapeField(lineSpan[start..]));
        return fields;
    }

    /// <summary>
    ///     Unescapes a CSV field, handling quotes and double quotes.
    /// </summary>
    /// <param name="field">The CSV field to unescape.</param>
    /// <returns>The unescaped field value.</returns>
    private static string UnescapeField(ReadOnlySpan<char> field)
    {
        if (field.Length == 0) return string.Empty;
        if (field[0] == '"' && field[^1] == '"')
        {
            field = field[1..^1];
            if (field.IndexOf('"') == -1) return field.ToString();

            StringBuilder sb = new(field.Length);
            for (var i = 0; i < field.Length; i++)
                if (field[i] == '"' && i + 1 < field.Length && field[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    sb.Append(field[i]);
                }

            return sb.ToString();
        }

        return field.ToString();
    }

    /// <summary>
    ///     Builds a <see cref="CsvRow" /> from a list of values and a row number.
    /// </summary>
    /// <param name="values">The field values.</param>
    /// <param name="rowNumber">The row number.</param>
    /// <returns>A new <see cref="CsvRow" /> instance.</returns>
    private static CsvRow BuildRow(List<string> values, int rowNumber)
    {
        return new CsvRow([.. values], rowNumber);
    }

    /// <summary>
    ///     Reads the headers from the specified CSV file.
    /// </summary>
    /// <param name="filePath">The path to the CSV file.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task ReadHeadersAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return;

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (extension == ".gz")
            extension = Path.GetExtension(Path.GetFileNameWithoutExtension(filePath)).ToLowerInvariant();

        _delimiter = extension switch
        {
            ".tsv" => '\t',
            ".ssv" => ';',
            _ => await DetectDelimiterAsync(filePath, cancellationToken)
        };

        await using var enumerator = BuildParserEnumerator(filePath, cancellationToken);

        if (await enumerator.MoveNextAsync())
        {
            var firstRow = ParseCsvLine(enumerator.Current);
            Headers = Configuration.FirstRowIsHeader
                ? firstRow
                : Enumerable.Range(0, firstRow.Count).Select(i => $"C{i + 1}").ToList();
        }
        else
        {
            Headers = [];
        }
    }

    /// <summary>
    ///     Detects whether the delimiter is ',' or ';' based on frequency in the first line.
    /// </summary>
    /// <param name="filePath">The path to the CSV file.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The detected delimiter character.</returns>
    private async Task<char> DetectDelimiterAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var enumerator = BuildParserEnumerator(filePath, cancellationToken);
        if (!await enumerator.MoveNextAsync()) return ',';

        var firstLine = enumerator.Current;
        var commaCount = 0;
        var semicolonCount = 0;
        var inQuotes = false;

        for (var i = 0; i < firstLine.Length; i++)
        {
            var c = firstLine[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < firstLine.Length && firstLine[i + 1] == '"')
                    i++;
                else
                    inQuotes = !inQuotes;
            }
            else if (!inQuotes)
            {
                switch (c)
                {
                    case ',':
                        commaCount++;
                        break;
                    case ';':
                        semicolonCount++;
                        break;
                }
            }
        }

        return semicolonCount > commaCount ? ';' : ',';
    }

    /// <summary>
    ///     Converts a zero-based data column index into its spreadsheet-style column identifier.
    /// </summary>
    /// <param name="columnIndex">The zero-based column index.</param>
    /// <returns>The spreadsheet-style column identifier (e.g., "C1", "C2").</returns>
    public static string GetColumnIdentifier(int columnIndex)
    {
        return $"C{columnIndex + 1}";
    }

    /// <summary>
    ///     Counts the total number of rows in the specified CSV file.
    /// </summary>
    /// <param name="filePath">The path to the CSV file.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The total number of rows (excluding the header if configured).</returns>
    public async Task<int> GetRowCountAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath)) return 0;
        var count = 0;
        await using var enumerator = BuildParserEnumerator(filePath, cancellationToken);
        while (await enumerator.MoveNextAsync()) count++;

        if (Configuration.FirstRowIsHeader && count > 0) count--;

        return count;
    }

    /// <summary>
    ///     Reads rows from the specified CSV file asynchronously.
    /// </summary>
    public async IAsyncEnumerable<CsvRow> ReadRowsAsyncEnumerable(string filePath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await ReadHeadersAsync(filePath, cancellationToken);
        var currentRowNumber = 0;
        await using var enumerator = BuildParserEnumerator(filePath, cancellationToken);

        while (await enumerator.MoveNextAsync() && !cancellationToken.IsCancellationRequested)
        {
            currentRowNumber++;
            if (currentRowNumber == 1 && Configuration.FirstRowIsHeader) continue;

            yield return BuildRow(ParseCsvLine(enumerator.Current), currentRowNumber);
        }
    }

    /// <summary>
    ///     Reads a range of rows from the specified CSV file asynchronously.
    /// </summary>
    /// <param name="filePath">The path to the CSV file.</param>
    /// <param name="startRow">The 1-based start row index.</param>
    /// <param name="endRow">The 1-based end row index.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An async enumerable of <see cref="CsvRow" /> instances.</returns>
    public async IAsyncEnumerable<CsvRow> ReadRangeAsyncEnumerable(string filePath, int startRow,
        int endRow, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await ReadHeadersAsync(filePath, cancellationToken);
        var currentRowNumber = 0;
        await using var enumerator = BuildParserEnumerator(filePath, cancellationToken);

        if (startRow <= 0) yield break;
        if (endRow < startRow) (startRow, endRow) = (endRow, startRow);
        if (startRow <= 0) startRow = 1;

        while (await enumerator.MoveNextAsync() && !cancellationToken.IsCancellationRequested)
        {
            currentRowNumber++;
            if (currentRowNumber == 1 && Configuration.FirstRowIsHeader) continue;
            if (currentRowNumber < startRow) continue;
            if (currentRowNumber > endRow) break;

            yield return BuildRow(ParseCsvLine(enumerator.Current), currentRowNumber);
        }
    }

    /// <summary>
    ///     Reads a range of rows from the specified CSV file asynchronously and returns them as an
    ///     <see cref="ObservableCollection{CsvRow}" />.
    /// </summary>
    /// <param name="filePath">The path to the CSV file.</param>
    /// <param name="startRow">The 1-based start row index.</param>
    /// <param name="endRow">The 1-based end row index.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An observable collection of <see cref="CsvRow" /> instances.</returns>
    public async Task<ObservableCollection<CsvRow>> ReadRangeAsync(string filePath, int startRow,
        int endRow, CancellationToken cancellationToken = default)
    {
        ObservableCollection<CsvRow> rows = [];
        await foreach (var row in ReadRangeAsyncEnumerable(filePath, startRow, endRow, cancellationToken))
            rows.Add(row);
        return rows;
    }

    /// <summary>
    ///     Searches for matches in the CSV file asynchronously.
    /// </summary>
    /// <param name="filePath">The path to the CSV file.</param>
    /// <param name="matcher">A function that returns true if a value matches the search criteria.</param>
    /// <param name="headersToSearch">The headers of the columns to search.</param>
    /// <param name="maxMatches">The maximum number of matches to find.</param>
    /// <param name="progress">An optional progress reporter for the number of rows processed.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An async enumerable of matching row details.</returns>
    private async IAsyncEnumerable<(CsvRow Row, string Header, string Value, int RowNumber)>
        ReadMatchesAsyncEnumerable(string filePath, Func<string, bool> matcher, List<string>? headersToSearch,
            int maxMatches, IProgress<int>? progress = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await ReadHeadersAsync(filePath, cancellationToken);
        var headers = (headersToSearch ?? Headers.Prepend(RowNumberKey).ToList()).ToList();
        await using var enumerator = BuildParserEnumerator(filePath, cancellationToken);
        var currentRowNumber = 0;
        var matchCount = 0;

        if (headers.Count == 0) yield break;

        while (matchCount < maxMatches && await enumerator.MoveNextAsync() &&
               !cancellationToken.IsCancellationRequested)
        {
            currentRowNumber++;
            if (currentRowNumber == 1 && Configuration.FirstRowIsHeader) continue;

            var row = BuildRow(ParseCsvLine(enumerator.Current), currentRowNumber);
            var foundInThisRow = false;

            foreach (var header in headers)
            {
                if (matchCount >= maxMatches) break;

                var value = row[header, Headers];
                if (!matcher(value)) continue;

                matchCount++;
                foundInThisRow = true;
                yield return (row, header, value, row.RowNumber);
            }

            if (foundInThisRow) progress?.Report(matchCount);
        }
    }

    /// <summary>
    ///     Searches for matches in the CSV file asynchronously and returns them as a list.
    /// </summary>
    /// <param name="filePath">The path to the CSV file.</param>
    /// <param name="matcher">A function that returns true if a value matches the search criteria.</param>
    /// <param name="headersToSearch">The headers of the columns to search.</param>
    /// <param name="maxMatches">The maximum number of matches to find.</param>
    /// <param name="progress">An optional progress reporter for the number of rows processed.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A list of matching row details.</returns>
    public async Task<List<(CsvRow Row, string Header, string Value, int RowNumber)>>
        ReadMatchesAsync(string filePath, Func<string, bool> matcher, List<string>? headersToSearch, int maxMatches,
            IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var matches = new List<(CsvRow Row, string Header, string Value, int RowNumber)>();
        await foreach (var match in ReadMatchesAsyncEnumerable(filePath, matcher, headersToSearch, maxMatches, progress,
                           cancellationToken))
            matches.Add(match);
        return matches;
    }

    /// <summary>
    ///     Exports the specified rows to a CSV file.
    /// </summary>
    /// <param name="filePath">The path to the destination file.</param>
    /// <param name="rows">The rows to export.</param>
    /// <param name="visibleHeaders">The headers of the columns to include in the export.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task ExportToCsvAsync(string filePath, IEnumerable<CsvRow> rows,
        List<string> visibleHeaders, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return;

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var delimiter = extension switch
        {
            ".tsv" => '\t',
            ".ssv" => ';',
            _ => ','
        };

        await using StreamWriter writer = new(filePath, false, Encoding.UTF8);

        if (Configuration.FirstRowIsHeader)
            await writer.WriteLineAsync(string.Join(delimiter.ToString(),
                visibleHeaders.Select(h => EscapeCsvField(h, delimiter))));

        foreach (var row in rows)
        {
            if (cancellationToken.IsCancellationRequested) return;
            var values = visibleHeaders.Select(h => row[h, Headers]);
            await writer.WriteLineAsync(string.Join(delimiter.ToString(),
                values.Select(v => EscapeCsvField(v, delimiter))));
        }
    }

    /// <summary>
    ///     Escapes a field for CSV, handling delimiters, quotes, and newlines.
    /// </summary>
    /// <param name="field">The field value to escape.</param>
    /// <param name="delimiter">The delimiter character.</param>
    /// <returns>The escaped field value.</returns>
    private static string EscapeCsvField(string field, char delimiter)
    {
        if (string.IsNullOrEmpty(field)) return string.Empty;
        if (field.Contains(delimiter) || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }

    /// <summary>
    ///     Compares two CSV files and returns rows that are different.
    /// </summary>
    /// <param name="leftFilePath">The path to the first CSV file.</param>
    /// <param name="rightFilePath">The path to the second CSV file.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An async enumerable of comparison results.</returns>
    public static async IAsyncEnumerable<ComparisonResult> CompareAsyncEnumerable(
        string leftFilePath,
        string rightFilePath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Parser leftParser = new();
        Parser rightParser = new();

        await leftParser.ReadHeadersAsync(leftFilePath, cancellationToken);
        await rightParser.ReadHeadersAsync(rightFilePath, cancellationToken);

        var ignoredColumns = new HashSet<int>();

        if (Configuration.FirstRowIsHeader)
        {
            var maxHeaderCols = Math.Max(leftParser.Headers.Count, rightParser.Headers.Count);
            var anomalousColumns = new List<int>();

            for (var i = 0; i < maxHeaderCols; i++)
            {
                var leftHeader = i < leftParser.Headers.Count ? leftParser.Headers[i] : null;
                var rightHeader = i < rightParser.Headers.Count ? rightParser.Headers[i] : null;

                if (leftHeader == rightHeader) continue;
                anomalousColumns.Add(i);
                ignoredColumns.Add(i);
            }

            if (anomalousColumns.Count > 0)
            {
                yield return new ComparisonResult(1, null, null, ComparisonStatus.AnomalousColumn, anomalousColumns);
            }
        }

        await using var leftEnumerator =
            leftParser.BuildParserEnumerator(leftFilePath, cancellationToken);
        await using var rightEnumerator =
            rightParser.BuildParserEnumerator(rightFilePath, cancellationToken);

        var currentRowNumber = Configuration.FirstRowIsHeader ? 1 : 0;
        var differencesFound = 0;
        var leftHasMore = true;
        var rightHasMore = true;

        if (Configuration.FirstRowIsHeader)
        {
            leftHasMore = await leftEnumerator.MoveNextAsync();
            rightHasMore = await rightEnumerator.MoveNextAsync();
        }

        while (leftHasMore && rightHasMore)
        {
            cancellationToken.ThrowIfCancellationRequested();

            leftHasMore = await leftEnumerator.MoveNextAsync();
            rightHasMore = await rightEnumerator.MoveNextAsync();

            if (!leftHasMore || !rightHasMore) break;

            currentRowNumber++;

            var leftRow = leftHasMore
                ? BuildRow(leftParser.ParseCsvLine(leftEnumerator.Current), currentRowNumber)
                : null;
            var rightRow = rightHasMore
                ? BuildRow(rightParser.ParseCsvLine(rightEnumerator.Current), currentRowNumber)
                : null;

            if (leftRow == null && rightRow != null)
            {
                differencesFound++;
                yield return new ComparisonResult(currentRowNumber, null, rightRow, ComparisonStatus.RightOnly);
            }
            else if (leftRow != null && rightRow == null)
            {
                differencesFound++;
                yield return new ComparisonResult(currentRowNumber, leftRow, null, ComparisonStatus.LeftOnly);
            }
            else if (leftRow != null && rightRow != null)
            {
                var diffColumns = new List<int>();
                var maxCols = Math.Max(leftRow.Values.Length, rightRow.Values.Length);
                for (var i = 0; i < maxCols; i++)
                {
                    if (ignoredColumns.Contains(i)) continue;

                    var leftVal = i < leftRow.Values.Length ? leftRow.Values[i] : null;
                    var rightVal = i < rightRow.Values.Length ? rightRow.Values[i] : null;
                    if (leftVal != rightVal) diffColumns.Add(i);
                }

                if (diffColumns.Count > 0)
                {
                    differencesFound++;
                    yield return new ComparisonResult(currentRowNumber, leftRow, rightRow, ComparisonStatus.Different,
                        diffColumns);
                }
                else
                {
                    yield return new ComparisonResult(currentRowNumber, leftRow, rightRow, ComparisonStatus.Equal);
                }
            }

            if (differencesFound >= Configuration.CompareLimit)
                yield break;
        }
    }
}

/// <summary>
///     Represents the status of a row comparison.
/// </summary>
public enum ComparisonStatus
{
    Equal,
    Different,
    LeftOnly,
    RightOnly,
    AnomalousColumn
}

/// <summary>
///     Represents the result of a comparison between two rows.
/// </summary>
/// <param name="RowNumber">The row number.</param>
/// <param name="LeftRow">The row from the left file.</param>
/// <param name="RightRow">The row from the right file.</param>
/// <param name="Status">The comparison status.</param>
/// <param name="DifferentColumns">The indices of the columns that are different.</param>
public record ComparisonResult(int RowNumber, CsvRow? LeftRow, CsvRow? RightRow, ComparisonStatus Status, List<int>? DifferentColumns = null);