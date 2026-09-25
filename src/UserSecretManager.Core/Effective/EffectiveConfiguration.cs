using UserSecretManager.Core.Analysis;
using UserSecretManager.Core.Configuration;
using UserSecretManager.Core.Discovery;
using UserSecretManager.Core.Migration;

namespace UserSecretManager.Core.Effective;

/// <summary>Kind of configuration layer.</summary>
public enum ConfigLayerKind
{
    /// <summary><c>appsettings.json</c>.</summary>
    Base,

    /// <summary><c>appsettings.{Environment}.json</c>.</summary>
    EnvironmentFile,

    /// <summary>User secrets.</summary>
    UserSecrets,

    /// <summary>Environment variables of a launch profile.</summary>
    LaunchProfile,

    /// <summary>An extra JSON file added by the user.</summary>
    CustomFile,
}

/// <summary>What to simulate.</summary>
/// <param name="Environment">Hosting environment name.</param>
/// <param name="IncludeUserSecrets">Whether user secrets are loaded (by default only in Development).</param>
/// <param name="LaunchProfile">Launch profile whose environment variables are applied, if any.</param>
public sealed record EffectiveConfigurationOptions(string Environment, bool IncludeUserSecrets, LaunchProfile? LaunchProfile = null);

/// <summary>A value a layer provides for a key.</summary>
/// <param name="Source">Layer label.</param>
/// <param name="Kind">Layer kind.</param>
/// <param name="Value">The value.</param>
public sealed record ConfigLayerValue(string Source, ConfigLayerKind Kind, string? Value);

/// <summary>The final value of a key and how it was reached.</summary>
/// <param name="Key">Configuration key.</param>
/// <param name="Layers">Every layer that sets the key, in load order; the last one wins.</param>
/// <param name="Hint">Sensitivity suggestion.</param>
public sealed record EffectiveEntry(string Key, IReadOnlyList<ConfigLayerValue> Layers, SensitivityHint Hint)
{
    /// <summary>The winning layer.</summary>
    public ConfigLayerValue Winner => Layers[^1];

    /// <summary>The effective value.</summary>
    public string? Value => Winner.Value;

    /// <summary>Whether the effective value is empty (for example cleared after moving it to user secrets).</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>Whether a later layer overrides an earlier one.</summary>
    public bool IsOverridden => Layers.Count > 1;
}

/// <summary>
/// Computes the configuration an application would see for an environment, mirroring the default host order:
/// appsettings.json → appsettings.{Environment}.json → user secrets → environment variables. Extra JSON files are
/// assumed to be added after the defaults and therefore applied last.
/// </summary>
public static class EffectiveConfigurationBuilder
{
    /// <summary>Label of the user secrets layer.</summary>
    public const string UserSecretsSource = "User Secrets";

    /// <summary>Builds the effective configuration.</summary>
    public static IReadOnlyList<EffectiveEntry> Build(ProjectConfiguration configuration, EffectiveConfigurationOptions options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        var layers = new OrderedDictionary<string, List<ConfigLayerValue>>(ConfigKey.Comparer);
        foreach (var (source, kind, values) in EnumerateLayers(configuration, options))
        {
            foreach (var (key, value) in values)
            {
                if (!layers.TryGetValue(key, out var list))
                {
                    list = [];
                    layers[key] = list;
                }

                list.Add(new ConfigLayerValue(source, kind, value));
            }
        }

        return layers
            .Select(pair => new EffectiveEntry(pair.Key, pair.Value,
                SensitivityAnalyzer.Analyze(pair.Key, pair.Value.Select(l => l.Value))))
            .ToList();
    }

    private static IEnumerable<(string Source, ConfigLayerKind Kind, IEnumerable<(string, string?)> Values)> EnumerateLayers(
        ProjectConfiguration configuration, EffectiveConfigurationOptions options)
    {
        var files = configuration.ValidFiles.ToList();
        foreach (var file in files.Where(f => f.IsBase))
        {
            yield return (file.FileName, ConfigLayerKind.Base, FileValues(file));
        }

        foreach (var file in files.Where(f => string.Equals(f.Environment, options.Environment, StringComparison.OrdinalIgnoreCase)))
        {
            yield return (file.FileName, ConfigLayerKind.EnvironmentFile, FileValues(file));
        }

        if (options.IncludeUserSecrets && configuration.Secrets is { } secrets)
        {
            yield return (UserSecretsSource, ConfigLayerKind.UserSecrets, secrets.Select(p => (p.Key, (string?)p.Value)));
        }

        if (options.LaunchProfile is { } profile)
        {
            yield return ($"launchSettings: {profile.Name}", ConfigLayerKind.LaunchProfile,
                profile.EnvironmentVariables.Select(v => (ToConfigKey(v.Key), (string?)v.Value)));
        }

        foreach (var file in files.Where(f => f.IsCustom))
        {
            yield return (file.FileName, ConfigLayerKind.CustomFile, FileValues(file));
        }
    }

    private static IEnumerable<(string, string?)> FileValues(AppSettingsFile file) =>
        file.Document!.Values.Select(v => (v.Key, v.Value));

    /// <summary>Environment variables use <c>__</c> as the section separator.</summary>
    private static string ToConfigKey(string variableName) =>
        variableName.Replace("__", ConfigKey.Delimiter, StringComparison.Ordinal);
}
