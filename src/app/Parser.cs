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
        private const int BatchSize = 50;

        private int _loadedLineCount;
        private bool _isLoadingBatch;
        private bool _finishedLoading;

        private IAsyncEnumerator<string>? _csvEnumerator;
        private string? _currentFilePath;

        public ObservableCollection<string> Lines { get; } = new();

        /// <summary>
        /// Asynchronously enumerates raw lines from the CSV file or decompressed GZIP stream.
        /// Each yielded item is one raw CSV line string.
        /// </summary>
        public async IAsyncEnumerator<string> ParserLineEnumerator(string filePath)
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

        /// <summary>
        /// Convenience method: reads all raw lines into a list asynchronously.
        /// </summary>
        public async Task<List<string>> ReadAllLinesAsync(string filePath)
        {
            var results = new List<string>();

            await using var enumerator = ParserLineEnumerator(filePath);

            while (await enumerator.MoveNextAsync())
            {
                results.Add(enumerator.Current);
            }

            return results;
        }

        /// <summary>
        /// Reads the next batch of lines into the observable collection.
        /// The same collection can be observed from another class.
        /// </summary>
        public async Task<ObservableCollection<string>> ReadBatchAsync(string filePath)
        {
            if (_isLoadingBatch || _finishedLoading)
            {
                return Lines;
            }

            _isLoadingBatch = true;

            try
            {
                if (_csvEnumerator is null || _currentFilePath != filePath)
                {
                    _currentFilePath = filePath;
                    _csvEnumerator = ParserLineEnumerator(filePath);
                }

                var loadedThisBatch = 0;

                while (loadedThisBatch < BatchSize && await _csvEnumerator.MoveNextAsync())
                {
                    Lines.Add(_csvEnumerator.Current);

                    _loadedLineCount++;
                    loadedThisBatch++;
                }

                if (loadedThisBatch == 0)
                {
                    _finishedLoading = true;

                    await _csvEnumerator.DisposeAsync();
                    _csvEnumerator = null;
                }

                return Lines;
            }
            finally
            {
                _isLoadingBatch = false;
            }
        }
    }
}

