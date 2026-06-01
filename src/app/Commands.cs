using System.Collections.Generic;
using System.Text;

namespace CSVoom.app;

public static class Commands
{
    public static string[] SplitCommand(string commandText)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < commandText.Length; i++)
        {
            var c = commandText[i];
            if (c == '\"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result.ToArray();
    }
}