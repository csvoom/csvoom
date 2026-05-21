using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSVoom.app;

public class Parser
{
    // Variables & applied objects

    public const string RowNumberKey = "__CsvRowNumber";

    public IReadOnlyList<string> Headers { get; private set; } = Array.Empty<string>();

    /// <summary>
    ///     Asynchronously lists raw lines from a CSV file or decompressed GZIP stream.
    /// </summary>
    private async IAsyncEnumerator<string> ParserLineEnumerator(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine("File not found: " + filePath);
            yield break;
        }

        var ext = Path.GetExtension(filePath);

        if (ext.Equals(".gz", StringComparison.OrdinalIgnoreCase))
        {
            await using var fileStream = File.OpenRead(filePath);
            await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzipStream);

            while (await reader.ReadLineAsync() is { } line)
            {
                yield return line;
                await Task.Yield();
            }

            yield break;
        }

        if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = File.OpenText(filePath);

            while (await reader.ReadLineAsync() is { } line)
            {
                yield return line;
                await Task.Yield();
            }

            yield break;
        }

        Console.WriteLine("Unsupported file format: " + filePath);
    }

    // Parser methods

    /// <summary>
    ///     Reads the first row of the file and stores it as the parser's header collection.
    /// </summary>
    public async Task<IReadOnlyList<string>> ReadHeadersAsync(string filePath)
    {
        await using var enumerator = ParserLineEnumerator(filePath);

        if (await enumerator.MoveNextAsync())
            Headers = ParseCsvLine(enumerator.Current);
        else
            Headers = Array.Empty<string>();

        return Headers;
    }

    /// <summary>
    ///     Reads a bounded range of CSV rows and converts them into dictionaries keyed by the header name.
    /// </summary>
    public async Task<ObservableCollection<Dictionary<string, string>>> ReadRangeAsync(
        string filePath,
        int startRow,
        int endRow,
        int maxRows)
    {
        var rows = new ObservableCollection<Dictionary<string, string>>();

        if (startRow <= 0 || endRow < startRow || maxRows <= 0) return rows;

        await EnsureHeadersLoadedAsync(filePath);

        await using var enumerator = ParserLineEnumerator(filePath);

        if (!await enumerator.MoveNextAsync()) return rows;

        var currentRowNumber = 1;

        while (await enumerator.MoveNextAsync())
        {
            currentRowNumber++;

            if (currentRowNumber < startRow) continue;

            if (currentRowNumber > endRow || rows.Count >= maxRows) break;

            rows.Add(BuildRow(ParseCsvLine(enumerator.Current), currentRowNumber));
        }

        return rows;
    }

    /// <summary>
    ///     Reads rows that satisfy the supplied predicate, up to the requested maximum number of rows.
    /// </summary>
    /// <summary>
    ///     Reads rows that satisfy the supplied predicate, up to the requested maximum number of rows.
    /// </summary>
    public async Task<ObservableCollection<Dictionary<string, string>>> ReadMatchingRowsAsync(
        string filePath,
        Func<Dictionary<string, string>, bool> predicate,
        int maxRows)
    {
        var rows = new ObservableCollection<Dictionary<string, string>>();

        if (maxRows <= 0) return rows;

        await EnsureHeadersLoadedAsync(filePath);

        await using var enumerator = ParserLineEnumerator(filePath);

        if (!await enumerator.MoveNextAsync()) return rows;

        var currentRowNumber = 1;

        while (await enumerator.MoveNextAsync())
        {
            currentRowNumber++;

            var row = BuildRow(ParseCsvLine(enumerator.Current), currentRowNumber);

            if (!predicate(row)) continue;

            rows.Add(row);

            if (rows.Count >= maxRows) break;
        }

        return rows;
    }

    /// <summary>
    ///     Finds the first occurrence of the specified text in the CSV data, optionally within a single header.
    /// </summary>
    public Task<(Dictionary<string, string> Row, string Header, int RowNumber)?> FindFirstAsync(
        string filePath,
        string searchText,
        string? searchHeader = null)
    {
        return FindNextAsync(filePath, searchText, searchHeader);
    }

    /// <summary>
    ///     Finds the next occurrence of the specified text after the supplied row and header.
    /// </summary>
    public async Task<(Dictionary<string, string> Row, string Header, int RowNumber)?> FindNextAsync(
        string filePath,
        string searchText,
        string? searchHeader = null,
        int startAfterRowNumber = 0,
        string? startAfterHeader = null)
    {
        if (string.IsNullOrWhiteSpace(searchText)) return null;

        await EnsureHeadersLoadedAsync(filePath);

        if (!string.IsNullOrWhiteSpace(searchHeader)
            && !searchHeader.Equals(RowNumberKey, StringComparison.OrdinalIgnoreCase)
            && !Headers.Contains(searchHeader, StringComparer.OrdinalIgnoreCase))
            return null;

        await using var enumerator = ParserLineEnumerator(filePath);

        if (!await enumerator.MoveNextAsync()) return null;

        var currentRowNumber = 1;
        var headersToSearch = string.IsNullOrWhiteSpace(searchHeader)
            ? Headers.Prepend(RowNumberKey).ToArray()
            : searchHeader.Equals(RowNumberKey, StringComparison.OrdinalIgnoreCase)
                ? [RowNumberKey]
                : Headers
                    .Where(header => header.Equals(searchHeader, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

        var startAfterHeaderIndex = string.IsNullOrWhiteSpace(startAfterHeader)
            ? -1
            : Array.FindIndex(
                headersToSearch,
                header => header.Equals(startAfterHeader, StringComparison.OrdinalIgnoreCase));

        var headerRow = new Dictionary<string, string>
        {
            [RowNumberKey] = currentRowNumber.ToString()
        };

        foreach (var header in Headers) headerRow[header] = header;

        var headerMatch = FindMatchInRow(
            headerRow,
            headersToSearch,
            searchText,
            currentRowNumber,
            startAfterRowNumber,
            startAfterHeaderIndex);

        if (headerMatch is not null) return headerMatch;

        while (await enumerator.MoveNextAsync())
        {
            currentRowNumber++;

            var row = BuildRow(ParseCsvLine(enumerator.Current), currentRowNumber);

            var match = FindMatchInRow(
                row,
                headersToSearch,
                searchText,
                currentRowNumber,
                startAfterRowNumber,
                startAfterHeaderIndex);

            if (match is not null) return match;
        }

        return null;
    }

    /// <summary>
    ///     Finds a matching value in the supplied row while respecting the requested starting position.
    /// </summary>
    private static (Dictionary<string, string> Row, string Header, int RowNumber)? FindMatchInRow(
        Dictionary<string, string> row,
        IReadOnlyList<string> headersToSearch,
        string searchText,
        int currentRowNumber,
        int startAfterRowNumber,
        int startAfterHeaderIndex)
    {
        if (currentRowNumber < startAfterRowNumber) return null;

        var firstHeaderIndex = currentRowNumber == startAfterRowNumber
            ? startAfterHeaderIndex + 1
            : 0;

        for (var headerIndex = firstHeaderIndex; headerIndex < headersToSearch.Count; headerIndex++)
        {
            var header = headersToSearch[headerIndex];

            if (!row.TryGetValue(header, out var value)) continue;

            if (value.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                return (row, header, currentRowNumber);
        }

        return null;
    }

    // Builder methods

    /// <summary>
    ///     Loads headers from the file if they have not already been read.
    /// </summary>
    private async Task EnsureHeadersLoadedAsync(string filePath)
    {
        if (Headers.Count > 0) return;

        await ReadHeadersAsync(filePath);
    }

    /// <summary>
    ///     Builds a row dictionary from parsed field values and includes the CSV row number.
    /// </summary>
    private Dictionary<string, string> BuildRow(IReadOnlyList<string> values, int rowNumber)
    {
        var row = new Dictionary<string, string>
        {
            [RowNumberKey] = rowNumber.ToString()
        };

        for (var i = 0; i < Headers.Count; i++) row[Headers[i]] = i < values.Count ? values[i] : string.Empty;

        return row;
    }

    /// <summary>
    ///     Parses a single CSV line into field values, handling quoted fields and escaped quotes.
    /// </summary>
    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}