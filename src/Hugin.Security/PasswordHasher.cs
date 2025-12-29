using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Hugin.Security;

/// <summary>
/// Provides Argon2id password hashing following OWASP recommendations.
/// </summary>
public sealed class PasswordHasher
{
    // OWASP recommended parameters for Argon2id
    private const int DegreeOfParallelism = 4;
    private const int MemorySize = 65536; // 64 MB
    private const int Iterations = 3;
    private const int HashSize = 32;
    private const int SaltSize = 16;

    /// <summary>
    /// Hashes a password using Argon2id.
    /// </summary>
    /// <returns>A PHC string format hash.</returns>
    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = ComputeHash(password, salt);

        return FormatPhcString(salt, hash);
    }

    /// <summary>
    /// Verifies a password against a hash.
    /// </summary>
    public static bool VerifyPassword(string password, string hashedPassword)
    {
        try
        {
            var (salt, expectedHash) = ParsePhcString(hashedPassword);
            var actualHash = ComputeHash(password, salt);

            return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if a hash needs to be upgraded (parameters changed).
    /// </summary>
    public static bool NeedsRehash(string hashedPassword)
    {
        try
        {
            // Parse the PHC string and check parameters
            var parts = hashedPassword.Split('$');
            if (parts.Length < 4 || parts[1] != "argon2id")
            {
                return true;
            }

            var paramPart = parts[3];
            var parameters = paramPart.Split(',')
                .Select(p => p.Split('='))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => p[1]);

            if (!parameters.TryGetValue("m", out var mStr) || !int.TryParse(mStr, out var m) || m != MemorySize)
            {
                return true;
            }

            if (!parameters.TryGetValue("t", out var tStr) || !int.TryParse(tStr, out var t) || t != Iterations)
            {
                return true;
            }

            if (!parameters.TryGetValue("p", out var pStr) || !int.TryParse(pStr, out var p) || p != DegreeOfParallelism)
            {
                return true;
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    private static byte[] ComputeHash(string password, byte[] salt)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                DegreeOfParallelism = DegreeOfParallelism,
                MemorySize = MemorySize,
                Iterations = Iterations
            };

            return argon2.GetBytes(HashSize);
        }
        finally
        {
            // Zero sensitive password bytes from memory
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private static string FormatPhcString(byte[] salt, byte[] hash)
    {
        var saltB64 = Convert.ToBase64String(salt).TrimEnd('=');
        var hashB64 = Convert.ToBase64String(hash).TrimEnd('=');

        return $"$argon2id$v=19$m={MemorySize},t={Iterations},p={DegreeOfParallelism}${saltB64}${hashB64}";
    }

    private static (byte[] salt, byte[] hash) ParsePhcString(string phcString)
    {
        var parts = phcString.Split('$');
        if (parts.Length != 6 || parts[1] != "argon2id")
        {
            throw new FormatException("Invalid PHC string format");
        }

        var saltB64 = parts[4];
        var hashB64 = parts[5];

        // Add padding back for Base64
        var salt = Convert.FromBase64String(PadBase64(saltB64));
        var hash = Convert.FromBase64String(PadBase64(hashB64));

        return (salt, hash);
    }

    private static string PadBase64(string base64)
    {
        var padding = (4 - (base64.Length % 4)) % 4;
        return padding switch
        {
            1 => base64 + "=",
            2 => base64 + "==",
            3 => base64 + "===",
            _ => base64
        };
    }
}
