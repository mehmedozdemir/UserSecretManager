using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UserSecretManager.Core.Configuration;
using UserSecretManager.Core.Discovery;
using UserSecretManager.Core.IO;
using UserSecretManager.Core.Secrets;
using UserSecretManager.Core.Security;

namespace UserSecretManager.Core.Profiles;

/// <summary>Metadata of a saved profile (values stay encrypted until loaded).</summary>
/// <param name="Name">Profile name, unique per UserSecretsId (case-insensitive).</param>
/// <param name="Description">Optional note.</param>
/// <param name="CreatedAt">When the profile was created.</param>
/// <param name="UpdatedAt">When the values were last written.</param>
/// <param name="KeyCount">Number of secrets in the profile.</param>
public sealed record SecretProfileInfo(string Name, string? Description, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int KeyCount);

/// <summary>
/// Named, encrypted sets of user secret values per UserSecretsId, e.g. "Local", "Staging DB". A profile can be applied
/// to <c>secrets.json</c> to switch the values a developer runs with.
/// </summary>
public sealed class SecretProfileStore
{
    /// <summary>Maximum profile name length.</summary>
    public const int MaxNameLength = 64;

    private const int FileNameHashLength = 16;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _root;
    private readonly ISecretProtector _protector;

    /// <summary>Creates a store under <paramref name="rootDirectory"/>.</summary>
    public SecretProfileStore(string rootDirectory, ISecretProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(protector);
        _root = rootDirectory;
        _protector = protector;
    }

    /// <summary>Returns why <paramref name="name"/> cannot be used, or <c>null</c> when it is valid.</summary>
    public static string? ValidateName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? "Profil adı boş olamaz"
            : trimmed.Length > MaxNameLength ? $"Profil adı en fazla {MaxNameLength} karakter olabilir"
            : trimmed.Any(char.IsControl) ? "Profil adı kontrol karakteri içeremez"
            : null;
    }

    /// <summary>Profiles of a UserSecretsId, sorted by name.</summary>
    public IReadOnlyList<SecretProfileInfo> List(string userSecretsId)
    {
        var directory = DirectoryFor(userSecretsId);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*.json")
            .Select(TryRead)
            .OfType<ProfileFile>()
            .Select(f => f.ToInfo())
            .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Whether a profile with this name exists.</summary>
    public bool Exists(string userSecretsId, string name) => File.Exists(PathFor(userSecretsId, name));

    /// <summary>Decrypts and returns the values of a profile.</summary>
    /// <exception cref="CryptographicException">The profile was encrypted by another user or machine.</exception>
    public IReadOnlyList<KeyValuePair<string, string>> Load(string userSecretsId, string name)
    {
        var file = TryRead(PathFor(userSecretsId, name))
                   ?? throw new FileNotFoundException($"'{name}' profili bulunamadı.");
        if (!string.Equals(file.Protector, _protector.Name, StringComparison.Ordinal))
        {
            throw new CryptographicException(
                $"'{name}' profili farklı bir yöntemle ({file.Protector}) şifrelenmiş ve bu sistemde açılamıyor.");
        }

        var json = _protector.Unprotect(Convert.FromBase64String(file.Data));
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        return file.Keys.Where(values.ContainsKey).Select(k => new KeyValuePair<string, string>(k, values[k])).ToList();
    }

    /// <summary>Creates or overwrites a profile. Keeps the original creation time when overwriting.</summary>
    public SecretProfileInfo Save(string userSecretsId, string name, IEnumerable<KeyValuePair<string, string>> values,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (ValidateName(name) is { } error)
        {
            throw new ArgumentException(error, nameof(name));
        }

        name = name.Trim();
        var list = values.DistinctBy(v => v.Key, ConfigKey.Comparer).ToList();
        var path = PathFor(userSecretsId, name);
        var existing = TryRead(path);
        var now = DateTimeOffset.Now;
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(list.ToDictionary(v => v.Key, v => v.Value));
        var file = new ProfileFile
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
            Keys = list.Select(v => v.Key).ToList(),
            Protector = _protector.Name,
            Data = Convert.ToBase64String(_protector.Protect(plaintext)),
        };
        AtomicFile.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(file, SerializerOptions));
        return file.ToInfo();
    }

    /// <summary>Renames a profile.</summary>
    public void Rename(string userSecretsId, string name, string newName)
    {
        if (ValidateName(newName) is { } error)
        {
            throw new ArgumentException(error, nameof(newName));
        }

        newName = newName.Trim();
        var source = PathFor(userSecretsId, name);
        var file = TryRead(source) ?? throw new FileNotFoundException($"'{name}' profili bulunamadı.");
        var target = PathFor(userSecretsId, newName);
        if (!string.Equals(source, target, StringComparison.Ordinal) && File.Exists(target))
        {
            throw new InvalidOperationException($"'{newName}' adında bir profil zaten var.");
        }

        var renamed = file with { Name = newName };
        AtomicFile.WriteAllBytes(target, JsonSerializer.SerializeToUtf8Bytes(renamed, SerializerOptions));
        if (!string.Equals(source, target, StringComparison.Ordinal))
        {
            File.Delete(source);
        }
    }

    /// <summary>Deletes a profile.</summary>
    public void Delete(string userSecretsId, string name)
    {
        var path = PathFor(userSecretsId, name);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Whether <paramref name="values"/> equal the current secrets exactly (same keys and values).</summary>
    public static bool Matches(IReadOnlyCollection<KeyValuePair<string, string>> values, SecretCollection? secrets)
    {
        ArgumentNullException.ThrowIfNull(values);
        return secrets is not null && secrets.Count == values.Count &&
               values.All(v => secrets.TryGetValue(v.Key, out var current) && string.Equals(current, v.Value, StringComparison.Ordinal));
    }

    private string DirectoryFor(string userSecretsId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userSecretsId);
        if (!ProjectInspector.IsValidId(userSecretsId))
        {
            throw new ArgumentException($"Geçersiz UserSecretsId: '{userSecretsId}'.", nameof(userSecretsId));
        }

        return Path.Combine(_root, userSecretsId);
    }

    /// <summary>File names derive from a hash of the lower-cased name, so any name is safe on every file system.</summary>
    private string PathFor(string userSecretsId, string name)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(name.Trim().ToUpperInvariant()));
        return Path.Combine(DirectoryFor(userSecretsId), Convert.ToHexStringLower(hash)[..FileNameHashLength] + ".json");
    }

    private static ProfileFile? TryRead(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProfileFile>(TextFileContent.Read(path).Text, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ProfileFile
    {
        public required string Name { get; init; }

        public string? Description { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }

        public required DateTimeOffset UpdatedAt { get; init; }

        public required List<string> Keys { get; init; }

        public required string Protector { get; init; }

        public required string Data { get; init; }

        public SecretProfileInfo ToInfo() => new(Name, Description, CreatedAt, UpdatedAt, Keys.Count);
    }
}
