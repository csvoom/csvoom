using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace CSVoom.app
{
    public class Parser
    {
        /// <summary>
        /// Asynchronously enumerates raw lines from the CSV file (or decompressed GZIP stream).
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
                // Open the CSV file and read lines directly.
                using StreamReader reader = File.OpenText(filePath);
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
    }
}

