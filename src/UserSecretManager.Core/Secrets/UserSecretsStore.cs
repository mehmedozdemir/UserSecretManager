using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using UserSecretManager.Core.Configuration;
using UserSecretManager.Core.Discovery;
using UserSecretManager.Core.IO;

namespace UserSecretManager.Core.Secrets;

/// <summary>
/// Reads and writes <c>secrets.json</c> files in the same location and format as <c>dotnet user-secrets</c>.
/// </summary>
public sealed class UserSecretsStore
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string? _rootOverride;

    /// <summary>Creates a store. <paramref name="rootOverride"/> replaces the platform root (used by tests).</summary>
    public UserSecretsStore(string? rootOverride = null)
    {
        _rootOverride = rootOverride;
    }

    /// <summary>Returns the path of the secrets file for <paramref name="userSecretsId"/>.</summary>
    public string GetFilePath(string userSecretsId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userSecretsId);
        if (!ProjectInspector.IsValidId(userSecretsId))
        {
            throw new ArgumentException($"Geçersiz UserSecretsId: '{userSecretsId}'.", nameof(userSecretsId));
        }

        if (_rootOverride is not null)
        {
            return Path.Combine(_rootOverride, userSecretsId, "secrets.json");
        }

        // Mirrors Microsoft.Extensions.Configuration.UserSecrets.PathHelper.
        var appData = Environment.GetEnvironmentVariable("APPDATA");
        if (!string.IsNullOrEmpty(appData))
        {
            return Path.Combine(appData, "Microsoft", "UserSecrets", userSecretsId, "secrets.json");
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        var root = !string.IsNullOrEmpty(home)
            ? home
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(root, ".microsoft", "usersecrets", userSecretsId, "secrets.json");
    }

    /// <summary>
    /// Loads the secrets of <paramref name="userSecretsId"/>. A missing file yields an empty collection.
    /// Throws <see cref="JsonException"/> if the file is not valid JSON.
    /// </summary>
    public SecretCollection Load(string userSecretsId)
    {
        var path = GetFilePath(userSecretsId);
        if (!File.Exists(path))
        {
            return new SecretCollection(path, exists: false, rawText: null);
        }

        var content = TextFileContent.Read(path);
        var collection = new SecretCollection(path, exists: true, content.Text);
        if (string.IsNullOrWhiteSpace(content.Text))
        {
            return collection;
        }

        foreach (var value in JsonConfigDocument.Parse(content.Text).Values)
        {
            collection[value.Key] = value.Value ?? string.Empty;
        }

        return collection;
    }

    /// <summary>Serializes secrets as a flat JSON object, like <c>dotnet user-secrets set</c> does.</summary>
    public static string Serialize(IEnumerable<KeyValuePair<string, string>> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            foreach (var pair in secrets)
            {
                writer.WriteString(pair.Key, pair.Value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
