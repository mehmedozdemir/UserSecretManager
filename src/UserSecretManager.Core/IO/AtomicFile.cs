namespace UserSecretManager.Core.IO;

/// <summary>
/// Writes files through a temporary file and a rename so a crash never leaves a half-written file behind.
/// </summary>
public static class AtomicFile
{
    /// <summary>Atomically replaces (or creates) <paramref name="path"/> with <paramref name="bytes"/>.</summary>
    public static void WriteAllBytes(string path, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bytes);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <summary>Atomically writes text content, preserving its BOM setting.</summary>
    public static void WriteText(string path, TextFileContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        WriteAllBytes(path, content.ToBytes());
    }
}
