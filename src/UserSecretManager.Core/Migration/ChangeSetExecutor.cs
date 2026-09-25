using System.Text.Json;
using UserSecretManager.Core.Configuration;
using UserSecretManager.Core.History;
using UserSecretManager.Core.IO;

namespace UserSecretManager.Core.Migration;

/// <summary>Thrown when a file changed on disk after the change set was computed.</summary>
public sealed class FileChangedExternallyException : Exception
{
    /// <summary>Creates the exception.</summary>
    public FileChangedExternallyException(string path)
        : base($"'{Path.GetFileName(path)}' bu işlem hazırlandıktan sonra değişti. Yenileyip tekrar deneyin.")
    {
        FilePath = path;
    }

    /// <summary>The changed file.</summary>
    public string FilePath { get; } = string.Empty;
}

/// <summary>
/// Applies change sets safely: verifies nothing changed on disk, validates the new JSON, takes a backup, writes each
/// file atomically and rolls back from the backup if any write fails.
/// </summary>
public sealed class ChangeSetExecutor
{
    private readonly BackupService _backups;

    /// <summary>Creates an executor.</summary>
    public ChangeSetExecutor(BackupService backups)
    {
        ArgumentNullException.ThrowIfNull(backups);
        _backups = backups;
    }

    /// <summary>Applies the changes and returns the history entry.</summary>
    public BackupEntry Apply(ChangeSet changeSet)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        if (changeSet.IsEmpty)
        {
            throw new InvalidOperationException("Uygulanacak değişiklik yok.");
        }

        foreach (var change in changeSet.Changes)
        {
            EnsureUnchanged(change);
            EnsureValid(change);
        }

        var backup = _backups.Create(changeSet.ProjectPath, changeSet.ProjectName, changeSet.Description,
            changeSet.Changes.Select(c => c.Path));
        try
        {
            foreach (var change in changeSet.Changes)
            {
                AtomicFile.WriteText(change.Path, change.NewContent);
            }
        }
        catch
        {
            _backups.RestoreFiles(backup);
            throw;
        }

        return backup;
    }

    private static void EnsureUnchanged(FileChange change)
    {
        var exists = File.Exists(change.Path);
        if (change.OriginalText is null)
        {
            if (exists)
            {
                throw new FileChangedExternallyException(change.Path);
            }

            return;
        }

        if (!exists || !string.Equals(TextFileContent.Read(change.Path).Text, change.OriginalText, StringComparison.Ordinal))
        {
            throw new FileChangedExternallyException(change.Path);
        }
    }

    private static void EnsureValid(FileChange change)
    {
        if (change.Kind == FileChangeKind.ProjectFile)
        {
            return;
        }

        try
        {
            JsonConfigDocument.Parse(change.NewContent.Text);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"{change.DisplayName} için üretilen içerik geçerli JSON değil; işlem iptal edildi. ({ex.Message})", ex);
        }
    }
}
