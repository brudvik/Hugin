using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Hugin.Security;

/// <summary>
/// Configuration for TLS connections.
/// </summary>
public sealed class TlsConfiguration
{
    /// <summary>
    /// Gets or sets the server certificate.
    /// </summary>
    public X509Certificate2? Certificate { get; set; }

    /// <summary>
    /// Gets or sets the minimum allowed TLS version.
    /// Default is TLS 1.3 for maximum security.
    /// </summary>
    public SslProtocols MinimumProtocolVersion { get; set; } = SslProtocols.Tls13;

    /// <summary>
    /// Gets or sets whether to allow TLS 1.2 connections as a fallback.
    /// Default is false. Only enable this if you must support older clients.
    /// </summary>
    public bool AllowTls12Fallback { get; set; }

    /// <summary>
    /// Gets or sets whether to require client certificates.
    /// </summary>
    public bool RequireClientCertificate { get; set; }

    /// <summary>
    /// Gets or sets whether to check certificate revocation.
    /// </summary>
    public bool CheckCertificateRevocation { get; set; } = true;

    /// <summary>
    /// Gets or sets the certificate validation callback.
    /// </summary>
    public RemoteCertificateValidationCallback? CertificateValidationCallback { get; set; }

    /// <summary>
    /// Creates server SSL options.
    /// </summary>
    public SslServerAuthenticationOptions CreateServerOptions()
    {
        if (Certificate is null)
        {
            throw new InvalidOperationException("Server certificate is required");
        }

        var enabledProtocols = AllowTls12Fallback
            ? SslProtocols.Tls12 | SslProtocols.Tls13
            : SslProtocols.Tls13;

        return new SslServerAuthenticationOptions
        {
            ServerCertificate = Certificate,
            ClientCertificateRequired = RequireClientCertificate,
            EnabledSslProtocols = enabledProtocols,
            CertificateRevocationCheckMode = CheckCertificateRevocation
                ? X509RevocationMode.Online
                : X509RevocationMode.NoCheck,
            RemoteCertificateValidationCallback = CertificateValidationCallback
        };
    }

    /// <summary>
    /// Loads a certificate from a PFX file.
    /// </summary>
    public static X509Certificate2 LoadCertificate(string path, string? password = null)
    {
        return new X509Certificate2(path, password, X509KeyStorageFlags.MachineKeySet);
    }

    /// <summary>
    /// Generates a self-signed certificate for development.
    /// </summary>
    public static X509Certificate2 GenerateSelfSignedCertificate(string subjectName, int validDays = 365)
    {
        using var rsa = RSA.Create(4096);

        var request = new CertificateRequest(
            $"CN={subjectName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // Add extensions
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection
                {
                    new("1.3.6.1.5.5.7.3.1") // Server Authentication
                },
                critical: true));

        // Add SAN
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(subjectName);
        sanBuilder.AddDnsName("localhost");
        request.CertificateExtensions.Add(sanBuilder.Build());

        var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(validDays));

        // Export and re-import to work around platform-specific issues
        return new X509Certificate2(
            certificate.Export(X509ContentType.Pfx),
            (string?)null,
            X509KeyStorageFlags.MachineKeySet);
    }
}

/// <summary>
/// Provides TLS certificate fingerprint utilities.
/// </summary>
public static class CertificateFingerprint
{
    /// <summary>
    /// Gets the SHA-256 fingerprint of a certificate.
    /// </summary>
    public static string GetSha256Fingerprint(X509Certificate2 certificate)
    {
        var hash = certificate.GetCertHash(HashAlgorithmName.SHA256);
        return Convert.ToHexString(hash).ToUpperInvariant();
    }

    /// <summary>
    /// Gets the SHA-256 fingerprint formatted with colons.
    /// </summary>
    public static string GetFormattedFingerprint(X509Certificate2 certificate)
    {
        var hash = certificate.GetCertHash(HashAlgorithmName.SHA256);
        return string.Join(":", hash.Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Compares two fingerprints for equality using constant-time comparison
    /// to prevent timing attacks.
    /// </summary>
    /// <param name="fingerprint1">The first fingerprint.</param>
    /// <param name="fingerprint2">The second fingerprint.</param>
    /// <returns>True if the fingerprints are equal; otherwise false.</returns>
    public static bool AreEqual(string fingerprint1, string fingerprint2)
    {
        // Normalize: remove colons and spaces, convert to uppercase
        var f1 = fingerprint1.Replace(":", "").Replace(" ", "").ToUpperInvariant();
        var f2 = fingerprint2.Replace(":", "").Replace(" ", "").ToUpperInvariant();

        // Different lengths - but still do constant-time return
        if (f1.Length != f2.Length)
        {
            return false;
        }

        // Convert to bytes for constant-time comparison
        try
        {
            var bytes1 = Convert.FromHexString(f1);
            var bytes2 = Convert.FromHexString(f2);
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(bytes1, bytes2);
        }
        catch (FormatException)
        {
            // Invalid hex string - use constant-time comparison on original strings
            return ConstantTimeStringEquals(f1, f2);
        }
    }

    /// <summary>
    /// Performs constant-time string comparison to prevent timing attacks.
    /// </summary>
    private static bool ConstantTimeStringEquals(string a, string b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        var result = 0;
        for (var i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }
        return result == 0;
    }
}
