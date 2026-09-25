using UserSecretManager.Core.IO;

namespace UserSecretManager.Core.Migration;

/// <summary>Kind of file a change touches.</summary>
public enum FileChangeKind
{
    /// <summary>An appsettings file.</summary>
    AppSettings,

    /// <summary>The user secrets file.</summary>
    Secrets,

    /// <summary>The project file (adding <c>UserSecretsId</c>).</summary>
    ProjectFile,
}

/// <summary>A pending change to one file.</summary>
/// <param name="Path">Full path of the file.</param>
/// <param name="Kind">Kind of file.</param>
/// <param name="DisplayName">Short label for the UI.</param>
/// <param name="OriginalText">Text the change was computed from; <c>null</c> when the file does not exist yet.</param>
/// <param name="NewContent">Content to write.</param>
public sealed record FileChange(string Path, FileChangeKind Kind, string DisplayName, string? OriginalText, TextFileContent NewContent)
{
    /// <summary>Whether the change creates a new file.</summary>
    public bool IsNewFile => OriginalText is null;
}

/// <summary>A group of file changes applied together, backed up together and undone together.</summary>
/// <param name="ProjectPath">Project the changes belong to.</param>
/// <param name="ProjectName">Project display name.</param>
/// <param name="Description">What the change does, shown in history.</param>
/// <param name="Changes">The file changes.</param>
public sealed record ChangeSet(string ProjectPath, string ProjectName, string Description, IReadOnlyList<FileChange> Changes)
{
    /// <summary>Whether there is nothing to write.</summary>
    public bool IsEmpty => Changes.Count == 0;
}
