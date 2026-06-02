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

public class Parser
{
    // Variables & applied objects

    public const string RowNumberKey = "__CsvRowNumber";
    private char _delimiter = ',';
    public List<string> Headers { get; private set; } = [];

    // Constructor methods
    private static StreamReader BuildReader(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or whitespace", nameof(filePath));

        var stream = File.OpenRead(filePath);
        try
        {
            var patterns =
                Configuration.CsvFilePatterns.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries);
            if (!patterns.Contains("*" + Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"Unsupported file format: {Path.GetExtension(filePath)}");
            if (filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                return new StreamReader(new GZipStream(stream, CompressionMode.Decompress), Encoding.UTF8, true);
            return new StreamReader(stream, Encoding.UTF8, true);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private async IAsyncEnumerator<string> BuildParserEnumerator(string filePath, CancellationToken cancel = default)
    {
        if (!File.Exists(filePath))
        {
            yield break;
        }

        using var reader = BuildReader(filePath);
        while (await reader.ReadLineAsync(cancel) is { } line) yield return line;
    }

    private List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>(Math.Max(1, line.Length / 8));
        var current = new StringBuilder(Math.Min(line.Length, 256));
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
            else if (c == _delimiter && !inQuotes)
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

    private Dictionary<string, string> BuildRow(List<string> values, int rowNumber)
    {
        var row = new Dictionary<string, string>(Headers.Count + 1)
        {
            [RowNumberKey] = rowNumber.ToString()
        };

        for (var i = 0; i < Headers.Count; i++) row[Headers[i]] = i < values.Count ? values[i] : string.Empty;

        return row;
    }

    public async Task ReadHeadersAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return;

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (extension == ".gz")
        {
            var fileNameWithoutGz = Path.GetFileNameWithoutExtension(filePath);
            extension = Path.GetExtension(fileNameWithoutGz).ToLowerInvariant();
        }

        _delimiter = extension switch
        {
            ".tsv" => '\t',
            ".ssv" => ';',
            _ => ','
        };

        await using var enumerator = BuildParserEnumerator(filePath, cancellationToken);

        if (await enumerator.MoveNextAsync())
        {
            var firstRow = ParseCsvLine(enumerator.Current);
            if (Configuration.FirstRowIsHeader)
            {
                Headers = firstRow;
            }
            else
            {
                Headers = new List<string>(firstRow.Count);
                for (var i = 0; i < firstRow.Count; i++) Headers.Add(GetColumnLetter(i));
            }
        }
        else
        {
            Headers = [];
        }
    }

    /// <summary>
    ///     Converts a zero-based data column index into its spreadsheet-style column letter.
    /// </summary>
    public static string GetColumnLetter(int columnIndex)
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

    public async IAsyncEnumerable<Dictionary<string, string>> ReadRangeAsyncEnumerable(string filePath, int startRow,
        int endRow, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await ReadHeadersAsync(filePath, cancellationToken);
        // Variables
        var currentRowNumber = 0;
        await using var enumerator = BuildParserEnumerator(filePath, cancellationToken);

        // Exception prevention
        if (startRow <= 0 || endRow < startRow) yield break;

        // Processing
        while (await enumerator.MoveNextAsync() && !cancellationToken.IsCancellationRequested)
        {
            currentRowNumber++;
            if (currentRowNumber == 1 && Configuration.FirstRowIsHeader) continue;

            if (currentRowNumber < startRow) continue;
            if (currentRowNumber > endRow) break;
            yield return BuildRow(ParseCsvLine(enumerator.Current), currentRowNumber);
        }
    }

    public async Task<ObservableCollection<Dictionary<string, string>>> ReadRangeAsync(string filePath, int startRow,
        int endRow, CancellationToken cancellationToken = default)
    {
        var rows = new ObservableCollection<Dictionary<string, string>>();
        await foreach (var row in ReadRangeAsyncEnumerable(filePath, startRow, endRow, cancellationToken))
            rows.Add(row);
        return rows;
    }

    public async IAsyncEnumerable<(Dictionary<string, string> Row, string Header, string Value, int RowNumber)>
        ReadMatchesAsyncEnumerable(string filePath, Func<string, bool> matcher, List<string>? headersToSearch,
            int maxMatches, IProgress<int>? progress = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await ReadHeadersAsync(filePath, cancellationToken);
        // Variables
        var headers = headersToSearch ?? Headers.Prepend(RowNumberKey);
        await using var enumerator = BuildParserEnumerator(filePath, cancellationToken);
        var currentRowNumber = 0;
        var matchCount = 0;

        // Exception prevention
        var enumerable = headers.ToList();
        if (enumerable.Count == 0) yield break;

        // Processing
        while (matchCount < maxMatches && await enumerator.MoveNextAsync() &&
               !cancellationToken.IsCancellationRequested)
        {
            currentRowNumber++;
            if (currentRowNumber == 1 && Configuration.FirstRowIsHeader) continue;

            var row = BuildRow(ParseCsvLine(enumerator.Current), currentRowNumber);
            var foundInThisRow = false;
            foreach (var header in enumerable.TakeWhile(header => matchCount < maxMatches))
            {
                if (!row.TryGetValue(header, out var value) || !matcher(value)) continue;
                matchCount++;
                foundInThisRow = true;
                yield return (row, header, value, currentRowNumber);
            }

            if (foundInThisRow) progress?.Report(matchCount);
        }
    }

    public async Task<List<(Dictionary<string, string> Row, string Header, string Value, int RowNumber)>>
        ReadMatchesAsync(string filePath, Func<string, bool> matcher, List<string>? headersToSearch, int maxMatches,
            IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var matches = new List<(Dictionary<string, string> Row, string Header, string Value, int RowNumber)>();
        await foreach (var match in ReadMatchesAsyncEnumerable(filePath, matcher, headersToSearch, maxMatches, progress,
                           cancellationToken)) matches.Add(match);
        return matches;
    }

    public async Task ExportToCsvAsync(string filePath, IEnumerable<Dictionary<string, string>> rows,
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

        await using var writer = new StreamWriter(filePath, false, Encoding.UTF8);

        // If the first row is data (not header), we should not write header identifier letters (A, B, C...)
        if (Configuration.FirstRowIsHeader)
            await writer.WriteLineAsync(string.Join(delimiter.ToString(),
                visibleHeaders.Select(h => EscapeCsvField(h, delimiter))));

        foreach (var row in rows)
        {
            if (cancellationToken.IsCancellationRequested) return;
            var values = visibleHeaders.Select(h => row.TryGetValue(h, out var v) ? v : string.Empty);
            await writer.WriteLineAsync(string.Join(delimiter.ToString(),
                values.Select(v => EscapeCsvField(v, delimiter))));
        }
    }

    private static string EscapeCsvField(string field, char delimiter)
    {
        if (string.IsNullOrEmpty(field)) return string.Empty;
        if (field.Contains(delimiter) || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }
}