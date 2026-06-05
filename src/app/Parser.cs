using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CSVoom.app;

/// <summary>
/// Represents a row in a CSV file.
/// </summary>
/// <param name="values">The field values of the row.</param>
/// <param name="rowNumber">The 1-based row number.</param>
public class CsvRow(string[] values, int rowNumber)
{
    /// <summary>
    /// Gets the field values of the row.
    /// </summary>
    public string[] Values { get; } = values;

    /// <summary>
    /// Gets the 1-based row number.
    /// </summary>
    public int RowNumber { get; } = rowNumber;

    /// <summary>
    /// Gets the field value at the specified index.
    /// </summary>
    public string this[int index] => index >= 0 && index < Values.Length ? Values[index] : string.Empty;

    /// <summary>
    /// Gets the field value for the specified header key.
    /// </summary>
    public string this[string key, List<string> headers]
    {
        get
        {
            if (key == Parser.RowNumberKey) return RowNumber.ToString();
            int index = headers.IndexOf(key);
            return index >= 0 && index < Values.Length ? Values[index] : string.Empty;
        }
    }
}

/// <summary>
/// Provides CSV parsing and export functionality.
/// </summary>
public class Parser
{
    /// <summary>
    /// Special header key used for the row number column.
    /// </summary>
    public const string RowNumberKey = "__CsvRowNumber";
    private char _delimiter = ',';
    private string[] _csvFilePatterns = [];

    /// <summary>
    /// Gets the headers of the parsed CSV file.
    /// </summary>
    public List<string> Headers { get; private set; } = [];

    /// <summary>
    /// Builds a <see cref="StreamReader"/> for the specified file path, handling compression if necessary.
    /// </summary>
    private StreamReader BuildReader(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or whitespace", nameof(filePath));

        if (_csvFilePatterns.Length == 0)
        {
            _csvFilePatterns = Configuration.CsvFilePatterns.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries);
        }

        FileStream stream = File.OpenRead(filePath);
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
    /// Builds an async enumerator that reads lines from the specified CSV file.
    /// </summary>
    private async IAsyncEnumerator<string> BuildParserEnumerator(string filePath, CancellationToken cancel = default)
    {
        if (!File.Exists(filePath)) yield break;

        using StreamReader reader = BuildReader(filePath);
        while (await reader.ReadLineAsync(cancel) is { } line) yield return line;
    }

