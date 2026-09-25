namespace UserSecretManager.Core.Configuration;

/// <summary>
/// Helpers for flattened configuration keys (<c>Section:Sub:Key</c>) as used by Microsoft.Extensions.Configuration.
/// </summary>
public static class ConfigKey
{
    /// <summary>The section delimiter.</summary>
    public const string Delimiter = ":";

    /// <summary>Configuration keys are case-insensitive.</summary>
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>Joins a parent path and a child segment.</summary>
    public static string Combine(string parent, string segment) =>
        parent.Length == 0 ? segment : string.Concat(parent, Delimiter, segment);

    /// <summary>Splits a key into its segments.</summary>
    public static string[] Split(string key) => key.Split(Delimiter);

    /// <summary>The last segment of a key.</summary>
    public static string LastSegment(string key)
    {
        var index = key.LastIndexOf(Delimiter, StringComparison.Ordinal);
        return index < 0 ? key : key[(index + 1)..];
    }

    /// <summary>
    /// The environment variable name that overrides this key (<c>ConnectionStrings__Default</c>).
    /// </summary>
    public static string ToEnvironmentVariableName(string key) =>
        key.Replace(Delimiter, "__", StringComparison.Ordinal);
}
