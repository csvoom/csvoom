using CSVoom.app;

namespace CSVoom.test.app;

public class CommandParsingTests
{
    [Fact]
    public void TestSplitCommandWithQuotes()
    {
        var command = "find \"column name\" search_term";
        var parts = Commands.SplitCommand(command);

        Assert.Equal(3, parts.Length);
        Assert.Equal("find", parts[0]);
        Assert.Equal("column name", parts[1]);
        Assert.Equal("search_term", parts[2]);
    }

    [Fact]
    public void TestSplitCommandWithoutQuotes()
    {
        var command = "load 1 100";
        var parts = Commands.SplitCommand(command);

        Assert.Equal(3, parts.Length);
        Assert.Equal("load", parts[0]);
        Assert.Equal("1", parts[1]);
        Assert.Equal("100", parts[2]);
    }

    [Fact]
    public void TestSplitCommandEmpty()
    {
        var command = "";
        var parts = Commands.SplitCommand(command);
        Assert.Empty(parts);
    }

    [Fact]
    public void TestSplitCommandMultipleSpaces()
    {
        var command = "find  \"col\"   term";
        var parts = Commands.SplitCommand(command);
        Assert.Equal(3, parts.Length);
        Assert.Equal("find", parts[0]);
        Assert.Equal("col", parts[1]);
        Assert.Equal("term", parts[2]);
    }
}