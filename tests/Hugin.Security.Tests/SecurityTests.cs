using FluentAssertions;
using Hugin.Security;
using Xunit;

namespace Hugin.Security.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void HashPasswordReturnsPhcFormatString()
    {
        var hash = PasswordHasher.HashPassword("password123");

        hash.Should().StartWith("$argon2id$");
        hash.Split('$').Should().HaveCount(6);
    }

    [Fact]
    public void VerifyPasswordWithCorrectPasswordReturnsTrue()
    {
        var password = "MySecurePassword123!";
        var hash = PasswordHasher.HashPassword(password);

        var result = PasswordHasher.VerifyPassword(password, hash);

        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyPasswordWithWrongPasswordReturnsFalse()
    {
        var hash = PasswordHasher.HashPassword("correctPassword");

        var result = PasswordHasher.VerifyPassword("wrongPassword", hash);

        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyPasswordWithInvalidHashReturnsFalse()
    {
        var result = PasswordHasher.VerifyPassword("password", "not-a-valid-hash");

        result.Should().BeFalse();
    }

    [Fact]
    public void HashPasswordSamePasswordDifferentHashes()
    {
        var password = "password123";

        var hash1 = PasswordHasher.HashPassword(password);
        var hash2 = PasswordHasher.HashPassword(password);

        hash1.Should().NotBe(hash2); // Different salts
        PasswordHasher.VerifyPassword(password, hash1).Should().BeTrue();
        PasswordHasher.VerifyPassword(password, hash2).Should().BeTrue();
    }

    [Fact]
    public void NeedsRehashWithCurrentParamsReturnsFalse()
    {
        var hash = PasswordHasher.HashPassword("password");

        var result = PasswordHasher.NeedsRehash(hash);

        result.Should().BeFalse();
    }
}

public class ConfigurationEncryptorTests
{
    [Fact]
    public void GenerateMasterKeyReturns32Bytes()
    {
        var key = ConfigurationEncryptor.GenerateMasterKey();

        key.Should().HaveCount(32);
    }

    [Fact]
    public void EncryptDecryptRoundTrip()
    {
        var key = ConfigurationEncryptor.GenerateMasterKey();
        var plaintext = "This is a secret message!";

        var encrypted = ConfigurationEncryptor.Encrypt(plaintext, key);
        var decrypted = ConfigurationEncryptor.Decrypt(encrypted, key);

        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void EncryptDifferentCiphertextEachTime()
    {
        var key = ConfigurationEncryptor.GenerateMasterKey();
        var plaintext = "test";

        var encrypted1 = ConfigurationEncryptor.Encrypt(plaintext, key);
        var encrypted2 = ConfigurationEncryptor.Encrypt(plaintext, key);

        encrypted1.Should().NotBe(encrypted2); // Different nonces
    }

    [Fact]
    public void DecryptWithWrongKeyThrows()
    {
        var key1 = ConfigurationEncryptor.GenerateMasterKey();
        var key2 = ConfigurationEncryptor.GenerateMasterKey();
        var encrypted = ConfigurationEncryptor.Encrypt("test", key1);

        var act = () => ConfigurationEncryptor.Decrypt(encrypted, key2);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void IsEncryptedWithValidEncryptedDataReturnsTrue()
    {
        var key = ConfigurationEncryptor.GenerateMasterKey();
        var encrypted = ConfigurationEncryptor.Encrypt("test", key);

        var result = ConfigurationEncryptor.IsEncrypted(encrypted);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsEncryptedWithPlainTextReturnsFalse()
    {
        var result = ConfigurationEncryptor.IsEncrypted("just plain text");

        result.Should().BeFalse();
    }

    [Fact]
    public void EncryptObjectDecryptObjectRoundTrip()
    {
        var key = ConfigurationEncryptor.GenerateMasterKey();
        var obj = new TestConfig { Name = "Test", Value = 42 };

        var encrypted = ConfigurationEncryptor.EncryptObject(obj, key);
        var decrypted = ConfigurationEncryptor.DecryptObject<TestConfig>(encrypted, key);

        decrypted.Should().NotBeNull();
        decrypted!.Name.Should().Be("Test");
        decrypted.Value.Should().Be(42);
    }

    private sealed class TestConfig
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }
}

public class HostCloakerTests
{
    private readonly HostCloaker _cloaker = new("test-secret", "test.cloak");

    [Fact]
    public void CloakIpAddressReturnsCloakedFormat()
    {
        var cloaked = _cloaker.CloakIpAddress("192.168.1.100");

        cloaked.Should().EndWith(".test.cloak");
        cloaked.Should().NotContain("192");
    }

    [Fact]
    public void CloakIpAddressSameIpSameCloak()
    {
        var cloak1 = _cloaker.CloakIpAddress("192.168.1.100");
        var cloak2 = _cloaker.CloakIpAddress("192.168.1.100");

        cloak1.Should().Be(cloak2);
    }

    [Fact]
    public void CloakIpAddressDifferentIpsDifferentCloaks()
    {
        var cloak1 = _cloaker.CloakIpAddress("192.168.1.100");
        var cloak2 = _cloaker.CloakIpAddress("192.168.1.101");

        cloak1.Should().NotBe(cloak2);
    }

    [Fact]
    public void CloakHostnamePreservesDomain()
    {
        var cloaked = _cloaker.CloakHostname("user.host.example.com");

        cloaked.Should().EndWith(".example.com");
        cloaked.Should().NotContain("user");
        cloaked.Should().NotContain("host");
    }

    [Fact]
    public void CloakAccountReturnsAccountBasedCloak()
    {
        var cloaked = _cloaker.CloakAccount("username");

        cloaked.Should().Be("username.test.cloak");
    }

    [Fact]
    public void CloakIpAddressUsesCloakIpAddress()
    {
        var cloaked = _cloaker.Cloak("192.168.1.1");

        cloaked.Should().EndWith(".test.cloak");
        cloaked.Should().NotContain("192");
    }

    [Fact]
    public void CloakHostnameUsesCloakHostname()
    {
        var cloaked = _cloaker.Cloak("user.example.com");

        cloaked.Should().Contain("example.com");
    }
}

public class RateLimiterTests
{
    [Fact]
    public void TryConsumeConnectionUnderLimitReturnsTrue()
    {
        var config = new RateLimitConfiguration { ConnectionsPerSecond = 10, ConnectionBurstSize = 5 };
        var limiter = new RateLimiter(config);
        var address = System.Net.IPAddress.Parse("192.168.1.1");

        var result = limiter.TryConsumeConnection(address);

        result.Should().BeTrue();
    }

    [Fact]
    public void TryConsumeConnectionOverBurstReturnsFalse()
    {
        var config = new RateLimitConfiguration { ConnectionsPerSecond = 1, ConnectionBurstSize = 2 };
        var limiter = new RateLimiter(config);
        var address = System.Net.IPAddress.Parse("192.168.1.1");

        limiter.TryConsumeConnection(address).Should().BeTrue();
        limiter.TryConsumeConnection(address).Should().BeTrue();
        limiter.TryConsumeConnection(address).Should().BeFalse();
    }

    [Fact]
    public void TryConsumeCommandUnderLimitReturnsTrue()
    {
        var config = new RateLimitConfiguration { CommandsPerSecond = 10, CommandBurstSize = 20 };
        var limiter = new RateLimiter(config);
        var connectionId = Guid.NewGuid();

        var result = limiter.TryConsumeCommand(connectionId, "PRIVMSG");

        result.Should().BeTrue();
    }

    [Fact]
    public void GetRemainingTokensNewConnectionReturnsBurstSize()
    {
        var config = new RateLimitConfiguration { CommandBurstSize = 20 };
        var limiter = new RateLimiter(config);
        var connectionId = Guid.NewGuid();

        var remaining = limiter.GetRemainingTokens(connectionId);

        remaining.Should().Be(20);
    }
}
