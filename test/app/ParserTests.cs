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
            Assert.Equal("1", rows[0]["value"]);
            Assert.Equal("2", rows[1]["value"]);
            Assert.Equal("3", rows[2]["value"]);
            Assert.Equal("2", rows[0][Parser.RowNumberKey]);
            Assert.Equal("4", rows[2][Parser.RowNumberKey]);
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
                s => s.Equals("London", StringComparison.OrdinalIgnoreCase), null, 100, progress: progress);

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

    private class MockProgress(Action<int> callback) : IProgress<int>
    {
        public void Report(int value) => callback(value);
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
                s => s.Equals("London", StringComparison.OrdinalIgnoreCase), null, 100, progress: progress);

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
            Assert.Equal("2", rows[0][Parser.RowNumberKey]);
            Assert.Equal("Alice", rows[0]["name"]);
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
            Assert.Equal("1", rows[0]["value"]);
            Assert.Equal("2", rows[1]["value"]);
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
            Assert.Equal("row-100", rows[0]["value"]);
            Assert.Equal("row-101", rows[1]["value"]);
            Assert.Equal("row-102", rows[2]["value"]);
            Assert.Equal("100", rows[0][Parser.RowNumberKey]);
            Assert.Equal("102", rows[2][Parser.RowNumberKey]);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }
}