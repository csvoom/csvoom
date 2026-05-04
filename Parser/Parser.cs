/// <summary>
/// Author: Lassi
/// Description: Contains the logic for parsing CSV files and extracting the necessary information for further processing.
/// </summary>

using System.IO.Compression;
using Microsoft.VisualBasic.FileIO;

//void Main() // TEST PURPOSE
//{
//    Parser.CSVParser parser = new Parser.CSVParser();
//    parser.ParseCSV("C:\\Users\\Gilas\\Downloads\\4G_DIS9_mr_20230726-200001_20230726-214508_import.csv.gz");
//}
//Main();

namespace Parser
{
    class CSVParser
    {
        public void ParseCSV(string filePath)
        {
            if (!File.Exists(filePath))
            { // The file does not exist, log an error message and return
                Console.WriteLine("File not found: " + filePath);
                return;
            }

            else if (Path.GetExtension(filePath).Equals(".gz", StringComparison.OrdinalIgnoreCase))
            { // If the file is in GZIP format, the file is decompressed and parsed at the same time through a stream
                using GZipStream stream = new GZipStream(File.OpenRead(filePath), CompressionMode.Decompress);
                using TextFieldParser parser = new TextFieldParser(stream);
                returnSegments(parser);
            }

            else
            { // If the file is in CSV format, it is parsed directly
                using TextFieldParser parser = new TextFieldParser(filePath);
                returnSegments(parser);
            }
        }
        private void returnSegments(TextFieldParser parser)
        { // The method that reads the CSV file and returns the segments to the UI for display
            parser.SetDelimiters(",");
            while (!parser.EndOfData)
            {
                string[] fields = parser.ReadFields();
                Console.WriteLine(string.Join(", ", fields));
            }
        }
    }
}
/// <remarks>
/// For now, this code reads the file in the embedded path and prints its fields into the console.
/// It is intended to send the data to the UI for display, but that is for later implementation.
/// </remarks>

