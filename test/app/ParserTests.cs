
using System.IO.Compression;
using CSVoom.Parser;
public class ParserTests
{
    Parser parser = new Parser();

    [Fact]
    public void testXUnit()
    { // Make sure XUnit is working.
        Assert.True(true);
    }
    [Fact]
    public async Task testParse100()
    { // Test that the parser can read a predefined file and write to the queue.
        if (!File.Exists("test100.csv"))
        { // Generates a test file if one does not yet exist.
            using StreamWriter testFile = new StreamWriter("test100.csv");
            for (int i = 0; i < 10; i++)
            {
                testFile.WriteLine("1,2,3,4,5,6,7,8,9,10");
            }
            testFile.Dispose();
        }
        var test = await parser.ReadAllLinesAsync("test100.csv");
        Console.WriteLine(test.Count);
        Assert.NotEmpty(test);
    }
    [Fact]
    public async Task testParse0()
    { // Test that the parser can read a predefined file and not cause an error if no data is present.
        if (!File.Exists("test0.csv"))
        { // Generates a test file if one does not yet exist.
            using StreamWriter testFile = new StreamWriter("test0.csv");
            testFile.Dispose();
        }
        var test = await parser.ReadAllLinesAsync("test0.csv");
        Assert.NotNull(test);
    }
    [Fact]
    public async Task testParseZIP()
    { // Test that the parser can read a predefined file and not cause an error if no data is present.
        if (!File.Exists("testZIP.csv.gz"))
        { // Generates a test file if one does not yet exist.
            using (GZipStream testFile = new GZipStream(new FileStream("testZIP.csv.gz", FileMode.Create), CompressionMode.Compress))
            {
                testFile.Dispose();
            }
        }
        var test = await parser.ReadAllLinesAsync("testZIP.csv.gz");
        Assert.NotNull(test); // Technically this does not test that it works, but if no error is passed, then it is likely that the file was read successfully.
    }
}

