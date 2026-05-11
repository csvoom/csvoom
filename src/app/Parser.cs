using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace CSVoom.app
{
    public class Parser
    {
        private const int BatchSize = 20;

        private bool _isLoadingBatch;
        private bool _finishedLoading;
        private bool _headersLoaded;

        private IAsyncEnumerator<string>? _csvEnumerator;
        private string? _currentFilePath;

        public ObservableCollection<Dictionary<string, string>> Rows { get; } = new();

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
            { // Open the GZIP file and read lines from the decompressed stream.
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

        /// <summary>
        /// Convenience method: reads all raw lines into a list asynchronously.
        /// </summary>
        /// <summary>
        /// Reads the next batch of CSV rows asynchronously.
        /// </summary>
        public async Task<ObservableCollection<Dictionary<string, string>>> ReadBatchAsync(string filePath)
        {
            if (_isLoadingBatch || _finishedLoading && _currentFilePath == filePath)
            {
                return Rows;
            }

            _isLoadingBatch = true;

            try
            {
                if (_csvEnumerator is null || _currentFilePath != filePath)
                {
                    if (_csvEnumerator is not null)
                    {
                        await _csvEnumerator.DisposeAsync();
                    }

                    _currentFilePath = filePath;
                    _csvEnumerator = ParserLineEnumerator(filePath);
                    _headersLoaded = false;
                    _finishedLoading = false;
                    Rows.Clear();
                    Headers = [];
                }

                if (!_headersLoaded)
                {
                    if (await _csvEnumerator.MoveNextAsync())
                    {
                        Headers = ParseCsvLine(_csvEnumerator.Current);
                        _headersLoaded = true;
                    }
                    else
                    {
                        _finishedLoading = true;
                        await _csvEnumerator.DisposeAsync();
                        _csvEnumerator = null;
                        return Rows;
                    }
                }

                var loadedThisBatch = 0;

                while (loadedThisBatch < BatchSize && await _csvEnumerator.MoveNextAsync())
                {
                    var values = ParseCsvLine(_csvEnumerator.Current);
                    var row = new Dictionary<string, string>();

                    for (var i = 0; i < Headers.Count; i++)
                    {
                        row[Headers[i]] = i < values.Count ? values[i] : string.Empty;
                    }

                    Rows.Add(row);
                    loadedThisBatch++;
                }

                if (loadedThisBatch == 0)
                {
                    _finishedLoading = true;

                    await _csvEnumerator.DisposeAsync();
                    _csvEnumerator = null;
                }

                return Rows;
            }
            finally
            {
                _isLoadingBatch = false;
            }
        }

        private static IReadOnlyList<string> ParseCsvLine(string line)
        {
            return line.Split(',');
        }
    }
}

