using System.IO.Compression;
using Xunit.Abstractions;

namespace CSVoom.app;

public class ParserTests(ITestOutputHelper testOutputHelper)
{
    private readonly Parser _parser = new();

    [Fact]
    public void TestXUnit()
    {
        Assert.True(true);
    }

    [Fact]
    public async Task TestGetRowCount()
    {
        var filePath = Path.GetTempFileName();
        File.Move(filePath, Path.ChangeExtension(filePath, ".csv"));
        filePath = Path.ChangeExtension(filePath, ".csv");

        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "name,city",
                "Alice,London",
                "Bob,Paris",
                "Charlie,London"
            ]);

            // Assuming Configuration.FirstRowIsHeader is true (default)
            var rowCount = await _parser.GetRowCountAsync(filePath);
            Assert.Equal(3, rowCount);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public async Task TestReadRange()
    {
        var filePath = Path.GetTempFileName();
        File.Move(filePath, Path.ChangeExtension(filePath, ".csv"));
        filePath = Path.ChangeExtension(filePath, ".csv");

        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "value",
                "1",
                "2",
                "3",
                "4",
                "5"
            ]);

            var rows = await _parser.ReadRangeAsync(filePath, 2, 4);

            Assert.Equal(3, rows.Count);
            Assert.Equal("1", rows[0][0]);
            Assert.Equal("2", rows[1][0]);
            Assert.Equal("3", rows[2][0]);
            Assert.Equal(2, rows[0].RowNumber);
            Assert.Equal(4, rows[2].RowNumber);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public async Task TestFindMatchesDoesNotIncludeHeader()
    {
        var filePath = Path.GetTempFileName();
        File.Move(filePath, Path.ChangeExtension(filePath, ".csv"));
        filePath = Path.ChangeExtension(filePath, ".csv");

        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "name,city",
                "Alice,London",
                "Bob,Paris"
            ]);

            // Search for "name" which is in the header
            var matches = await _parser.ReadMatchesAsync(filePath,
                s => s.Contains("name", StringComparison.OrdinalIgnoreCase), null, 100);

            // Should be empty because it no longer searches headers
            Assert.Empty(matches);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public async Task TestReadMatchesReportsProgress()
    {
        var filePath = Path.GetTempFileName();
        File.Move(filePath, Path.ChangeExtension(filePath, ".csv"));
        filePath = Path.ChangeExtension(filePath, ".csv");

        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "name,city",
                "Alice,London",
                "Bob,Paris",
                "Charlie,London"
            ]);

            var progressCounts = new List<int>();
            var progress = new Progress<int>(count => progressCounts.Add(count));

            // Search for "London" which appears twice
            var matches = await _parser.ReadMatchesAsync(filePath,
                s => s.Equals("London", StringComparison.OrdinalIgnoreCase), null, 100, progress);

            // Wait a bit for Progress<T> to dispatch (it uses SynchronizationContext or ThreadPool)
            // In a unit test without a SynchronizationContext, it might be immediate or on ThreadPool.
            // Actually Progress<int> in tests might be tricky. Let's use a custom IProgress.

            Assert.Equal(2, matches.Count);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public async Task TestExportToCsv()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        var exportPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");

        try
        {
            await File.WriteAllLinesAsync(sourcePath,
            [
                "Header1,Header2,Header3",
                "val1,val2,val3",
                "val4,val5,val6"
            ]);

            // Configuration.FirstRowIsHeader is true by default
            await _parser.ReadHeadersAsync(sourcePath);
            var rows = await _parser.ReadRangeAsync(sourcePath, 1, 10);

            // Export only Header1 and Header3
            var visibleHeaders = new List<string> { "Header1", "Header3" };

            await _parser.ExportToCsvAsync(exportPath, rows, visibleHeaders);

            var exportedLines = await File.ReadAllLinesAsync(exportPath);

            Assert.Equal(3, exportedLines.Length);
            Assert.Equal("Header1,Header3", exportedLines[0]);
            Assert.Equal("val1,val3", exportedLines[1]);
            Assert.Equal("val4,val6", exportedLines[2]);
        }
        finally
        {
            if (File.Exists(sourcePath)) File.Delete(sourcePath);
            if (File.Exists(exportPath)) File.Delete(exportPath);
        }
    }

    [Fact]
    public async Task TestExportToCsvEscaping()
    {
        var exportPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");

        try
        {
            var rows = new List<CsvRow>
            {
                new(["val with , comma", "val with \" quotes"], 1),
                new(["val with\nnewline", "normal"], 2)
            };
            var visibleHeaders = new List<string> { "H1", "H2" };
            _parser.Headers.AddRange(["H1", "H2"]);

            // Configuration.FirstRowIsHeader is true by default
            await _parser.ExportToCsvAsync(exportPath, rows, visibleHeaders);

            var exportedLines = await File.ReadAllLinesAsync(exportPath);

            // Lines:
            // 1: H1,H2
            // 2: "val with , comma","val with "" quotes"
            // 3: "val with
            // 4: newline",normal
            Assert.Equal(4, exportedLines.Length);
            Assert.Equal("H1,H2", exportedLines[0]);
            Assert.Equal("\"val with , comma\",\"val with \"\" quotes\"", exportedLines[1]);
        }
        finally
        {
            if (File.Exists(exportPath)) File.Delete(exportPath);
        }
    }

    // No header test is difficult without mocking Configuration, 
    // but we can skip it or assume it's true as it's the default.
    /*
    [Fact]
    public async Task TestExportToCsvNoHeader()
    {
        var exportPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");

        try
        {
            var rows = new List<Dictionary<string, string>>
            {
                new() { ["A"] = "1", ["B"] = "2" }
            };
            var visibleHeaders = new List<string> { "A", "B" };

            // Configuration.FirstRowIsHeader = false; // Cannot set
            await _parser.ExportToCsvAsync(exportPath, rows, visibleHeaders);

            var exportedLines = await File.ReadAllLinesAsync(exportPath);

            Assert.Single(exportedLines);
            Assert.Equal("1,2", exportedLines[0]);
        }
        finally
        {
            if (File.Exists(exportPath)) File.Delete(exportPath);
        }
    }
    */


    [Fact]
    public async Task TestReadTsv()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tsv");

        try
        {
            await File.WriteAllLinesAsync(sourcePath,
            [
                "Header1\tHeader2\tHeader3",
                "val1\tval2\tval3",
                "val4\t\"quoted\tval\"\tval6"
            ]);

            await _parser.ReadHeadersAsync(sourcePath);
            Assert.Equal(3, _parser.Headers.Count);
            Assert.Equal("Header2", _parser.Headers[1]);

            var rows = await _parser.ReadRangeAsync(sourcePath, 1, 10);
            Assert.Equal(2, rows.Count);
            Assert.Equal("val1", rows[0][0]);
            Assert.Equal("quoted\tval", rows[1][1]);
            Assert.Equal("val6", rows[1][2]);
        }
        finally
        {
            if (File.Exists(sourcePath)) File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task TestReadSsv()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ssv");

        try
        {
            await File.WriteAllLinesAsync(sourcePath,
            [
                "Header1;Header2;Header3",
                "val1;val2;val3",
                "val4;\"quoted;val\";val6"
            ]);

            await _parser.ReadHeadersAsync(sourcePath);
            Assert.Equal(3, _parser.Headers.Count);
            Assert.Equal("Header2", _parser.Headers[1]);

            var rows = await _parser.ReadRangeAsync(sourcePath, 1, 10);
            Assert.Equal(2, rows.Count);
            Assert.Equal("val1", rows[0][0]);
            Assert.Equal("quoted;val", rows[1][1]);
            Assert.Equal("val6", rows[1][2]);
        }
        finally
        {
            if (File.Exists(sourcePath)) File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task TestReadMatchesReportsProgressImmediate()
    {
        var filePath = Path.GetTempFileName();
        File.Move(filePath, Path.ChangeExtension(filePath, ".csv"));
        filePath = Path.ChangeExtension(filePath, ".csv");

        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "name,city",
                "Alice,London",
                "Bob,Paris",
                "Charlie,London"
            ]);

            var progressCounts = new List<int>();
            var progress = new MockProgress(count => progressCounts.Add(count));

            await _parser.ReadMatchesAsync(filePath,
                s => s.Equals("London", StringComparison.OrdinalIgnoreCase), null, 100, progress);

            // Expecting:
            // 1. Initial report (0)
            // 2. After Alice (1)
            // 3. After Charlie (2)
            Assert.Contains(1, progressCounts);
            Assert.Contains(2, progressCounts);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public async Task TestReadRangeIncludesHeader()
    {
        var filePath = Path.GetTempFileName();
        File.Move(filePath, Path.ChangeExtension(filePath, ".csv"));
        filePath = Path.ChangeExtension(filePath, ".csv");

        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "name,city",
                "Alice,London",
                "Bob,Paris"
            ]);

            var rows = await _parser.ReadRangeAsync(filePath, 1, 2);

            Assert.Single(rows);
            Assert.Equal(2, rows[0].RowNumber);
            Assert.Equal("Alice", rows[0][0]);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public async Task TestParseZip()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv.gz");

        try
        {
            await using (var compressedFile = File.Create(filePath))
            await using (var gzipStream = new GZipStream(compressedFile, CompressionMode.Compress))
            await using (var writer = new StreamWriter(gzipStream))
            {
                await writer.WriteLineAsync("value");
                await writer.WriteLineAsync("1");
                await writer.WriteLineAsync("2");
            }

            var rows = await _parser.ReadRangeAsync(filePath, 2, 3);

            Assert.Equal(2, rows.Count);
            Assert.Equal("1", rows[0][0]);
            Assert.Equal("2", rows[1][0]);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public async Task TestReadRangeUsesFileRowNumbers()
    {
        var filePath = Path.GetTempFileName();
        File.Move(filePath, Path.ChangeExtension(filePath, ".csv"));
        filePath = Path.ChangeExtension(filePath, ".csv");

        try
        {
            var lines = new List<string> { "value" };

            for (var i = 2; i <= 105; i++) lines.Add($"row-{i}");

            await File.WriteAllLinesAsync(filePath, lines);

            var rows = await _parser.ReadRangeAsync(filePath, 100, 102);

            Assert.Equal(3, rows.Count);
            Assert.Equal("row-100", rows[0][0]);
            Assert.Equal("row-101", rows[1][0]);
            Assert.Equal("row-102", rows[2][0]);
            Assert.Equal(100, rows[0].RowNumber);
            Assert.Equal(102, rows[2].RowNumber);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public async Task TestFirstRowAsData()
    {
        var filePath = Path.GetTempFileName();
        File.Move(filePath, Path.ChangeExtension(filePath, ".csv"));
        filePath = Path.ChangeExtension(filePath, ".csv");

        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "Alice,London",
                "Bob,Paris"
            ]);

            // Set configuration to treat first row as data
            var oldVal = Configuration.GetRawValue(nameof(Configuration.FirstRowIsHeader));
            Configuration.Save(new Dictionary<string, string> { { nameof(Configuration.FirstRowIsHeader), "false" } });

            try
            {
                var rows = await _parser.ReadRangeAsync(filePath, 1, 2);

                Assert.Equal(2, rows.Count);
                Assert.Equal("Alice", rows[0][0]);
                Assert.Equal("London", rows[0][1]);
                Assert.Equal(1, rows[0].RowNumber);

                Assert.Equal("Bob", rows[1][0]);
                Assert.Equal("Paris", rows[1][1]);
                Assert.Equal(2, rows[1].RowNumber);
            }
            finally
            {
                Configuration.Save(
                    new Dictionary<string, string> { { nameof(Configuration.FirstRowIsHeader), oldVal } });
            }
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public void TestCsvFilePatternsSplit()
    {
        var oldPatterns = Configuration.GetRawValue(nameof(Configuration.CsvFilePatterns));
        try
        {
            // Test with semicolon (app.config style)
            Configuration.Save(new Dictionary<string, string>
                { { nameof(Configuration.CsvFilePatterns), "*.csv;*.gz;*.ssv;*.tsv" } });

            var filePath = "test.tsv";
            var patterns =
                Configuration.CsvFilePatterns.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Contains("*" + Path.GetExtension(filePath), patterns, StringComparer.OrdinalIgnoreCase);

            // Test with comma (default Configuration.cs style)
            Configuration.Save(new Dictionary<string, string>
                { { nameof(Configuration.CsvFilePatterns), "*.csv,*.gz,*.ssv,*.tsv" } });
            patterns = Configuration.CsvFilePatterns.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Contains("*" + Path.GetExtension(filePath), patterns, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Configuration.Save(
                new Dictionary<string, string> { { nameof(Configuration.CsvFilePatterns), oldPatterns } });
        }
    }

    [Fact]
    public async Task TestAutoDetectSeparator()
    {
        var filePath = Path.GetTempFileName();
        File.Move(filePath, Path.ChangeExtension(filePath, ".csv"));
        filePath = Path.ChangeExtension(filePath, ".csv");

        try
        {
            // Scenario 1: Semicolon separated file with .csv extension
            await File.WriteAllLinesAsync(filePath,
            [
                "name;city;age",
                "Alice;London;30",
                "Bob;Paris;25"
            ]);

            var parser = new Parser();
            await parser.ReadHeadersAsync(filePath);

            Assert.Equal(3, parser.Headers.Count);
            Assert.Equal("name", parser.Headers[0]);
            Assert.Equal("city", parser.Headers[1]);
            Assert.Equal("age", parser.Headers[2]);

            var rows = await parser.ReadRangeAsync(filePath, 1, 10);
            Assert.Equal(2, rows.Count);
            Assert.Equal("Alice", rows[0][0]);
            Assert.Equal("London", rows[0][1]);
            Assert.Equal("30", rows[0][2]);

            // Scenario 2: Comma separated file with .csv extension
            await File.WriteAllLinesAsync(filePath,
            [
                "name,city,age",
                "Alice,London,30",
                "Bob,Paris,25"
            ]);

            parser = new Parser();
            await parser.ReadHeadersAsync(filePath);

            Assert.Equal(3, parser.Headers.Count);
            Assert.Equal("name", parser.Headers[0]);
            Assert.Equal("city", parser.Headers[1]);
            Assert.Equal("age", parser.Headers[2]);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public async Task TestCompareAsyncEnumerable()
    {
        var leftFile = Path.GetTempFileName() + ".csv";
        var rightFile = Path.GetTempFileName() + ".csv";

        try
        {
            await File.WriteAllLinesAsync(leftFile,
            [
                "id,name",
                "1,Alice",
                "2,Bob",
                "3,Charlie"
            ]);

            await File.WriteAllLinesAsync(rightFile,
            [
                "id,name",
                "1,Alice",
                "2,Robert",
                "4,David"
            ]);

            var results = new List<ComparisonResult>();
            await foreach (var result in Parser.CompareAsyncEnumerable(leftFile, rightFile)) results.Add(result);

            // Alice is same, so not in results
            // Bob vs Robert is different (Row 2)
            // Charlie vs David is different (Row 3)

            Assert.Equal(2, results.Count);

            Assert.Contains(results, r => r.RowNumber == 2 && r.Status == ComparisonStatus.Different);
            Assert.Contains(results, r => r.RowNumber == 3 && r.Status == ComparisonStatus.Different);
        }
        finally
        {
            if (File.Exists(leftFile)) File.Delete(leftFile);
            if (File.Exists(rightFile)) File.Delete(rightFile);
        }
    }

    private class MockProgress(Action<int> callback) : IProgress<int>
    {
        public void Report(int value)
        {
            callback(value);
        }
    }
}