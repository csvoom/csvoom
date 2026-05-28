using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CSVoom.app;

public class Parser
{
    // Variables & applied objects

    public const string RowNumberKey = "__CsvRowNumber";

    public IReadOnlyList<string> Headers { get; private set; } = [];
    
    // Constructor methods
    private static StreamReader BuildReader(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be null or whitespace", nameof(filePath));
        
        var reader = File.OpenRead(filePath);
        if (filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return new StreamReader(reader);
        }
        if (filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            return new StreamReader(new GZipStream(reader, CompressionMode.Decompress));
        }
        throw new ArgumentException($"Unsupported file format: {Path.GetExtension(filePath)}");
    }
    private static async IAsyncEnumerator<string> BuildParserEnumerator(string filePath, CancellationToken cancel = default)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine("File not found: " + filePath);
            yield break;
        }

        using var reader = BuildReader(filePath);
        while (await reader.ReadLineAsync(cancel) is { } line)
        {
            yield return line;
        }
    }
    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>(Math.Max(1, line.Length / 8));
        var current = new StringBuilder(Math.Min(line.Length, 256));
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            switch (c)
            {
                case '"' when inQuotes && i + 1 < line.Length && line[i + 1] == '"':
                    current.Append('"');
                    i++;
                    break;
                case '"':
                    inQuotes = !inQuotes;
                    break;
                case ',' when !inQuotes:
                    fields.Add(current.ToString());
                    current.Clear();
                    break;
                default:
                    current.Append(c);
                    break;
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
    private Dictionary<string, string> BuildRow(IReadOnlyList<string> values, int rowNumber)
    {
        var row = new Dictionary<string, string>(Headers.Count + 1)
        {
            [RowNumberKey] = rowNumber.ToString()
        };

        for (var i = 0; i < Headers.Count; i++) row[Headers[i]] = i < values.Count ? values[i] : string.Empty;

        return row;
    }
    
    /// <summary>
    ///     Reads the first row of the file and stores it as the parser's header collection.
    /// </summary>
    public async Task ReadHeadersAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return;
        await using var enumerator = BuildParserEnumerator(filePath, cancellationToken);

        if (await enumerator.MoveNextAsync())
        {
            Headers = ParseCsvLine(enumerator.Current);
        }
        else
        {
            Headers = [];
        }
    }
    
    public async IAsyncEnumerable<Dictionary<string, string>> ReadRangeAsyncEnumerable(string filePath, int startRow, int endRow, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await ReadHeadersAsync(filePath, cancellationToken);
        // Variables
        var currentRowNumber = 1;
        await using var enumerator = BuildParserEnumerator(filePath, cancellationToken);
        
        // Exception prevention
        if (startRow <= 0 || endRow < startRow || !await enumerator.MoveNextAsync()) yield break;

        // Processing
        while (await enumerator.MoveNextAsync() && !cancellationToken.IsCancellationRequested)
        {
            currentRowNumber++;
            if (currentRowNumber < startRow) continue;
            if (currentRowNumber > endRow) break;
            yield return BuildRow(ParseCsvLine(enumerator.Current), currentRowNumber);
        }
    }

    public async Task<ObservableCollection<Dictionary<string, string>>> ReadRangeAsync(string filePath, int startRow, int endRow, CancellationToken cancellationToken = default)
    {
        var rows = new ObservableCollection<Dictionary<string, string>>();
        await foreach (var row in ReadRangeAsyncEnumerable(filePath, startRow, endRow, cancellationToken))
        {
            rows.Add(row);
        }
        return rows;
    }

    public async IAsyncEnumerable<(Dictionary<string, string> Row, string Header, string Value, int RowNumber)> ReadMatchesAsyncEnumerable(string filePath, Func<string, bool> matcher, IReadOnlyList<string>? headersToSearch, int maxMatches, [EnumeratorCancellation] CancellationToken cancellationToken = default, IProgress<int>? progress = null)
    {
        await ReadHeadersAsync(filePath, cancellationToken);
        // Variables
        var headers = headersToSearch ?? Headers.Prepend(RowNumberKey).ToArray();
        await using var enumerator = BuildParserEnumerator(filePath, cancellationToken);
        var currentRowNumber = 1;
        var matchCount = 0;
        
        // Exception prevention
        if (!headers.Any()) yield break;
        
        // Processing
        if (!await enumerator.MoveNextAsync()) yield break;

        while (matchCount < maxMatches && await enumerator.MoveNextAsync() && !cancellationToken.IsCancellationRequested)
        {
            currentRowNumber++;
            var row = BuildRow(ParseCsvLine(enumerator.Current), currentRowNumber);
            var foundInThisRow = false;
            foreach (var header in headers)
            {
                if (matchCount >= maxMatches) break;
                if (!row.TryGetValue(header, out var value) || !matcher(value)) continue;
                matchCount++;
                foundInThisRow = true;
                yield return (row, header, value, currentRowNumber);
            }
            if (foundInThisRow) progress?.Report(matchCount);
        }
    }

    public async Task<IReadOnlyList<(Dictionary<string, string> Row, string Header, string Value, int RowNumber)>> ReadMatchesAsync(string filePath, Func<string, bool> matcher, IReadOnlyList<string>? headersToSearch, int maxMatches, CancellationToken cancellationToken = default, IProgress<int>? progress = null)
    {
        var matches = new List<(Dictionary<string, string> Row, string Header, string Value, int RowNumber)>();
        await foreach (var match in ReadMatchesAsyncEnumerable(filePath, matcher, headersToSearch, maxMatches, cancellationToken, progress))
        {
            matches.Add(match);
        }
        return matches;
    }
}