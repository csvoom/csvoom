/// <summary>
/// Author: Lassi
/// Description: Contains the logic for parsing CSV files and extracting the necessary information for further processing.
/// </summary>

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace CSVoom.Parser
{
    public class Parser
    {
        /// <summary>
        /// Asynchronously enumerates raw lines from the CSV file (or decompressed GZIP stream).
        /// Each yielded item is one raw CSV line string.
        /// </summary>
        public async IAsyncEnumerator<string> parserLineEnumerator(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine("File not found: " + filePath);
                yield break;
            }

            string ext = Path.GetExtension(filePath);
            if (ext.Equals(".gz", StringComparison.OrdinalIgnoreCase))
            { // Open the GZIP file and read lines from the decompressed stream.
                using FileStream fileStream = File.OpenRead(filePath);
                using GZipStream gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
                using (StreamReader reader = new StreamReader(gzipStream))
                {
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        yield return line;
                        await Task.Yield();
                    }
                }
            }
            else if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            { // Open the CSV file and read lines directly.
                using (StreamReader reader = File.OpenText(filePath))
                {
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        yield return line;
                        await Task.Yield();
                    }
                }
            }
            else
            {
                Console.WriteLine("Unsupported file format: " + filePath);
                yield break;
            }
        }

        /// <summary>
        /// Convenience method: reads all raw lines into a list asynchronously.
        /// </summary>
        public async Task<List<string>> ReadAllLinesAsync(string filePath)
        {
            var results = new List<string>();
            await using var enumerator = parserLineEnumerator(filePath);
            while (await enumerator.MoveNextAsync())
            {
                results.Add(enumerator.Current);
            }
            return results;
        }
    }
}