    /// <summary>
    /// Parses a single CSV line into a list of fields.
    /// </summary>
    private List<string> ParseCsvLine(string line)
    {
        List<string> fields = new List<string>(Headers.Count > 0 ? Headers.Count : Math.Max(1, line.Length / 8));
        if (line.Length == 0)
        {
            fields.Add(string.Empty);
            return fields;
        }

        ReadOnlySpan<char> lineSpan = line;
        bool inQuotes = false;
        int start = 0;

        for (int i = 0; i < lineSpan.Length; i++)
        {
            char c = lineSpan[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < lineSpan.Length && lineSpan[i + 1] == '"')
                {
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
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
    /// Unescapes a CSV field, handling quotes and double quotes.
    /// </summary>
    private static string UnescapeField(ReadOnlySpan<char> field)
    {
        if (field.Length == 0) return string.Empty;
        if (field[0] == '"' && field[^1] == '"')
        {
            field = field[1..^1];
            if (field.IndexOf('"') == -1) return field.ToString();

            StringBuilder sb = new(field.Length);
            for (int i = 0; i < field.Length; i++)
            {
                if (field[i] == '"' && i + 1 < field.Length && field[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    sb.Append(field[i]);
                }
            }

            return sb.ToString();
        }

        return field.ToString();
    }

    /// <summary>
    /// Builds a <see cref="CsvRow"/> from a list of values and a row number.
    /// </summary>
    private CsvRow BuildRow(List<string> values, int rowNumber) => new([.. values], rowNumber);

    /// <summary>
    /// Reads the headers from the specified CSV file.
    /// </summary>
    public async Task ReadHeadersAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return;

        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (extension == ".gz")
        {
            extension = Path.GetExtension(Path.GetFileNameWithoutExtension(filePath)).ToLowerInvariant();
        }

        _delimiter = extension switch
        {
            ".tsv" => '\t',
            ".ssv" => ';',
            _ => await DetectDelimiterAsync(filePath, cancellationToken)
        };

        await using IAsyncEnumerator<string> enumerator = BuildParserEnumerator(filePath, cancellationToken);

        if (await enumerator.MoveNextAsync())
        {
            List<string> firstRow = ParseCsvLine(enumerator.Current);
            Headers = Configuration.FirstRowIsHeader
                ? firstRow
                : Enumerable.Range(0, firstRow.Count).Select(GetColumnLetter).ToList();
        }
        else
        {
            Headers = [];
        }
    }

    /// <summary>
    /// Detects whether the delimiter is ',' or ';' based on frequency in the first line.
    /// </summary>
    private async Task<char> DetectDelimiterAsync(string filePath, CancellationToken cancellationToken)
    {
        await using IAsyncEnumerator<string> enumerator = BuildParserEnumerator(filePath, cancellationToken);
        if (!await enumerator.MoveNextAsync()) return ',';
        
        string firstLine = enumerator.Current;
        int commaCount = 0;
        int semicolonCount = 0;
        bool inQuotes = false;

        for (int i = 0; i < firstLine.Length; i++)
        {
            char c = firstLine[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < firstLine.Length && firstLine[i + 1] == '"')
                {
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
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
    ///     Converts a zero-based data column index into its spreadsheet-style column letter.
    /// </summary>
    public static string GetColumnLetter(int columnIndex)
    {
        string letter = string.Empty;
        columnIndex++;
        while (columnIndex > 0)
        {
            columnIndex--;
            letter = (char)('A' + columnIndex % 26) + letter;
            columnIndex /= 26;
        }

        return letter;
    }

    /// <summary>
    /// Counts the total number of rows in the specified CSV file.
    /// </summary>
    public async Task<int> GetRowCountAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath)) return 0;
        int count = 0;
        await using IAsyncEnumerator<string> enumerator = BuildParserEnumerator(filePath, cancellationToken);
        while (await enumerator.MoveNextAsync())
        {
            count++;
        }

        if (Configuration.FirstRowIsHeader && count > 0)
        {
            count--;
        }

        return count;
    }

    /// <summary>
    /// Reads a range of rows from the specified CSV file asynchronously.
    /// </summary>
    public async IAsyncEnumerable<CsvRow> ReadRangeAsyncEnumerable(string filePath, int startRow,
        int endRow, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await ReadHeadersAsync(filePath, cancellationToken);
        int currentRowNumber = 0;
        await using IAsyncEnumerator<string> enumerator = BuildParserEnumerator(filePath, cancellationToken);

        if (startRow <= 0 || endRow < startRow) yield break;

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
    /// Reads a range of rows from the specified CSV file asynchronously and returns them as an <see cref="ObservableCollection{CsvRow}"/>.
    /// </summary>
    public async Task<ObservableCollection<CsvRow>> ReadRangeAsync(string filePath, int startRow,
        int endRow, CancellationToken cancellationToken = default)
    {
        ObservableCollection<CsvRow> rows = [];
        await foreach (CsvRow row in ReadRangeAsyncEnumerable(filePath, startRow, endRow, cancellationToken))
            rows.Add(row);
        return rows;
    }

    /// <summary>
    /// Searches for matches in the CSV file asynchronously.
    /// </summary>
    public async IAsyncEnumerable<(CsvRow Row, string Header, string Value, int RowNumber)>
        ReadMatchesAsyncEnumerable(string filePath, Func<string, bool> matcher, List<string>? headersToSearch,
            int maxMatches, IProgress<int>? progress = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await ReadHeadersAsync(filePath, cancellationToken);
        List<string> headers = (headersToSearch ?? Headers.Prepend(RowNumberKey).ToList()).ToList();
        await using IAsyncEnumerator<string> enumerator = BuildParserEnumerator(filePath, cancellationToken);
        int currentRowNumber = 0;
        int matchCount = 0;

        if (headers.Count == 0) yield break;

        while (matchCount < maxMatches && await enumerator.MoveNextAsync() &&
               !cancellationToken.IsCancellationRequested)
        {
            currentRowNumber++;
            if (currentRowNumber == 1 && Configuration.FirstRowIsHeader) continue;

            CsvRow row = BuildRow(ParseCsvLine(enumerator.Current), currentRowNumber);
            bool foundInThisRow = false;

            // BUG: The code was using 'headers' which might be the parser's internal 'Headers' list.
            // When iterating, it should only check the headers specified in 'headersToSearch'.
            // Wait, 'headers' IS derived from 'headersToSearch'.

            foreach (string header in headers)
            {
                if (matchCount >= maxMatches) break;

                string value = row[header, Headers];
                if (!matcher(value)) continue;

                matchCount++;
                foundInThisRow = true;
                yield return (row, header, value, currentRowNumber);
            }

            if (foundInThisRow) progress?.Report(matchCount);
        }
    }

    /// <summary>
    /// Searches for matches in the CSV file asynchronously and returns them as a list.
    /// </summary>
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
    /// Exports the specified rows to a CSV file.
    /// </summary>
    public async Task ExportToCsvAsync(string filePath, IEnumerable<CsvRow> rows,
        List<string> visibleHeaders, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return;

        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        char delimiter = extension switch
        {
            ".tsv" => '\t',
            ".ssv" => ';',
            _ => ','
        };

        await using StreamWriter writer = new(filePath, false, Encoding.UTF8);

        if (Configuration.FirstRowIsHeader)
            await writer.WriteLineAsync(string.Join(delimiter.ToString(),
                visibleHeaders.Select(h => EscapeCsvField(h, delimiter))));

        foreach (CsvRow row in rows)
        {
            if (cancellationToken.IsCancellationRequested) return;
            IEnumerable<string> values = visibleHeaders.Select(h => row[h, Headers]);
            await writer.WriteLineAsync(string.Join(delimiter.ToString(),
                values.Select(v => EscapeCsvField(v, delimiter))));
        }
    }

    /// <summary>
    /// Escapes a field for CSV, handling delimiters, quotes, and newlines.
    /// </summary>
    private static string EscapeCsvField(string field, char delimiter)
    {
        if (string.IsNullOrEmpty(field)) return string.Empty;
        if (field.Contains(delimiter) || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }
}