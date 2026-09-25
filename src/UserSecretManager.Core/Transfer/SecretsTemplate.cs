using UserSecretManager.Core.Configuration;
using UserSecretManager.Core.Secrets;

namespace UserSecretManager.Core.Transfer;

/// <summary>
/// <c>secrets.template.json</c>: the secret keys a project needs, with empty values, meant to be committed so a new
/// developer knows which secrets to set.
/// </summary>
public static class SecretsTemplate
{
    /// <summary>File name, placed next to the project file.</summary>
    public const string FileName = "secrets.template.json";

    /// <summary>Template path for a project directory.</summary>
    public static string PathFor(string projectDirectory) => Path.Combine(projectDirectory, FileName);

    /// <summary>Template content for <paramref name="keys"/>; values are always empty.</summary>
    public static string Create(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return UserSecretsStore.Serialize(keys
            .Distinct(ConfigKey.Comparer)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(k => new KeyValuePair<string, string>(k, string.Empty)));
    }

    /// <summary>Keys listed in a template (flat or nested JSON).</summary>
    public static IReadOnlyList<string> ReadKeys(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return JsonConfigDocument.Parse(text).Values.Select(v => v.Key).Distinct(ConfigKey.Comparer).ToList();
    }
}
