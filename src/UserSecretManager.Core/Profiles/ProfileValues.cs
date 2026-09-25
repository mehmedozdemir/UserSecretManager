using UserSecretManager.Core.Configuration;
using UserSecretManager.Core.Effective;
using UserSecretManager.Core.Migration;
using UserSecretManager.Core.Secrets;

namespace UserSecretManager.Core.Profiles;

/// <summary>How a profile is applied to <c>secrets.json</c>.</summary>
public enum ProfileApplyMode
{
    /// <summary><c>secrets.json</c> becomes exactly the profile.</summary>
    Replace,

    /// <summary>Profile values are written over the current secrets; other secrets stay.</summary>
    Merge,
}

/// <summary>Values taken from an environment for a new profile.</summary>
/// <param name="Values">Keys that have a value in the environment.</param>
/// <param name="MissingKeys">Requested keys without a value in the environment.</param>
public sealed record EnvironmentValues(IReadOnlyList<KeyValuePair<string, string>> Values, IReadOnlyList<string> MissingKeys);

/// <summary>Builds profile contents and the secrets that result from applying one.</summary>
public static class ProfileValues
{
    /// <summary>
    /// Reads <paramref name="keys"/> as the application would see them in <paramref name="environment"/> without user
    /// secrets, e.g. to create a "Staging" profile from <c>appsettings.Staging.json</c>.
    /// </summary>
    public static EnvironmentValues FromEnvironment(ProjectConfiguration configuration, string environment, IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);
        ArgumentNullException.ThrowIfNull(keys);

        var effective = EffectiveConfigurationBuilder
            .Build(configuration, new EffectiveConfigurationOptions(environment, IncludeUserSecrets: false))
            .ToDictionary(e => e.Key, e => e.Value, ConfigKey.Comparer);

        var values = new List<KeyValuePair<string, string>>();
        var missing = new List<string>();
        foreach (var key in keys.Distinct(ConfigKey.Comparer))
        {
            if (effective.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
            {
                values.Add(new(key, value));
            }
            else
            {
                missing.Add(key);
            }
        }

        return new EnvironmentValues(values, missing);
    }

    /// <summary>The secrets that result from applying <paramref name="profile"/> with <paramref name="mode"/>.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Apply(SecretCollection current,
        IReadOnlyList<KeyValuePair<string, string>> profile, ProfileApplyMode mode)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(profile);
        if (mode == ProfileApplyMode.Replace)
        {
            return profile;
        }

        var merged = current.Clone();
        foreach (var pair in profile)
        {
            merged[pair.Key] = pair.Value;
        }

        return merged.ToList();
    }
}
