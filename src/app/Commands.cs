using System.Collections.Generic;
using System.Text;

namespace CSVoom.app;

/// <summary>
/// Provides methods for parsing and splitting commands.
/// </summary>
public static class Commands
{
    /// <summary>
    /// Splits a command text into individual arguments, respecting double quotes.
    /// </summary>
    /// <param name="commandText">The command text to split.</param>
    /// <returns>An array of command arguments.</returns>
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
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                    break;
                default:
                    current.Append(c);
                    break;
            }
        }

        if (current.Length > 0) result.Add(current.ToString());

        return [.. result];
    }
}