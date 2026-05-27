using System.IO.Compression;
using Xunit.Abstractions;

namespace CSVoom.app;

public class ParserTests(ITestOutputHelper testOutputHelper)
{
    private readonly Parser _parser = new();
    private readonly ITestOutputHelper _testOutputHelper = testOutputHelper;

    [Fact]
    public void TestXUnit()
    {
        Assert.True(true);
    }

    [Fact]
    public async Task TestReadHeaders()
    {
        var filePath = Path.GetTempFileName();
        File.Move(filePath, Path.ChangeExtension(filePath, ".csv"));
        filePath = Path.ChangeExtension(filePath, ".csv");

        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "name,age,city",
                "Alice,30,London"
            ]);

            var headers = await _parser.ReadHeadersAsync(filePath);

            Assert.Equal(["name", "age", "city"], headers);
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
    public async Task TestReadEmptyFile()
    {
        var filePath = Path.GetTempFileName();
        File.Move(filePath, Path.ChangeExtension(filePath, ".csv"));
        filePath = Path.ChangeExtension(filePath, ".csv");

        try
        {
            await File.WriteAllTextAsync(filePath, string.Empty);

            var headers = await _parser.ReadHeadersAsync(filePath);
            var rows = await _parser.ReadRangeAsync(filePath, 1, 10);

            Assert.Empty(headers);
            Assert.Empty(rows);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public async Task TestFindFirstInHeader()
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
            var match = await _parser.FindFirstAsync(filePath, "name");

            Assert.NotNull(match);
            Assert.Equal(1, match.Value.RowNumber);
            Assert.Equal("name", match.Value.Header);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public async Task TestFindFirstInHeaderWithSpecificColumn()
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

            // Search for "name" in column "name" - should find it in the header
            var match = await _parser.FindFirstAsync(filePath, "name", "name");

            Assert.NotNull(match);
            Assert.Equal(1, match.Value.RowNumber);
            Assert.Equal("name", match.Value.Header);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public async Task TestFindMatchesInHeader()
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
            var matches = await _parser.FindMatchesAsync(filePath,
                s => s.Contains("name", StringComparison.OrdinalIgnoreCase), null, 100);

            Assert.NotEmpty(matches);
            var headerMatch = matches.FirstOrDefault(m => m.RowNumber == 1);
            Assert.NotEqual(default, headerMatch);
            Assert.Equal("name", headerMatch.Header);
            Assert.Equal("name", headerMatch.Value);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public async Task TestReadMatchingRows()
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

            var rows = await _parser.ReadMatchingRowsAsync(
                filePath,
                row => row.TryGetValue("city", out var city)
                       && city.Equals("London", StringComparison.OrdinalIgnoreCase),
                100,
                CancellationToken.None);

            Assert.Equal(2, rows.Count);
            Assert.Equal("Alice", rows[0]["name"]);
            Assert.Equal("Charlie", rows[1]["name"]);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public async Task TestReadMatchingRowsIncludesHeader()
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

            var rows = await _parser.ReadMatchingRowsAsync(
                filePath,
                row => row.TryGetValue("name", out var name)
                       && name.Equals("name", StringComparison.OrdinalIgnoreCase),
                100,
                CancellationToken.None);

            Assert.Empty(rows);
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
    public async Task TestFindFirst()
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
                "Charlie,Berlin"
            ]);

            var match = await _parser.FindFirstAsync(filePath, "paris");

            Assert.NotNull(match);
            Assert.Equal(3, match.Value.RowNumber);
            Assert.Equal("city", match.Value.Header);
            Assert.Equal("Bob", match.Value.Row["name"]);
            Assert.Equal("Paris", match.Value.Row["city"]);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public async Task TestFindFirstSearchesOnlyRequestedColumn()
    {
        var filePath = Path.GetTempFileName();
        File.Move(filePath, Path.ChangeExtension(filePath, ".csv"));
        filePath = Path.ChangeExtension(filePath, ".csv");

        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "name,city",
                "Paris,London",
                "Bob,Paris"
            ]);

            var match = await _parser.FindFirstAsync(filePath, "paris", "city");

            Assert.NotNull(match);
            Assert.Equal(3, match.Value.RowNumber);
            Assert.Equal("city", match.Value.Header);
            Assert.Equal("Bob", match.Value.Row["name"]);
            Assert.Equal("Paris", match.Value.Row["city"]);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public async Task TestFindFirstReturnsNullWhenRequestedColumnDoesNotExist()
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

            var match = await _parser.FindFirstAsync(filePath, "paris", "country");

            Assert.Null(match);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public async Task TestFindFirstReturnsNullWhenNoMatchExists()
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

            var match = await _parser.FindFirstAsync(filePath, "Berlin");

            Assert.Null(match);
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