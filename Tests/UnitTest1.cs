
using CSVoom.Parser;
public class UnitTest1
{
    string filePath = "C:\\Users\\Gilas\\Downloads\\4G_DIS9_mr_20230726-200001_20230726-214508_import.csv.gz";
    CSVParser parser = new CSVParser();

    [Fact]
    public void Test_XUnit()
    { // Make sure XUnit is working.
        Assert.True(true);
    }
    [Fact]
    public void Test_ParseCSV()
    { // Test that the parser can read a predefined file and write to the queue.
        parser.ParseCSV(filePath);
        Assert.NotEmpty(parser.queue);
    }
}

