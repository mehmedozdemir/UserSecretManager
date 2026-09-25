using System.Collections;
using UserSecretManager.Core.Configuration;

namespace UserSecretManager.Core.Secrets;

/// <summary>
/// Ordered, case-insensitive set of user secrets loaded from a <c>secrets.json</c> file.
/// </summary>
public sealed class SecretCollection : IEnumerable<KeyValuePair<string, string>>
{
    private readonly OrderedDictionary<string, string> _values = new(ConfigKey.Comparer);

    /// <summary>Creates a collection for the given file.</summary>
    public SecretCollection(string filePath, bool exists, string? rawText)
    {
        FilePath = filePath;
        Exists = exists;
        RawText = rawText;
    }

    /// <summary>Path of the secrets file.</summary>
    public string FilePath { get; }

    /// <summary>Whether the file existed when loaded.</summary>
    public bool Exists { get; }

    /// <summary>File text as loaded, or <c>null</c> if the file did not exist.</summary>
    public string? RawText { get; }

    /// <summary>Number of secrets.</summary>
    public int Count => _values.Count;

    /// <summary>Keys in file order.</summary>
    public IEnumerable<string> Keys => _values.Keys;

    /// <summary>Gets or sets a secret; setting an existing key keeps its position.</summary>
    public string this[string key]
    {
        get => _values[key];
        set => _values[key] = value;
    }

    /// <summary>Whether the key exists.</summary>
    public bool ContainsKey(string key) => _values.ContainsKey(key);

    /// <summary>Tries to get a secret value.</summary>
    public bool TryGetValue(string key, out string value)
    {
        var found = _values.TryGetValue(key, out var existing);
        value = existing ?? string.Empty;
        return found;
    }

    /// <summary>Removes a secret.</summary>
    public bool Remove(string key) => _values.Remove(key);

    /// <summary>Creates an independent copy.</summary>
    public SecretCollection Clone()
    {
        var copy = new SecretCollection(FilePath, Exists, RawText);
        foreach (var pair in _values)
        {
            copy[pair.Key] = pair.Value;
        }

        return copy;
    }

    /// <summary>Serialized file content for the current values.</summary>
    public string ToJson() => UserSecretsStore.Serialize(_values);

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
