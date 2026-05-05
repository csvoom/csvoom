
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
        if (!File.Exists("test0.csv"))
        { // Generates a test file if one does not yet exist.
            StreamWriter testFile = new StreamWriter("test0.csv");
            testFile.Close();
        }

        parser.ParseCSV("test100.csv");
        Assert.NotEmpty(parser.queue);
    }
}

