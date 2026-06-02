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

        foreach (var c in commandText)
        {
            switch (c)
            {
                case '\"':
                    inQuotes = !inQuotes;
                    break;
                case ' ' when !inQuotes:
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }

                    break;
                }
                default:
                    current.Append(c);
                    break;
            }
        }

        if (current.Length > 0) result.Add(current.ToString());

        return result.ToArray();
    }
}