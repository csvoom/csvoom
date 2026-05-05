
using System.IO.Compression;
using CSVoom.Parser;
public class UnitTest1
{
    CSVParser parser = new CSVParser();

    [Fact]
    public void Test_XUnit()
    { // Make sure XUnit is working.
        Assert.True(true);
    }
    [Fact]
    public void Test_ParseCSV_100()
    { // Test that the parser can read a predefined file and write to the queue.
        parser.queue.Clear();
        if (!File.Exists("test100.csv"))
        { // Generates a test file if one does not yet exist.
            StreamWriter testFile = new StreamWriter("test100.csv");
            for (int i = 0; i < 10; i++)
            {
                testFile.WriteLine("1,2,3,4,5,6,7,8,9,10");
            }
            testFile.Close();
        }
        parser.ParseCSV("test100.csv");
        Assert.NotEmpty(parser.queue);
    }
    [Fact]
    public void Test_ParseCSV_0()
    { // Test that the parser can read a predefined file and not cause an error if no data is present.
        parser.queue.Clear();
        if (!File.Exists("test0.csv"))
        { // Generates a test file if one does not yet exist.
            StreamWriter testFile = new StreamWriter("test0.csv");
            testFile.Close();
        }
        parser.ParseCSV("test0.csv");
        Assert.Empty(parser.queue);
    }
    [Fact]
    public void Test_ParseCSV_ZIP()
    { // Test that the parser can read a predefined file and not cause an error if no data is present.
        parser.queue.Clear();
        if (!File.Exists("testZIP.csv.gz"))
        { // Generates a test file if one does not yet exist.
            GZipStream testFile = new GZipStream(File.Create("testZIP.csv"), CompressionMode.Compress);
            testFile.Close();
        }
        parser.ParseCSV("testZIP.csv.gz");
        Assert.Empty(parser.queue); // Technically this does not test that it works, but if no error is passed, then it is likely that the file was read successfully.
    }
}

