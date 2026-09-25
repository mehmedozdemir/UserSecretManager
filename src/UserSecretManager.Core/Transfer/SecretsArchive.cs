using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UserSecretManager.Core.Transfer;

/// <summary>Secrets and where they came from, as stored in an export file.</summary>
/// <param name="ProjectName">Project the secrets were exported from.</param>
/// <param name="UserSecretsId">UserSecretsId at export time.</param>
/// <param name="ExportedAt">Export time.</param>
/// <param name="Secrets">The secrets in file order.</param>
public sealed record SecretsArchiveContent(
    string? ProjectName,
    string? UserSecretsId,
    DateTimeOffset ExportedAt,
    IReadOnlyList<KeyValuePair<string, string>> Secrets);

/// <summary>Thrown when an export file cannot be read (wrong password, corrupt or unsupported file).</summary>
public sealed class SecretsArchiveException : Exception
{
    /// <summary>Creates the exception.</summary>
    public SecretsArchiveException()
    {
    }

    /// <summary>Creates the exception.</summary>
    public SecretsArchiveException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public SecretsArchiveException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Password-protected export files for sharing secrets with a teammate or moving them to another machine.
/// Key: PBKDF2-SHA256 (600 000 iterations, random salt). Cipher: AES-256-GCM with the header as associated data.
/// </summary>
public static class SecretsArchive
{
    /// <summary>File extension of export files.</summary>
    public const string FileExtension = ".usmsecrets";

    /// <summary>Minimum password length.</summary>
    public const int MinPasswordLength = 8;

    private const string Format = "usm-secrets";
    private const int Version = 1;
    private const string Kdf = "pbkdf2-sha256";
    private const int DefaultIterations = 600_000;

    // Iterations come from the file; bound them so a crafted file cannot hang the application.
    private const int MaxIterations = 10_000_000;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>Returns why <paramref name="password"/> is not acceptable, or <c>null</c>.</summary>
    public static string? ValidatePassword(string? password) =>
        string.IsNullOrEmpty(password) || password.Length < MinPasswordLength
            ? $"Parola en az {MinPasswordLength} karakter olmalı"
            : null;

    /// <summary>Encrypts <paramref name="content"/> with <paramref name="password"/>.</summary>
    public static byte[] Export(SecretsArchiveContent content, string password) =>
        Export(content, password, DefaultIterations);

    internal static byte[] Export(SecretsArchiveContent content, string password, int iterations)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (ValidatePassword(password) is { } error)
        {
            throw new ArgumentException(error, nameof(password));
        }

        var payload = new Payload
        {
            ProjectName = content.ProjectName,
            UserSecretsId = content.UserSecretsId,
            ExportedAt = content.ExportedAt,
            Secrets = content.Secrets.Select(s => new Entry { Key = s.Key, Value = s.Value }).ToList(),
        };
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        var key = DeriveKey(password, salt, iterations);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(iterations));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }

        var envelope = new Envelope
        {
            Format = Format,
            Version = Version,
            Kdf = Kdf,
            Iterations = iterations,
            Salt = Convert.ToBase64String(salt),
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Ciphertext = Convert.ToBase64String(ciphertext),
        };
        return JsonSerializer.SerializeToUtf8Bytes(envelope, SerializerOptions);
    }

    /// <summary>Decrypts an export file.</summary>
    /// <exception cref="SecretsArchiveException">Wrong password, corrupt or unsupported file.</exception>
    public static SecretsArchiveContent Import(byte[] data, string password)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(password);

        var envelope = ReadEnvelope(data);
        byte[] plaintext;
        var key = DeriveKey(password, Convert.FromBase64String(envelope.Salt), envelope.Iterations);
        try
        {
            var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
            plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(Convert.FromBase64String(envelope.Nonce), ciphertext, Convert.FromBase64String(envelope.Tag),
                plaintext, AssociatedData(envelope.Iterations));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            throw new SecretsArchiveException("Parola yanlış veya dosya bozuk.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(plaintext)
                          ?? throw new SecretsArchiveException("Dosya içeriği boş.");
            return new SecretsArchiveContent(payload.ProjectName, payload.UserSecretsId, payload.ExportedAt,
                payload.Secrets.Select(e => new KeyValuePair<string, string>(e.Key, e.Value)).ToList());
        }
        catch (JsonException ex)
        {
            throw new SecretsArchiveException("Dosya içeriği okunamadı.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static Envelope ReadEnvelope(byte[] data)
    {
        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(data);
        }
        catch (JsonException ex)
        {
            throw new SecretsArchiveException("Bu bir User Secret Manager dışa aktarım dosyası değil.", ex);
        }

        if (envelope is null || envelope.Format != Format)
        {
            throw new SecretsArchiveException("Bu bir User Secret Manager dışa aktarım dosyası değil.");
        }

        if (envelope.Version != Version || envelope.Kdf != Kdf || envelope.Iterations is < 1 or > MaxIterations)
        {
            throw new SecretsArchiveException($"Desteklenmeyen dosya sürümü ({envelope.Version}).");
        }

        return envelope;
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, KeySize);

    private static byte[] AssociatedData(int iterations) =>
        Encoding.UTF8.GetBytes($"{Format}/{Version}/{Kdf}/{iterations}");

    private sealed class Envelope
    {
        public required string Format { get; init; }

        public required int Version { get; init; }

        public required string Kdf { get; init; }

        public required int Iterations { get; init; }

        public required string Salt { get; init; }

        public required string Nonce { get; init; }

        public required string Tag { get; init; }

        public required string Ciphertext { get; init; }
    }

    private sealed class Payload
    {
        public string? ProjectName { get; init; }

        public string? UserSecretsId { get; init; }

        public DateTimeOffset ExportedAt { get; init; }

        public List<Entry> Secrets { get; init; } = [];
    }

    private sealed class Entry
    {
        public required string Key { get; init; }

        public required string Value { get; init; }
    }
}
