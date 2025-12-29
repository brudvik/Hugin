using FluentAssertions;
using Hugin.Core.Entities;
using Xunit;

namespace Hugin.Core.Tests.Entities;

/// <summary>
/// Tests for the Account entity.
/// </summary>
public class AccountTests
{
    private static Account CreateTestAccount(string name = "TestAccount")
    {
        return new Account(Guid.NewGuid(), name, "argon2id$hash");
    }

    [Fact]
    public void ConstructorInitializesWithDefaultValues()
    {
        // Arrange & Act
        var account = new Account(Guid.NewGuid(), "MyAccount", "passwordhash");

        // Assert
        account.Name.Should().Be("MyAccount");
        account.PasswordHash.Should().Be("passwordhash");
        account.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        account.Email.Should().BeNull();
        account.IsVerified.Should().BeFalse();
        account.IsSuspended.Should().BeFalse();
    }

    [Fact]
    public void SetPasswordUpdatesHash()
    {
        // Arrange
        var account = CreateTestAccount();
        var newHash = "argon2id$v=19$m=65536,t=3,p=1$newhash";

        // Act
        account.SetPassword(newHash);

        // Assert
        account.PasswordHash.Should().Be(newHash);
    }

    [Fact]
    public void AddCertificateFingerprintAddsFingerprint()
    {
        // Arrange
        var account = CreateTestAccount();
        var fingerprint = "SHA256:abc123def456";

        // Act
        account.AddCertificateFingerprint(fingerprint);

        // Assert
        account.CertificateFingerprints.Should().Contain(fingerprint.ToUpperInvariant());
    }

    [Fact]
    public void RemoveCertificateFingerprintRemovesFingerprint()
    {
        // Arrange
        var account = CreateTestAccount();
        account.AddCertificateFingerprint("SHA256:abc123");

        // Act
        account.RemoveCertificateFingerprint("SHA256:abc123");

        // Assert
        account.CertificateFingerprints.Should().BeEmpty();
    }

    [Fact]
    public void VerifyAccountSetsIsVerifiedToTrue()
    {
        // Arrange
        var account = CreateTestAccount();

        // Act
        account.Verify();

        // Assert
        account.IsVerified.Should().BeTrue();
    }

    [Fact]
    public void UpdateLastSeenUpdatesTimestamp()
    {
        // Arrange
        var account = CreateTestAccount();
        var originalTime = account.LastSeenAt;

        // Wait a small amount to ensure time difference
        Thread.Sleep(10);

        // Act
        account.UpdateLastSeen();

        // Assert
        account.LastSeenAt.Should().BeAfter(originalTime ?? DateTimeOffset.MinValue);
        account.LastSeenAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void SetEmailSetsEmailAddress()
    {
        // Arrange
        var account = CreateTestAccount();

        // Act
        account.SetEmail("test@example.com");

        // Assert
        account.Email.Should().Be("test@example.com");
    }

    [Fact]
    public void RegisterNicknameAddsNickname()
    {
        // Arrange
        var account = CreateTestAccount();

        // Act
        account.RegisterNickname("MyNick");

        // Assert
        account.RegisteredNicknames.Should().Contain("MyNick");
    }

    [Fact]
    public void UnregisterNicknameRemovesNickname()
    {
        // Arrange
        var account = CreateTestAccount();
        account.RegisterNickname("MyNick");

        // Act
        account.UnregisterNickname("MyNick");

        // Assert
        account.RegisteredNicknames.Should().BeEmpty();
    }

    [Fact]
    public void SuspendSetsIsSuspendedAndReason()
    {
        // Arrange
        var account = CreateTestAccount();

        // Act
        account.Suspend("Abuse violation");

        // Assert
        account.IsSuspended.Should().BeTrue();
        account.SuspensionReason.Should().Be("Abuse violation");
    }

    [Fact]
    public void UnsuspendClearsSuspension()
    {
        // Arrange
        var account = CreateTestAccount();
        account.Suspend("Some reason");

        // Act
        account.Unsuspend();

        // Assert
        account.IsSuspended.Should().BeFalse();
        account.SuspensionReason.Should().BeNull();
    }

    [Fact]
    public void GrantOperatorSetsOperatorPrivileges()
    {
        // Arrange
        var account = CreateTestAccount();

        // Act
        account.GrantOperator(OperatorPrivileges.LocalOper);

        // Assert
        account.IsOperator.Should().BeTrue();
        account.OperatorPrivileges.Should().Be(OperatorPrivileges.LocalOper);
    }

    [Fact]
    public void RevokeOperatorClearsOperatorPrivileges()
    {
        // Arrange
        var account = CreateTestAccount();
        account.GrantOperator(OperatorPrivileges.GlobalOper);

        // Act
        account.RevokeOperator();

        // Assert
        account.IsOperator.Should().BeFalse();
        account.OperatorPrivileges.Should().Be(OperatorPrivileges.None);
    }
}
