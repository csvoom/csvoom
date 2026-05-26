using System.Collections.Generic;
using System.Configuration;
using System.Linq;

namespace CSVoom.app;

public static class Configuration
{
    // Integers
    public static int AutoLoadRows =>
        GetInt(nameof(AutoLoadRows), 10_000, 1); // When no value is specified, default to 10,000 for "Load"

    public static int AutoFindRows =>
        GetInt(nameof(AutoFindRows), 100, 1); // When no value is specified, default to 100 for "Find"

    public static int RegexTimeoutMilliseconds =>
        GetInt(nameof(RegexTimeoutMilliseconds), 250, 1); // Timeout for resolving regex patterns

    public static int MaxCommandHistoryItems =>
        GetInt(nameof(MaxCommandHistoryItems), 50, 0); // Maximum number of command history items to keep

    // Booleans

    public static bool CaseInsensitiveSearch =>
        GetBool(nameof(CaseInsensitiveSearch), true); // Whether to perform case-insensitive search

    public static bool RegexSearch => GetBool(nameof(RegexSearch), true); // Whether to seek regex out of command input

    public static bool ShowCommandExamples =>
        GetBool(nameof(ShowCommandExamples), true); // Whether to show examples for command usage

    public static bool CreateColumnHeaders =>
        GetBool(nameof(CreateColumnHeaders), true); // Whether to move the first row into column headers

    public static bool CreateRowNumbers =>
        GetBool(nameof(CreateRowNumbers), true); // Whether to add an automatic numbers column

    // Strings
    public static string CsvFilePatterns =>
        GetString(nameof(CsvFilePatterns), "*.csv;*.gz"); // File patterns to match CSV files

    public static IReadOnlyList<ConfigurationSetting> Settings { get; } =
    [
        new(nameof(AutoLoadRows), "Integer", "10,000",
            "Minimum: 1. When no value is specified, default to 10,000 for \"Load\"."),
        new(nameof(AutoFindRows), "Integer", "100",
            "Minimum: 1. When no value is specified, default to 100 for \"Find\"."),
        new(nameof(RegexTimeoutMilliseconds), "Integer", "250", "Minimum: 1. Timeout for resolving regex patterns."),
        new(nameof(MaxCommandHistoryItems), "Integer", "50",
            "Minimum: 0. Maximum number of command history items to keep."),
        new(nameof(CaseInsensitiveSearch), "Boolean", "true", "Whether to perform case-insensitive search."),
        new(nameof(RegexSearch), "Boolean", "true", "Whether to seek regex out of command input."),
        new(nameof(ShowCommandExamples), "Boolean", "true", "Whether to show examples for command usage."),
        new(nameof(CreateColumnHeaders), "Boolean", "true", "Whether to move the first row into column headers."),
        new(nameof(CreateRowNumbers), "Boolean", "true", "Whether to add an automatic numbers column."),
        new(nameof(CsvFilePatterns), "String", "*.csv;*.gz", "File patterns to match CSV files.")
    ];

    public static string GetRawValue(string key)
    {
        return ConfigurationManager.AppSettings[key] ?? GetDefaultValue(key);
    }

    private static string GetDefaultValue(string key)
    {
        return Settings.First(setting => setting.Key == key).DefaultValue;
    }

    public static void Save(IDictionary<string, string> values)
    {
        var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

        foreach (var (key, value) in values)
            if (config.AppSettings.Settings[key] is null)
                config.AppSettings.Settings.Add(key, value);
            else
                config.AppSettings.Settings[key].Value = value;

        config.Save(ConfigurationSaveMode.Modified);
        ConfigurationManager.RefreshSection("appSettings");
    }

    // Resolvers
    private static int GetInt(string key, int defaultValue, int? minValue = null, int? maxValue = null)
    {
        var value = ConfigurationManager.AppSettings[key];

        if (!int.TryParse(value, out var parsedValue) || (minValue.HasValue && parsedValue < minValue.Value) ||
            (maxValue.HasValue && parsedValue > maxValue.Value))
            return defaultValue;

        return parsedValue;
    }

    private static bool GetBool(string key, bool defaultValue)
    {
        var value = ConfigurationManager.AppSettings[key];

        return bool.TryParse(value, out var parsedValue)
            ? parsedValue
            : defaultValue;
    }

    private static string GetString(string key, string defaultValue)
    {
        var value = ConfigurationManager.AppSettings[key];

        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value;
    }
}

public sealed record ConfigurationSetting(
    string Key,
    string Type,
    string DefaultValue,
    string Description);