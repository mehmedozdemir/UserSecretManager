using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using UserSecretManager.Core.IO;

namespace UserSecretManager.Core.History;

/// <summary>A file captured in a backup.</summary>
/// <param name="OriginalPath">Where the file lives.</param>
/// <param name="BackupFileName">Name of the copy inside the backup directory; <c>null</c> if the file did not exist.</param>
public sealed record BackupFile(string OriginalPath, string? BackupFileName)
{
    /// <summary>Whether the file existed when the backup was taken.</summary>
    [JsonIgnore]
    public bool Existed => BackupFileName is not null;
}

/// <summary>One entry of the operation history together with the files as they were before the operation.</summary>
public sealed record BackupEntry
{
    /// <summary>Unique id (also the directory name).</summary>
    public required string Id { get; init; }

    /// <summary>When the backup was taken.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Project the operation belongs to.</summary>
    public required string ProjectPath { get; init; }

    /// <summary>Project display name.</summary>
    public required string ProjectName { get; init; }

    /// <summary>What the operation did.</summary>
    public required string Description { get; init; }

    /// <summary>Captured files.</summary>
    public required IReadOnlyList<BackupFile> Files { get; init; }
}

/// <summary>
/// Stores copies of files before they are modified and restores them on request. Keeps the latest
/// <see cref="MaxEntries"/> entries.
/// </summary>
public sealed class BackupService
{
    /// <summary>Default number of history entries kept.</summary>
    public const int DefaultMaxEntries = 50;

    private const string ManifestFileName = "manifest.json";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _root;

    /// <summary>Creates a service that stores backups under <paramref name="rootDirectory"/>.</summary>
    public BackupService(string rootDirectory, int maxEntries = DefaultMaxEntries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);
        _root = rootDirectory;
        MaxEntries = maxEntries;
    }

    /// <summary>Maximum number of entries kept.</summary>
    public int MaxEntries { get; }

    /// <summary>Copies the current state of <paramref name="paths"/> into a new backup.</summary>
    public BackupEntry Create(string projectPath, string projectName, string description, IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var now = DateTimeOffset.Now;
        var id = $"{now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}-{Guid.NewGuid().ToString("N")[..6]}";
        var directory = Path.Combine(_root, id);
        Directory.CreateDirectory(directory);

        var files = new List<BackupFile>();
        foreach (var (path, index) in paths.Distinct(StringComparer.OrdinalIgnoreCase).Select((p, i) => (p, i)))
        {
            if (!File.Exists(path))
            {
                files.Add(new BackupFile(path, null));
                continue;
            }

            var copyName = $"{index:D2}_{Path.GetFileName(path)}";
            File.Copy(path, Path.Combine(directory, copyName));
            files.Add(new BackupFile(path, copyName));
        }

        var entry = new BackupEntry
        {
            Id = id,
            CreatedAt = now,
            ProjectPath = projectPath,
            ProjectName = projectName,
            Description = description,
            Files = files,
        };
        AtomicFile.WriteAllBytes(Path.Combine(directory, ManifestFileName),
            JsonSerializer.SerializeToUtf8Bytes(entry, SerializerOptions));
        Prune();
        return entry;
    }

    /// <summary>Lists entries newest first, optionally only for one project.</summary>
    public IReadOnlyList<BackupEntry> List(string? projectPath = null)
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        var normalized = projectPath is null ? null : Path.GetFullPath(projectPath);
        return Directory.EnumerateDirectories(_root)
            .Select(TryReadManifest)
            .OfType<BackupEntry>()
            .Where(e => normalized is null ||
                        string.Equals(Path.GetFullPath(e.ProjectPath), normalized, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.CreatedAt)
            .ToList();
    }

    /// <summary>Writes the backed-up files back, deleting files that did not exist at backup time.</summary>
    public void RestoreFiles(BackupEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var directory = Path.Combine(_root, entry.Id);
        foreach (var file in entry.Files)
        {
            if (file.BackupFileName is { } copy)
            {
                AtomicFile.WriteAllBytes(file.OriginalPath, File.ReadAllBytes(Path.Combine(directory, copy)));
            }
            else if (File.Exists(file.OriginalPath))
            {
                File.Delete(file.OriginalPath);
            }
        }
    }

    /// <summary>
    /// Restores <paramref name="entry"/> after backing up the current state, so the restore itself can be undone.
    /// Returns the safety backup.
    /// </summary>
    public BackupEntry Restore(BackupEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var safety = Create(entry.ProjectPath, entry.ProjectName,
            $"Geri yükleme öncesi durum ({entry.CreatedAt:yyyy-MM-dd HH:mm:ss} kaydı)",
            entry.Files.Select(f => f.OriginalPath));
        RestoreFiles(entry);
        return safety;
    }

    private BackupEntry? TryReadManifest(string directory)
    {
        var path = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<BackupEntry>(TextFileContent.Read(path).Text, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Prune()
    {
        var stale = Directory.EnumerateDirectories(_root)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(MaxEntries);
        foreach (var directory in stale)
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
