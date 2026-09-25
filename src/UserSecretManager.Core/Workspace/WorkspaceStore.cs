using System.Text.Json;
using System.Text.Json.Serialization;
using UserSecretManager.Core.IO;

namespace UserSecretManager.Core.Workspace;

/// <summary>Kind of a remembered item.</summary>
public enum WorkspaceItemKind
{
    /// <summary>A <c>.sln</c> or <c>.slnx</c> file.</summary>
    Solution,

    /// <summary>A project file.</summary>
    Project,
}

/// <summary>A remembered solution or project. Never contains secret values.</summary>
public sealed class WorkspaceItem
{
    /// <summary>Full path of the solution or project file.</summary>
    public required string Path { get; init; }

    /// <summary>Kind of item.</summary>
    public required WorkspaceItemKind Kind { get; init; }

    /// <summary>When the item was added.</summary>
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>When the item was last opened.</summary>
    public DateTimeOffset? LastOpenedAt { get; set; }

    /// <summary>Whether the user pinned the item.</summary>
    public bool IsFavorite { get; set; }

    /// <summary>File name without extension.</summary>
    [JsonIgnore]
    public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(Path);
}

/// <summary>The application's persisted state.</summary>
public sealed class WorkspaceState
{
    /// <summary>Remembered items.</summary>
    public List<WorkspaceItem> Items { get; init; } = [];

    /// <summary>Last selected project path, restored on start.</summary>
    public string? LastProjectPath { get; set; }

    /// <summary>"System", "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";
}

/// <summary>Loads and saves <see cref="WorkspaceState"/>.</summary>
public sealed class WorkspaceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    /// <summary>Creates a store backed by <paramref name="filePath"/>.</summary>
    public WorkspaceStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    /// <summary>Loads the state. A missing or corrupt file yields an empty state (a corrupt file is kept aside).</summary>
    public WorkspaceState Load()
    {
        if (!File.Exists(_filePath))
        {
            return new WorkspaceState();
        }

        try
        {
            return JsonSerializer.Deserialize<WorkspaceState>(TextFileContent.Read(_filePath).Text, SerializerOptions)
                   ?? new WorkspaceState();
        }
        catch (JsonException)
        {
            File.Copy(_filePath, _filePath + ".corrupt", overwrite: true);
            return new WorkspaceState();
        }
    }

    /// <summary>Saves the state atomically.</summary>
    public void Save(WorkspaceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        AtomicFile.WriteAllBytes(_filePath, JsonSerializer.SerializeToUtf8Bytes(state, SerializerOptions));
    }
}
