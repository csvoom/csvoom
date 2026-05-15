using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;

namespace CSVoom.app
{
    public class Parser
    {
        public const string RowNumberKey = "__CsvRowNumber";

        public IReadOnlyList<string> Headers { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// Asynchronously lists raw lines from the CSV file or decompressed GZIP stream.
        /// Each yielded item is one raw CSV line string.
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

        public async Task<IReadOnlyList<string>> ReadHeadersAsync(string filePath)
        {
            await using var enumerator = ParserLineEnumerator(filePath);

            if (await enumerator.MoveNextAsync())
            {
                Headers = ParseCsvLine(enumerator.Current);
            }
            else
            {
                Headers = Array.Empty<string>();
            }

            return Headers;
        }

        public async Task<ObservableCollection<Dictionary<string, string>>> ReadRangeAsync(
            string filePath,
            int startRow,
            int endRow,
            int maxRows)
        {
            var rows = new ObservableCollection<Dictionary<string, string>>();

            if (startRow <= 0 || endRow < startRow || maxRows <= 0)
            {
                return rows;
            }

            await EnsureHeadersLoadedAsync(filePath);

            await using var enumerator = ParserLineEnumerator(filePath);

            if (!await enumerator.MoveNextAsync())
            {
                return rows;
            }

            var currentRowNumber = 0;

            while (await enumerator.MoveNextAsync())
            {
                currentRowNumber++;

                if (currentRowNumber < startRow)
                {
                    continue;
                }

                if (currentRowNumber > endRow || rows.Count >= maxRows)
                {
                    break;
                }

                rows.Add(BuildRow(ParseCsvLine(enumerator.Current), currentRowNumber));
            }

            return rows;
        }

        public async Task<ObservableCollection<Dictionary<string, string>>> ReadMatchingRowsAsync(
            string filePath,
            Func<Dictionary<string, string>, bool> predicate,
            int maxRows)
        {
            var rows = new ObservableCollection<Dictionary<string, string>>();

            if (maxRows <= 0)
            {
                return rows;
            }

            await EnsureHeadersLoadedAsync(filePath);

            await using var enumerator = ParserLineEnumerator(filePath);

            if (!await enumerator.MoveNextAsync())
            {
                return rows;
            }

            var currentRowNumber = 0;

            while (await enumerator.MoveNextAsync())
            {
                currentRowNumber++;

                var row = BuildRow(ParseCsvLine(enumerator.Current), currentRowNumber);

                if (!predicate(row))
                {
                    continue;
                }

                rows.Add(row);

                if (rows.Count >= maxRows)
                {
                    break;
                }
            }

            return rows;
        }

        public async Task<(Dictionary<string, string> Row, string Header, int RowNumber)?> FindFirstAsync(
            string filePath,
            string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                return null;
            }

            await EnsureHeadersLoadedAsync(filePath);

            await using var enumerator = ParserLineEnumerator(filePath);

            if (!await enumerator.MoveNextAsync())
            {
                return null;
            }

            var currentRowNumber = 0;

            while (await enumerator.MoveNextAsync())
            {
                currentRowNumber++;

                var row = BuildRow(ParseCsvLine(enumerator.Current), currentRowNumber);

                foreach (var header in Headers)
                {
                    if (!row.TryGetValue(header, out var value))
                    {
                        continue;
                    }

                    if (value.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    {
                        return (row, header, currentRowNumber);
                    }
                }
            }

            return null;
        }

        private async Task EnsureHeadersLoadedAsync(string filePath)
        {
            if (Headers.Count > 0)
            {
                return;
            }

            await ReadHeadersAsync(filePath);
        }

        private Dictionary<string, string> BuildRow(IReadOnlyList<string> values, int rowNumber)
        {
            var row = new Dictionary<string, string>
            {
                [RowNumberKey] = rowNumber.ToString()
            };

            for (var i = 0; i < Headers.Count; i++)
            {
                row[Headers[i]] = i < values.Count ? values[i] : string.Empty;
            }

            return row;
        }

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
}

