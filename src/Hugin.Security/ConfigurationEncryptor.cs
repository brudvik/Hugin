using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hugin.Security;

/// <summary>
/// Provides AES-256-GCM encryption for configuration data.
/// </summary>
public sealed class ConfigurationEncryptor
{
    private const int KeySize = 32; // 256 bits
    private const int NonceSize = 12; // 96 bits for GCM
    private const int TagSize = 16; // 128 bits

    /// <summary>
    /// Gets the master key from environment variable.
    /// </summary>
    public static byte[]? GetMasterKeyFromEnvironment()
    {
        var keyHex = Environment.GetEnvironmentVariable("HUGIN_MASTER_KEY");
        if (string.IsNullOrEmpty(keyHex))
        {
            return null;
        }

        try
        {
            return Convert.FromHexString(keyHex);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Generates a new random master key.
    /// </summary>
    public static byte[] GenerateMasterKey()
    {
        return RandomNumberGenerator.GetBytes(KeySize);
    }

    /// <summary>
    /// Generates a new master key as a hex string.
    /// </summary>
    public static string GenerateMasterKeyHex()
    {
        return Convert.ToHexString(GenerateMasterKey()).ToLowerInvariant();
    }

    /// <summary>
    /// Encrypts a string value.
    /// </summary>
    public static string Encrypt(string plaintext, byte[] key)
    {
        if (key.Length != KeySize)
        {
            throw new ArgumentException($"Key must be {KeySize} bytes", nameof(key));
        }

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

            // Format: nonce || ciphertext || tag (base64 encoded)
            var result = new byte[NonceSize + ciphertext.Length + TagSize];
            nonce.CopyTo(result, 0);
            ciphertext.CopyTo(result, NonceSize);
            tag.CopyTo(result, NonceSize + ciphertext.Length);

            return Convert.ToBase64String(result);
        }
        finally
        {
            // Zero sensitive plaintext bytes from memory
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    /// <summary>
    /// Decrypts a string value.
    /// </summary>
    public static string Decrypt(string encrypted, byte[] key)
    {
        if (key.Length != KeySize)
        {
            throw new ArgumentException($"Key must be {KeySize} bytes", nameof(key));
        }

        var combined = Convert.FromBase64String(encrypted);

        if (combined.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Invalid encrypted data length");
        }

        var nonce = combined[..NonceSize];
        var tag = combined[^TagSize..];
        var ciphertext = combined[NonceSize..^TagSize];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            // Zero sensitive plaintext bytes from memory
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>
    /// Encrypts an object as JSON.
    /// </summary>
    public static string EncryptObject<T>(T obj, byte[] key)
    {
        var json = JsonSerializer.Serialize(obj);
        return Encrypt(json, key);
    }

    /// <summary>
    /// Decrypts a JSON object.
    /// </summary>
    public static T? DecryptObject<T>(string encrypted, byte[] key)
    {
        var json = Decrypt(encrypted, key);
        return JsonSerializer.Deserialize<T>(json);
    }

    /// <summary>
    /// Checks if a string appears to be encrypted.
    /// </summary>
    public static bool IsEncrypted(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(value);
            return bytes.Length >= NonceSize + TagSize;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Represents an encrypted configuration value.
/// </summary>
public readonly struct EncryptedValue
{
    private readonly string _encryptedData;

    public EncryptedValue(string encryptedData)
    {
        _encryptedData = encryptedData;
    }

    /// <summary>
    /// Creates an encrypted value from plaintext.
    /// </summary>
    public static EncryptedValue FromPlaintext(string plaintext, byte[] key)
    {
        return new EncryptedValue(ConfigurationEncryptor.Encrypt(plaintext, key));
    }

    /// <summary>
    /// Decrypts and returns the value.
    /// </summary>
    public string Decrypt(byte[] key)
    {
        return ConfigurationEncryptor.Decrypt(_encryptedData, key);
    }

    public override string ToString() => _encryptedData;

    public static implicit operator string(EncryptedValue value) => value._encryptedData;
}
