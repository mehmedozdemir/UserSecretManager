namespace UserSecretManager.Core.IO;

/// <summary>
/// Locations of the application's own data (workspace list, backups). Never holds secret values itself,
/// except inside backups which mirror files the user already keeps on the same machine.
/// </summary>
public sealed class AppDataLocation
{
    /// <summary>Creates a location rooted at <paramref name="rootDirectory"/>.</summary>
    public AppDataLocation(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = rootDirectory;
    }

    /// <summary>The default per-user location (LocalApplicationData/UserSecretManager).</summary>
    public static AppDataLocation Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UserSecretManager"));

    /// <summary>Root directory of all application data.</summary>
    public string RootDirectory { get; }

    /// <summary>File that stores the remembered projects and preferences.</summary>
    public string WorkspaceFile => Path.Combine(RootDirectory, "workspace.json");

    /// <summary>Directory that holds one sub-directory per backup.</summary>
    public string BackupsDirectory => Path.Combine(RootDirectory, "backups");

    /// <summary>Directory that holds encrypted secret profiles, one sub-directory per UserSecretsId.</summary>
    public string ProfilesDirectory => Path.Combine(RootDirectory, "profiles");

    /// <summary>Key file used to encrypt profiles where DPAPI is not available.</summary>
    public string ProfileKeyFile => Path.Combine(RootDirectory, "profiles.key");
}
