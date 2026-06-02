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

public class CsvRow(string[] values, int rowNumber)
{
    public string[] Values { get; } = values;
    public int RowNumber { get; } = rowNumber;

    public string this[int index] => index >= 0 && index < Values.Length ? Values[index] : string.Empty;

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

public class Parser
{
    // Variables & applied objects

    public const string RowNumberKey = "__CsvRowNumber";
    private char _delimiter = ',';
    private string[] _csvFilePatterns = [];
    public List<string> Headers { get; private set; } = [];

    // Constructor methods
    private StreamReader BuildReader(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or whitespace", nameof(filePath));

        if (_csvFilePatterns.Length == 0)
        {
            _csvFilePatterns = Configuration.CsvFilePatterns.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries);
        }

        var stream = File.OpenRead(filePath);
        try
        {
            if (!_csvFilePatterns.Contains("*" + Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase))
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
        var fields = new List<string>(Headers.Count > 0 ? Headers.Count : Math.Max(1, line.Length / 8));
        if (line.Length == 0)
        {
            fields.Add(string.Empty);
            return fields;
        }

        var lineSpan = line.AsSpan();
        var inQuotes = false;
        var start = 0;

        for (var i = 0; i < lineSpan.Length; i++)
        {
            var c = lineSpan[i];

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

    private static string UnescapeField(ReadOnlySpan<char> field)
    {
        if (field.Length == 0) return string.Empty;
        if (field[0] == '"' && field[^1] == '"')
        {
            field = field[1..^1];
            if (field.IndexOf('"') == -1) return field.ToString();

            var sb = new StringBuilder(field.Length);
            for (var i = 0; i < field.Length; i++)
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

    private CsvRow BuildRow(List<string> values, int rowNumber)
    {
        return new CsvRow([..values], rowNumber);
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

    public async IAsyncEnumerable<CsvRow> ReadRangeAsyncEnumerable(string filePath, int startRow,
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

    public async Task<ObservableCollection<CsvRow>> ReadRangeAsync(string filePath, int startRow,
        int endRow, CancellationToken cancellationToken = default)
    {
        var rows = new ObservableCollection<CsvRow>();
        await foreach (var row in ReadRangeAsyncEnumerable(filePath, startRow, endRow, cancellationToken))
            rows.Add(row);
        return rows;
    }

    public async IAsyncEnumerable<(CsvRow Row, string Header, string Value, int RowNumber)>
        ReadMatchesAsyncEnumerable(string filePath, Func<string, bool> matcher, List<string>? headersToSearch,
            int maxMatches, IProgress<int>? progress = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await ReadHeadersAsync(filePath, cancellationToken);
        // Variables
        var headers = headersToSearch ?? Headers.Prepend(RowNumberKey).ToList();
        await using var enumerator = BuildParserEnumerator(filePath, cancellationToken);
        var currentRowNumber = 0;
        var matchCount = 0;

        // Exception prevention
        if (headers.Count == 0) yield break;

        // Processing
        while (matchCount < maxMatches && await enumerator.MoveNextAsync() &&
               !cancellationToken.IsCancellationRequested)
        {
            currentRowNumber++;
            if (currentRowNumber == 1 && Configuration.FirstRowIsHeader) continue;

            var row = BuildRow(ParseCsvLine(enumerator.Current), currentRowNumber);
            var foundInThisRow = false;
            foreach (var header in headers.TakeWhile(header => matchCount < maxMatches))
            {
                var value = row[header, Headers];
                if (!matcher(value)) continue;
                matchCount++;
                foundInThisRow = true;
                yield return (row, header, value, currentRowNumber);
            }

            if (foundInThisRow) progress?.Report(matchCount);
        }
    }

    public async Task<List<(CsvRow Row, string Header, string Value, int RowNumber)>>
        ReadMatchesAsync(string filePath, Func<string, bool> matcher, List<string>? headersToSearch, int maxMatches,
            IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var matches = new List<(CsvRow Row, string Header, string Value, int RowNumber)>();
        await foreach (var match in ReadMatchesAsyncEnumerable(filePath, matcher, headersToSearch, maxMatches, progress,
                           cancellationToken)) matches.Add(match);
        return matches;
    }

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

        await using var writer = new StreamWriter(filePath, false, Encoding.UTF8);

        // If the first row is data (not header), we should not write header identifier letters (A, B, C...)
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

    private static string EscapeCsvField(string field, char delimiter)
    {
        if (string.IsNullOrEmpty(field)) return string.Empty;
        if (field.Contains(delimiter) || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }
}