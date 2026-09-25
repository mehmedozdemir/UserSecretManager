using UserSecretManager.Core.Configuration;

namespace UserSecretManager.Core.Migration;

/// <summary>Severity of a plan notice.</summary>
public enum NoticeSeverity
{
    /// <summary>For information.</summary>
    Info,

    /// <summary>Needs attention but does not block.</summary>
    Warning,

    /// <summary>Blocks applying the plan.</summary>
    Error,
}

/// <summary>A message about the plan as a whole.</summary>
/// <param name="Severity">Severity.</param>
/// <param name="Message">Message text.</param>
public sealed record PlanNotice(NoticeSeverity Severity, string Message);

/// <summary>A possible value for a secret and where it comes from.</summary>
/// <param name="Value">The value.</param>
/// <param name="Sources">Labels of the files (or "User Secrets") holding this value.</param>
/// <param name="IsExistingSecret">Whether this is the value currently in user secrets.</param>
public sealed record ValueCandidate(string Value, IReadOnlyList<string> Sources, bool IsExistingSecret);

/// <summary>Clearing a key in one appsettings file.</summary>
public sealed class FileClearAction
{
    internal FileClearAction(AppSettingsFile file, string oldValue, bool isEnabled, string? warning)
    {
        File = file;
        OldValue = oldValue;
        IsEnabled = isEnabled;
        Warning = warning;
    }

    /// <summary>The file to edit.</summary>
    public AppSettingsFile File { get; }

    /// <summary>Current value in the file.</summary>
    public string OldValue { get; }

    /// <summary>Whether the value will be cleared.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Why clearing this file needs attention, if it does.</summary>
    public string? Warning { get; }
}

/// <summary>Moving one key to user secrets.</summary>
public sealed class MigrationItem
{
    internal MigrationItem(string key, IReadOnlyList<ValueCandidate> candidates, ValueCandidate selected,
        IReadOnlyList<FileClearAction> fileActions)
    {
        Key = key;
        Candidates = candidates;
        Selected = selected;
        FileActions = fileActions;
    }

    /// <summary>Configuration key.</summary>
    public string Key { get; }

    /// <summary>Distinct non-empty values found, highest priority first.</summary>
    public IReadOnlyList<ValueCandidate> Candidates { get; }

    /// <summary>The value that will be stored as the secret.</summary>
    public ValueCandidate Selected { get; set; }

    /// <summary>Whether the files (or an existing secret) disagree on the value.</summary>
    public bool HasConflict => Candidates.Count > 1;

    /// <summary>Per-file clear actions.</summary>
    public IReadOnlyList<FileClearAction> FileActions { get; }

    /// <summary>Name of the environment variable that can supply the value outside Development.</summary>
    public string EnvironmentVariableName => ConfigKey.ToEnvironmentVariableName(Key);
}

/// <summary>A reviewed, editable plan for moving keys to user secrets.</summary>
public sealed class MigrationPlan
{
    internal MigrationPlan(ProjectConfiguration configuration, IReadOnlyList<MigrationItem> items,
        IReadOnlyList<string> skippedKeys, string? newUserSecretsId, string secretsFilePath, IReadOnlyList<PlanNotice> notices)
    {
        Configuration = configuration;
        Items = items;
        SkippedKeys = skippedKeys;
        NewUserSecretsId = newUserSecretsId;
        SecretsFilePath = secretsFilePath;
        Notices = notices;
    }

    /// <summary>The snapshot the plan was built from.</summary>
    public ProjectConfiguration Configuration { get; }

    /// <summary>Keys to move.</summary>
    public IReadOnlyList<MigrationItem> Items { get; }

    /// <summary>Selected keys that have no value anywhere and are therefore skipped.</summary>
    public IReadOnlyList<string> SkippedKeys { get; }

    /// <summary>Id that will be added to the project file, when the project has none.</summary>
    public string? NewUserSecretsId { get; }

    /// <summary>Secrets file that will be written.</summary>
    public string SecretsFilePath { get; }

    /// <summary>Plan-level notices.</summary>
    public IReadOnlyList<PlanNotice> Notices { get; }

    /// <summary>Whether an error prevents applying the plan.</summary>
    public bool IsBlocked => Notices.Any(n => n.Severity == NoticeSeverity.Error) || Items.Count == 0;
}
