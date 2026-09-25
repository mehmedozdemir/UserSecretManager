using System.Runtime.Versioning;
using System.Security.Cryptography;
using UserSecretManager.Core.IO;

namespace UserSecretManager.Core.Security;

/// <summary>Encrypts data at rest for the current user.</summary>
public interface ISecretProtector
{
    /// <summary>Identifier stored next to protected data.</summary>
    string Name { get; }

    /// <summary>Encrypts <paramref name="plaintext"/>.</summary>
    byte[] Protect(byte[] plaintext);

    /// <summary>Decrypts data produced by <see cref="Protect"/>. Throws <see cref="CryptographicException"/> on failure.</summary>
    byte[] Unprotect(byte[] protectedData);
}

/// <summary>Windows DPAPI bound to the current user account.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "UserSecretManager.Profiles.v1"u8.ToArray();

    /// <inheritdoc />
    public string Name => "dpapi";

    /// <inheritdoc />
    public byte[] Protect(byte[] plaintext) => ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

    /// <inheritdoc />
    public byte[] Unprotect(byte[] protectedData) =>
        ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
}

/// <summary>
/// AES-256-GCM with a random key kept in a file only the current user can read (used where DPAPI is unavailable).
/// Layout: nonce (12) | tag (16) | ciphertext.
/// </summary>
public sealed class KeyFileSecretProtector : ISecretProtector
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly string _keyPath;
    private byte[]? _key;

    /// <summary>Creates a protector whose key lives at <paramref name="keyPath"/> (created on first use).</summary>
    public KeyFileSecretProtector(string keyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        _keyPath = keyPath;
    }

    /// <inheritdoc />
    public string Name => "keyfile";

    /// <inheritdoc />
    public byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var result = new byte[NonceSize + TagSize + plaintext.Length];
        var nonce = result.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(GetKey(), TagSize);
        aes.Encrypt(nonce, plaintext, result.AsSpan(NonceSize + TagSize), result.AsSpan(NonceSize, TagSize));
        return result;
    }

    /// <inheritdoc />
    public byte[] Unprotect(byte[] protectedData)
    {
        ArgumentNullException.ThrowIfNull(protectedData);
        if (protectedData.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Şifreli veri bozuk.");
        }

        var plaintext = new byte[protectedData.Length - NonceSize - TagSize];
        using var aes = new AesGcm(GetKey(), TagSize);
        aes.Decrypt(protectedData.AsSpan(0, NonceSize), protectedData.AsSpan(NonceSize + TagSize),
            protectedData.AsSpan(NonceSize, TagSize), plaintext);
        return plaintext;
    }

    private byte[] GetKey()
    {
        if (_key is not null)
        {
            return _key;
        }

        if (File.Exists(_keyPath))
        {
            _key = File.ReadAllBytes(_keyPath);
            if (_key.Length != KeySize)
            {
                throw new CryptographicException($"Anahtar dosyası geçersiz: {_keyPath}");
            }

            return _key;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_keyPath))!);
        var key = RandomNumberGenerator.GetBytes(KeySize);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var stream = new FileStream(_keyPath, options))
        {
            stream.Write(key);
        }

        _key = key;
        return _key;
    }
}

/// <summary>Chooses the protector for the current platform.</summary>
public static class SecretProtectors
{
    /// <summary>DPAPI on Windows; a user-only key file elsewhere.</summary>
    public static ISecretProtector CreateDefault(AppDataLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return OperatingSystem.IsWindows()
            ? new DpapiSecretProtector()
            : new KeyFileSecretProtector(location.ProfileKeyFile);
    }
}
