using System.Text.Json;
using UserSecretManager.Core.Analysis;
using UserSecretManager.Core.Configuration;
using UserSecretManager.Core.Discovery;
using UserSecretManager.Core.Secrets;

namespace UserSecretManager.Core.Migration;

/// <summary>A consistent snapshot of a project's configuration files and user secrets.</summary>
public sealed class ProjectConfiguration
{
    private ProjectConfiguration(ProjectInfo project, IReadOnlyList<AppSettingsFile> files,
        SecretCollection? secrets, string secretsFilePath, string? secretsError)
    {
        Project = project;
        Files = files;
        Secrets = secrets;
        SecretsFilePath = secretsFilePath;
        SecretsError = secretsError;
    }

    /// <summary>The inspected project.</summary>
    public ProjectInfo Project { get; }

    /// <summary>appsettings files in display order (base, Development, others).</summary>
    public IReadOnlyList<AppSettingsFile> Files { get; }

    /// <summary>Files that could be parsed.</summary>
    public IEnumerable<AppSettingsFile> ValidFiles => Files.Where(f => f.Document is not null);

    /// <summary>Current secrets; empty when the project has no id yet, <c>null</c> when the file is unreadable.</summary>
    public SecretCollection? Secrets { get; }

    /// <summary>
    /// Path of the secrets file, or an empty string when the id is missing or cannot be evaluated.
    /// </summary>
    public string SecretsFilePath { get; }

    /// <summary>Error reading the secrets file, if any.</summary>
    public string? SecretsError { get; }

    /// <summary>Custom files that were configured but no longer exist.</summary>
    public IReadOnlyList<string> MissingCustomFiles { get; private set; } = [];

    /// <summary>
    /// Loads the snapshot. <paramref name="customFiles"/> are extra JSON files (absolute, or relative to the project
    /// directory) that the user added to the project.
    /// </summary>
    public static ProjectConfiguration Load(ProjectInfo project, UserSecretsStore store, IEnumerable<string>? customFiles = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(store);

        var customPaths = (customFiles ?? [])
            .Select(p => Path.GetFullPath(p, project.Directory))
            .Where(p => !project.ConfigFilePaths.Contains(p, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var files = project.ConfigFilePaths
            .Select(AppSettingsFile.Load)
            .Concat(customPaths.Where(File.Exists).Select(AppSettingsFile.LoadCustom))
            .Order(Comparer<AppSettingsFile>.Create(AppSettingsFile.CompareForDisplay))
            .ToList();
        var configuration = LoadSecrets(project, store, files);
        configuration.MissingCustomFiles = customPaths.Where(p => !File.Exists(p)).ToList();
        return configuration;
    }

    private static ProjectConfiguration LoadSecrets(ProjectInfo project, UserSecretsStore store, List<AppSettingsFile> files)
    {
        if (project.SecretsId.Id is not { } id)
        {
            return new ProjectConfiguration(project, files, new SecretCollection(string.Empty, false, null), string.Empty, null);
        }

        var path = store.GetFilePath(id);
        try
        {
            return new ProjectConfiguration(project, files, store.Load(id), path, null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ProjectConfiguration(project, files, null, path, ex.Message);
        }
    }

    /// <summary>Builds one entry per distinct key across all files and secrets.</summary>
    public IReadOnlyList<ConfigEntry> BuildEntries()
    {
        var order = new List<string>();
        var perKey = new Dictionary<string, Dictionary<AppSettingsFile, string?>>(ConfigKey.Comparer);
        foreach (var file in ValidFiles)
        {
            foreach (var value in file.Document!.Values)
            {
                if (!perKey.TryGetValue(value.Key, out var map))
                {
                    map = [];
                    perKey[value.Key] = map;
                    order.Add(value.Key);
                }

                map[file] = value.Value;
            }
        }

        foreach (var key in Secrets?.Keys ?? [])
        {
            if (!perKey.ContainsKey(key))
            {
                perKey[key] = [];
                order.Add(key);
            }
        }

        return order.Select(key => CreateEntry(key, perKey[key])).ToList();
    }

    private ConfigEntry CreateEntry(string key, Dictionary<AppSettingsFile, string?> values)
    {
        string? secret = null;
        var hasSecret = Secrets is not null && Secrets.TryGetValue(key, out secret!);
        var hint = SensitivityAnalyzer.Analyze(key, values.Values.Append(secret));
        return new ConfigEntry(key, values, hasSecret ? secret : null, hasSecret, hint);
    }
}

/// <summary>A configuration key with its value in every file and in user secrets.</summary>
/// <param name="Key">Flattened key.</param>
/// <param name="FileValues">Value per file for files that define the key.</param>
/// <param name="SecretValue">Value in user secrets, if present.</param>
/// <param name="HasSecret">Whether the key exists in user secrets.</param>
/// <param name="Hint">Sensitivity suggestion.</param>
public sealed record ConfigEntry(
    string Key,
    IReadOnlyDictionary<AppSettingsFile, string?> FileValues,
    string? SecretValue,
    bool HasSecret,
    SensitivityHint Hint)
{
    /// <summary>Whether any file still holds a non-empty value for this key.</summary>
    public bool HasFileContent => FileValues.Values.Any(v => !string.IsNullOrEmpty(v));

    /// <summary>Whether the key only exists in user secrets.</summary>
    public bool IsSecretOnly => HasSecret && FileValues.Count == 0;
}
