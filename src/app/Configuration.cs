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
    ///     Gets the maximum number of rows to load.
    /// </summary>
    public static int MaxItems => GetInt(nameof(MaxItems), 10000, 1);

    /// <summary>
    ///     Gets the maximum number of matches to find automatically.
    /// </summary>
    public static int MaxFind => GetInt(nameof(MaxFind), 100, 1);

    /// <summary>
    ///     Gets the timeout for regular expression operations.
    /// </summary>
    public static int RegexTimeout => GetInt(nameof(RegexTimeout), 250, 1);

    /// <summary>
    ///     Gets the maximum number of differences to find when comparing files.
    /// </summary>
    public static int MaxCompare => GetInt(nameof(MaxCompare), 10000, 1);

    /// <summary>
    ///     Gets a value indicating whether a search is case-insensitive by default.
    /// </summary>
    public static bool CaseInsensitiveSearch => GetBool(nameof(CaseInsensitiveSearch), true);

    /// <summary>
    ///     Gets a value indicating whether regex search is enabled by default.
    /// </summary>
    public static bool RegexSearch => GetBool(nameof(RegexSearch), true);

    /// <summary>
    ///     Gets a value indicating whether the first row is treated as a header.
    /// </summary>
    public static bool FirstRowIsHeader => GetBool(nameof(FirstRowIsHeader), true);

    private const string DefaultCsvFilePatterns = "*.csv;*.gz;*.ssv;*.tsv";

    /// <summary>
    ///     Gets the theme variant (e.g., "Light" or "Dark").
    /// </summary>
    public static string Theme => GetString(nameof(Theme), "Dark");

    /// <summary>
    ///     Gets the list of available configuration settings.
    /// </summary>
    public static IReadOnlyList<ConfigurationSetting> Settings { get; } =
    [
        new(nameof(MaxItems), "Integer", "1000", "Max amount of rows to be loaded in at once"),
        new(nameof(MaxFind), "Integer", "1000", "Max amount of rows to find until automatic cancellation"),
        new(nameof(MaxCompare), "Integer", "1000", "Max amount of differences until automatic cancellation"),
        new(nameof(RegexTimeout), "Integer", "250", "Timeout for parsing regex"),
        new(nameof(CaseInsensitiveSearch), "Boolean", "true", "Whether to perform case-insensitive search."),
        new(nameof(RegexSearch), "Boolean", "true", "Whether to seek regex out of input."),
        new(nameof(FirstRowIsHeader), "Boolean", "true", "Whether to combine the first row into header."),
        new(nameof(Theme), "Select", "Dark", "Theme variant: Light or Dark.", ["Light", "Dark"])
    ];

    /// <summary>
    ///     Gets the CSV file patterns as an array of strings.
    /// </summary>
    /// <returns>An array of file patterns.</returns>
    public static string[] GetCsvFilePatterns() =>
        DefaultCsvFilePatterns.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    ///     Gets the raw configuration value for the specified key.
    /// </summary>
    /// <param name="key">The configuration key.</param>
    /// <returns>The raw value or the default value if the key is not found.</returns>
    public static string GetRawValue(string key) =>
        ConfigurationManager.AppSettings[key] ?? Settings.First(s => s.Key == key).DefaultValue;

    /// <summary>
    ///     Saves the specified configuration values.
    /// </summary>
    /// <param name="values">A dictionary of key-value pairs to save.</param>
    public static void Save(IDictionary<string, string> values)
    {
        var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
        var settings = config.AppSettings.Settings;

        foreach (var (key, value) in values)
        {
            if (settings[key] is null) settings.Add(key, value);
            else settings[key].Value = value;
        }

        config.Save(ConfigurationSaveMode.Modified);
        ConfigurationManager.RefreshSection("appSettings");
    }

    private static int GetInt(string key, int defaultValue, int? minValue = null, int? maxValue = null)
    {
        var value = ConfigurationManager.AppSettings[key];
        if (string.IsNullOrWhiteSpace(value) || !int.TryParse(value, out var parsedValue) || parsedValue < minValue || parsedValue > maxValue) return defaultValue;
        return parsedValue;

    }

    private static bool GetBool(string key, bool defaultValue) =>
        bool.TryParse(ConfigurationManager.AppSettings[key], out var parsedValue) ? parsedValue : defaultValue;

    private static string GetString(string key, string defaultValue) =>
        ConfigurationManager.AppSettings[key] ?? defaultValue;
}

/// <summary>
///     Represents a single configuration setting.
/// </summary>
public sealed record ConfigurationSetting(string Key, string Type, string DefaultValue, string Description, string[]? Options = null);