using System;
using System.Collections.Generic;

namespace CSVoom.app;

/// <summary>
///     Provides methods for parsing and splitting commands.
/// </summary>
public static class Commands
{
    /// <summary>
    ///     Splits a command text into individual arguments, respecting double quotes.
    /// </summary>
    /// <param name="commandText">The command text to split.</param>
    /// <returns>An array of command arguments.</returns>
    public static string[] SplitCommand(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText)) return [];

        var result = new List<string>();
        var span = commandText.AsSpan();
        var inQuotes = false;
        var start = -1;

        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            switch (c)
            {
                case '\"' when inQuotes:
                    result.Add(span[start..i].ToString());
                    start = -1;
                    inQuotes = false;
                    break;
                case '\"':
                    inQuotes = true;
                    start = i + 1;
                    break;
                case ' ' when !inQuotes:
                {
                    if (start != -1)
                    {
                        result.Add(span[start..i].ToString());
                        start = -1;
                    }

                    break;
                }
                default:
                {
                    if (start == -1)
                    {
                        start = i;
                    }

                    break;
                }
            }
        }

        if (start != -1)
        {
            result.Add(span[start..].ToString());
        }

        return [.. result];
    }
}