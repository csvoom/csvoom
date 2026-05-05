/// <summary>
/// Author: Lassi
/// Description: Contains the logic for parsing CSV files and extracting the necessary information for further processing.
/// </summary>

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Microsoft.VisualBasic.FileIO;

namespace CSVoom.Parser
{
    public class CSVParser
    {
        public List<string> queue = new List<string>();
        public void ParseCSV(string filePath)
        {
            if (!File.Exists(filePath))
            { // The file does not exist, log an error message and return
                Console.WriteLine("File not found: " + filePath);
                return;
            }

            else if (Path.GetExtension(filePath).Equals(".gz", StringComparison.OrdinalIgnoreCase))
            { // If the file is in GZIP format, the file is decompressed and parsed at the same time through a stream 
                parseDataStream(new GZipStream(File.OpenRead(filePath), CompressionMode.Decompress));
            }

            else if (Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            { // If the file is in CSV format, it is parsed directly
                parseDataStream(File.OpenRead(filePath));
            }

            else
            { // The file is using an unsupported format. Log an error and return
                Console.WriteLine("Unsupported file format: " + filePath);
                return;
            }
        }

        private void parseDataStream(Stream dataStream)
        {
            using TextFieldParser parser = new TextFieldParser(dataStream);
            parser.SetDelimiters(",");
            while (!parser.EndOfData)
            {
                string readLine = parser.ReadLine() + "";
                queue.Add(readLine);
            }
        }
    }
}
/// <remarks>
/// For now, this code reads the file in the embedded path and prints its fields into the console.
/// It is intended to send the data to the UI for display, but that is for later implementation.
/// </remarks>

