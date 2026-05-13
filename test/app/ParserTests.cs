using System.IO.Compression;
using Xunit.Abstractions;

namespace CSVoom.app;

public class ParserTests(ITestOutputHelper testOutputHelper)
{
    private readonly ITestOutputHelper _testOutputHelper = testOutputHelper;
    private readonly Parser _parser = new Parser();

    [Fact]
    public void TestXUnit()
    { // Make sure XUnit is working.
        Assert.True(true);
    }
    [Fact]
    public async Task TestParse100()
    { // Test that the parser can read a predefined file and write to the queue.
        await using var testFile = new StreamWriter("test100.csv");
        for (int i = 0; i < 10; i++)
        {
            await testFile.WriteLineAsync("1");
        }

        await _parser.ReadBatchAsync("test100.csv");
        Assert.NotEmpty(_parser.Rows);
    }
    [Fact]
    public async Task TestParse0()
    { // Test that the parser can read a predefined file and not cause an error if no data is present.
        await using var testFile = new StreamWriter("test0.csv");

        var test = await _parser.ReadBatchAsync("test0.csv");
        Assert.NotNull(test);
    }
    [Fact]
    public async Task TestParseZip()
    { // Test that the parser can read a predefined gzip-compressed CSV file.
        await using var compressedFile = File.Create("testZIP.csv.gz");
        await using var gzipStream = new GZipStream(compressedFile, CompressionMode.Compress);
        await using var writer = new StreamWriter(gzipStream);
        await writer.WriteLineAsync("value");
        await writer.WriteLineAsync("1");

        await _parser.ReadBatchAsync("testZIP.csv.gz");
        Assert.NotEmpty(_parser.Rows);
    }
}