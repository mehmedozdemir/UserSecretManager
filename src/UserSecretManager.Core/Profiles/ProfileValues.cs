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

/// <summary>What applying a set of values does to one secret.</summary>
public enum SecretChangeKind
{
    /// <summary>Same key and value already in secrets.json.</summary>
    Unchanged,

    /// <summary>The value will change.</summary>
    Changed,

    /// <summary>The key will be added.</summary>
    Added,

    /// <summary>The key will be deleted (replace mode).</summary>
    Removed,

    /// <summary>The key is not in the applied values but stays (merge mode).</summary>
    Kept,
}

/// <summary>Effect of applying values on one secret.</summary>
/// <param name="Key">Secret key.</param>
/// <param name="Kind">What happens.</param>
/// <param name="CurrentValue">Value in secrets.json now, if any.</param>
/// <param name="NewValue">Value after applying, if the key remains.</param>
public sealed record SecretChange(string Key, SecretChangeKind Kind, string? CurrentValue, string? NewValue);

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

    /// <summary>
    /// Per-key effect of applying <paramref name="values"/>: the applied keys in their order, then the current keys they
    /// do not contain.
    /// </summary>
    public static IReadOnlyList<SecretChange> Compare(SecretCollection current,
        IReadOnlyList<KeyValuePair<string, string>> values, ProfileApplyMode mode)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(values);

        var changes = new List<SecretChange>();
        var applied = new HashSet<string>(ConfigKey.Comparer);
        foreach (var (key, value) in values)
        {
            if (!applied.Add(key))
            {
                continue;
            }

            var kind = !current.TryGetValue(key, out var existing) ? SecretChangeKind.Added
                : string.Equals(existing, value, StringComparison.Ordinal) ? SecretChangeKind.Unchanged
                : SecretChangeKind.Changed;
            changes.Add(new SecretChange(key, kind, kind == SecretChangeKind.Added ? null : existing, value));
        }

        foreach (var (key, value) in current.Where(p => !applied.Contains(p.Key)))
        {
            changes.Add(mode == ProfileApplyMode.Replace
                ? new SecretChange(key, SecretChangeKind.Removed, value, null)
                : new SecretChange(key, SecretChangeKind.Kept, value, value));
        }

        return changes;
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
