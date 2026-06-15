using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;

namespace CSVoom.app;

/// <summary>
///     Provides application configuration settings.
/// </summary>
public static class Configuration
{
    /// <summary>
    ///     Gets the number of rows to load automatically.
    /// </summary>
    public static int AutoLoadRows => GetInt(nameof(AutoLoadRows), 10000, 1);

    /// <summary>
    ///     Gets the maximum number of matches to find.
    /// </summary>
    public static int AutoFindRows => GetInt(nameof(AutoFindRows), 100, 1);

    /// <summary>
    ///     Gets the timeout in milliseconds for regex operations.
    /// </summary>
    public static int RegexTimeoutMilliseconds => GetInt(nameof(RegexTimeoutMilliseconds), 250, 1);

    /// <summary>
    ///     Gets the maximum number of items to keep in the command history.
    /// </summary>
    public static int MaxCommandHistoryItems => GetInt(nameof(MaxCommandHistoryItems), 50, 0);

    /// <summary>
    ///     Gets the maximum number of differences to find before canceling.
    /// </summary>
    public static int CompareLimit => GetInt(nameof(CompareLimit), 10000, 1);

    /// <summary>
    ///     Gets a value indicating whether searches should be case-insensitive.
    /// </summary>
    public static bool CaseInsensitiveSearch => GetBool(nameof(CaseInsensitiveSearch), true);

    /// <summary>
    ///     Gets a value indicating whether regex search is enabled.
    /// </summary>
    public static bool RegexSearch => GetBool(nameof(RegexSearch), true);

    /// <summary>
    ///     Gets a value indicating whether command examples should be shown.
    /// </summary>
    public static bool ShowCommandExamples => GetBool(nameof(ShowCommandExamples), true);

    /// <summary>
    ///     Gets a value indicating whether the first row is treated as a header.
    /// </summary>
    public static bool FirstRowIsHeader => GetBool(nameof(FirstRowIsHeader), true);


    /// <summary>
    ///     Gets the file patterns to match CSV files.
    /// </summary>
    public static string CsvFilePatterns => GetString(nameof(CsvFilePatterns), "*.csv;*.gz;*.ssv;*.tsv");

    /// <summary>
    ///     Gets the theme variant.
    /// </summary>
    public static string Theme => GetString(nameof(Theme), "Dark");

    /// <summary>
    ///     Gets the list of available configuration settings.
    /// </summary>
    public static IReadOnlyList<ConfigurationSetting> Settings { get; } =
    [
        new(nameof(AutoLoadRows), "Integer", "10000",
            "Minimum: 1. When no value is specified, default to 10000 for \"Load\"."),
        new(nameof(AutoFindRows), "Integer", "100",
            "Minimum: 1. When no value is specified, default to 100 for \"Find\"."),
        new(nameof(RegexTimeoutMilliseconds), "Integer", "250", "Minimum: 1. Timeout for resolving regex patterns."),
        new(nameof(MaxCommandHistoryItems), "Integer", "50",
            "Minimum: 0. Maximum number of command history items to keep."),
        new(nameof(CompareLimit), "Integer", "10000",
            "Minimum: 1. Maximum differences to find before canceling."),
        new(nameof(CaseInsensitiveSearch), "Boolean", "true", "Whether to perform case-insensitive search."),
        new(nameof(RegexSearch), "Boolean", "true", "Whether to seek regex out of command input."),
        new(nameof(ShowCommandExamples), "Boolean", "true", "Whether to show examples for command usage."),
        new(nameof(FirstRowIsHeader), "Boolean", "true", "Whether to treat the first row as a header."),
        new(nameof(CsvFilePatterns), "String", "*.csv;*.gz;*.ssv;*.tsv", "File patterns to match CSV files."),
        new(nameof(Theme), "String", "Dark", "Theme variant: Light or Dark.")
    ];

    /// <summary>
    ///     Gets the file patterns to match CSV files as an array.
    /// </summary>
    public static string[] GetCsvFilePatterns()
    {
        return CsvFilePatterns.Split([';', ','],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    ///     Gets the raw configuration value for the specified key.
    /// </summary>
    public static string GetRawValue(string key)
    {
        return ConfigurationManager.AppSettings[key] ?? Settings.First(s => s.Key == key).DefaultValue;
    }

    /// <summary>
    ///     Saves the specified configuration values.
    /// </summary>
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

    /// <summary>
    ///     Resolves an integer configuration value.
    /// </summary>
    private static int GetInt(string key, int defaultValue, int? minValue = null, int? maxValue = null)
    {
        var value = ConfigurationManager.AppSettings[key];
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;

        value = value.Replace(",", "").Replace(" ", "");

        return int.TryParse(value, out var parsedValue) &&
               (minValue == null || parsedValue >= minValue) &&
               (maxValue == null || parsedValue <= maxValue)
            ? parsedValue
            : defaultValue;
    }

    private static bool GetBool(string key, bool defaultValue)
    {
        return bool.TryParse(ConfigurationManager.AppSettings[key], out var parsedValue) ? parsedValue : defaultValue;
    }

    private static string GetString(string key, string defaultValue)
    {
        var value = ConfigurationManager.AppSettings[key];
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }
}

/// <summary>
///     Represents a single configuration setting.
/// </summary>
public sealed record ConfigurationSetting(string Key, string Type, string DefaultValue, string Description);