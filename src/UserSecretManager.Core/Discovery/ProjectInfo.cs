namespace UserSecretManager.Core.Discovery;

/// <summary>Where a project's <c>UserSecretsId</c> is defined.</summary>
public enum UserSecretsIdSource
{
    /// <summary>No id is defined; one can be added to the project file.</summary>
    None,

    /// <summary>Defined as an MSBuild property in the project file.</summary>
    ProjectFile,

    /// <summary>Inherited from a <c>Directory.Build.props</c> file.</summary>
    DirectoryBuildProps,

    /// <summary>Defined with <c>[assembly: UserSecretsId("...")]</c> in source code.</summary>
    AssemblyAttribute,

    /// <summary>Defined through an MSBuild expression this tool cannot evaluate.</summary>
    Unresolvable,
}

/// <summary>A project's user secrets id and where it comes from.</summary>
/// <param name="Id">The id, or <c>null</c> when none is defined or it cannot be evaluated.</param>
/// <param name="Source">Where the id is defined.</param>
/// <param name="SourcePath">File that defines the id, if any.</param>
/// <param name="RawValue">Raw text of the definition (useful when unresolvable).</param>
public sealed record UserSecretsIdInfo(string? Id, UserSecretsIdSource Source, string? SourcePath, string? RawValue = null)
{
    /// <summary>No id defined.</summary>
    public static UserSecretsIdInfo Missing { get; } = new(null, UserSecretsIdSource.None, null);

    /// <summary>Whether a usable id exists.</summary>
    public bool IsDefined => Id is not null;

    /// <summary>Whether secrets can be written (an id exists or can be added to the project file).</summary>
    public bool CanWrite => Source != UserSecretsIdSource.Unresolvable;
}

/// <summary>Everything the tool needs to know about a project.</summary>
public sealed record ProjectInfo
{
    /// <summary>Full path to the project file.</summary>
    public required string ProjectPath { get; init; }

    /// <summary>Project name (file name without extension).</summary>
    public string Name => Path.GetFileNameWithoutExtension(ProjectPath);

    /// <summary>Directory that contains the project file.</summary>
    public string Directory => Path.GetDirectoryName(ProjectPath) ?? string.Empty;

    /// <summary>The MSBuild SDK, e.g. <c>Microsoft.NET.Sdk.Web</c>.</summary>
    public string? Sdk { get; init; }

    /// <summary>The user secrets id.</summary>
    public required UserSecretsIdInfo SecretsId { get; init; }

    /// <summary>Whether the project is known to load user secrets at runtime (Web SDK, hosting or the package).</summary>
    public bool HasUserSecretsSupport { get; init; }

    /// <summary>Full paths of <c>appsettings*.json</c> files next to the project file.</summary>
    public IReadOnlyList<string> ConfigFilePaths { get; init; } = [];

    /// <summary>Environment names used by <c>launchSettings.json</c> profiles.</summary>
    public IReadOnlyList<string> LaunchEnvironments { get; init; } = [];

    /// <summary>Root of the git repository that contains the project, if any.</summary>
    public string? GitRoot { get; init; }
}
