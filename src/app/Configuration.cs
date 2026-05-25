using System.Configuration;

namespace CSVoom.app;

public static class Configuration
{
    // Integers
    public static int AutoVisibleRows => GetInt(nameof(AutoVisibleRows), 10_000, minValue: 1);
    
    public static int RegexTimeoutMilliseconds => GetInt(nameof(RegexTimeoutMilliseconds), 250, minValue: 1);
    
    public static int MaxCommandHistoryItems => GetInt(nameof(MaxCommandHistoryItems), 50, minValue: 0);
    
    // Booleans
    
    public static bool CaseInsensitiveSearch => GetBool(nameof(CaseInsensitiveSearch), true);
    
    public static bool RegexSearch => GetBool(nameof(RegexSearch), true);
    
    public static bool ShowCommandExamples => GetBool(nameof(ShowCommandExamples), true);
    
    // Strings
    public static string CsvFilePatterns => GetString(nameof(CsvFilePatterns), "*.csv;*.gz");
    
    // Resolvers
    private static int GetInt(string key, int defaultValue, int? minValue = null, int? maxValue = null)
    {
        var value = ConfigurationManager.AppSettings[key];

        if (!int.TryParse(value, out var parsedValue) || minValue.HasValue && parsedValue < minValue.Value || maxValue.HasValue && parsedValue > maxValue.Value)
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