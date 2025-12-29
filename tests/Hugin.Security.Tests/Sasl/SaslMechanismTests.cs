using System.Text;
using FluentAssertions;
using Hugin.Security.Sasl;
using Xunit;

namespace Hugin.Security.Tests.Sasl;

/// <summary>
/// Tests for the PlainMechanism SASL implementation.
/// </summary>
public class PlainMechanismTests
{
    private readonly PlainMechanism _mechanism = new();

    [Fact]
    public void NameReturnsPlain()
    {
        _mechanism.Name.Should().Be("PLAIN");
    }

    [Fact]
    public void RequiresTlsIsTrue()
    {
        _mechanism.RequiresTls.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessAsyncWithValidCredentialsSucceeds()
    {
        // Arrange
        var context = CreateContext(validateCredentials: (user, pass, _) =>
            Task.FromResult(user == "testuser" && pass == "password123"));

        // Format: \0username\0password
        var clientResponse = Encoding.UTF8.GetBytes("\0testuser\0password123");

        // Act
        var result = await _mechanism.ProcessAsync(clientResponse, context);

        // Assert
        result.State.Should().Be(SaslState.Success);
        result.AccountName.Should().Be("testuser");
    }

    [Fact]
    public async Task ProcessAsyncWithAuthorizationIdentityUsesAuthzid()
    {
        // Arrange
        var context = CreateContext(validateCredentials: (user, pass, _) =>
            Task.FromResult(user == "authcid" && pass == "password123"));

        // Format: authzid\0authcid\0password
        var clientResponse = Encoding.UTF8.GetBytes("requestedaccount\0authcid\0password123");

        // Act
        var result = await _mechanism.ProcessAsync(clientResponse, context);

        // Assert
        result.State.Should().Be(SaslState.Success);
        result.AccountName.Should().Be("requestedaccount");
    }

    [Fact]
    public async Task ProcessAsyncWithInvalidCredentialsFails()
    {
        // Arrange
        var context = CreateContext(validateCredentials: (_, _, _) => Task.FromResult(false));
        var clientResponse = Encoding.UTF8.GetBytes("\0testuser\0wrongpassword");

        // Act
        var result = await _mechanism.ProcessAsync(clientResponse, context);

        // Assert
        result.State.Should().Be(SaslState.Failure);
        result.ErrorMessage.Should().Contain("Invalid credentials");
    }

    [Fact]
    public async Task ProcessAsyncWithMissingUsernameFails()
    {
        // Arrange
        var context = CreateContext();
        var clientResponse = Encoding.UTF8.GetBytes("\0\0password"); // Empty username

        // Act
        var result = await _mechanism.ProcessAsync(clientResponse, context);

        // Assert
        result.State.Should().Be(SaslState.Failure);
        result.ErrorMessage.Should().Contain("Missing username");
    }

    [Fact]
    public async Task ProcessAsyncWithInvalidFormatFails()
    {
        // Arrange
        var context = CreateContext();
        var clientResponse = Encoding.UTF8.GetBytes("onlyonepart"); // Not null-separated

        // Act
        var result = await _mechanism.ProcessAsync(clientResponse, context);

        // Assert
        result.State.Should().Be(SaslState.Failure);
        result.ErrorMessage.Should().Contain("Invalid PLAIN format");
    }

    private static SaslContext CreateContext(
        Func<string, string, CancellationToken, Task<bool>>? validateCredentials = null,
        string? certificateFingerprint = null)
    {
        return new SaslContext(
            Guid.NewGuid(),
            certificateFingerprint,
            validateCredentials ?? ((_, _, _) => Task.FromResult(false)),
            (_, _) => Task.FromResult<string?>(null));
    }
}

/// <summary>
/// Tests for the ExternalMechanism SASL implementation.
/// </summary>
public class ExternalMechanismTests
{
    private readonly ExternalMechanism _mechanism = new();

    [Fact]
    public void NameReturnsExternal()
    {
        _mechanism.Name.Should().Be("EXTERNAL");
    }

    [Fact]
    public void RequiresTlsIsTrue()
    {
        _mechanism.RequiresTls.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessAsyncWithValidCertificateSucceeds()
    {
        // Arrange
        var fingerprint = "SHA256:abc123def456";
        var context = CreateContext(
            certificateFingerprint: fingerprint,
            getAccountByFingerprint: (fp, _) =>
                Task.FromResult(fp == fingerprint ? "certuser" : (string?)null));

        // Act
        var result = await _mechanism.ProcessAsync(Array.Empty<byte>(), context);

        // Assert
        result.State.Should().Be(SaslState.Success);
        result.AccountName.Should().Be("certuser");
    }

    [Fact]
    public async Task ProcessAsyncWithMatchingAuthzidSucceeds()
    {
        // Arrange
        var fingerprint = "SHA256:abc123def456";
        var context = CreateContext(
            certificateFingerprint: fingerprint,
            getAccountByFingerprint: (_, _) => Task.FromResult<string?>("certuser"));

        var clientResponse = Encoding.UTF8.GetBytes("certuser"); // Authzid matches account

        // Act
        var result = await _mechanism.ProcessAsync(clientResponse, context);

        // Assert
        result.State.Should().Be(SaslState.Success);
    }

    [Fact]
    public async Task ProcessAsyncWithMismatchedAuthzidFails()
    {
        // Arrange
        var fingerprint = "SHA256:abc123def456";
        var context = CreateContext(
            certificateFingerprint: fingerprint,
            getAccountByFingerprint: (fp, _) => Task.FromResult<string?>("certuser"));

        var clientResponse = Encoding.UTF8.GetBytes("otheraccount"); // Authzid doesn't match

        // Act
        var result = await _mechanism.ProcessAsync(clientResponse, context);

        // Assert
        result.State.Should().Be(SaslState.Failure);
        result.ErrorMessage.Should().Contain("Authorization identity mismatch");
    }

    [Fact]
    public async Task ProcessAsyncWithNoCertificateFails()
    {
        // Arrange
        var context = CreateContext(certificateFingerprint: null);

        // Act
        var result = await _mechanism.ProcessAsync(Array.Empty<byte>(), context);

        // Assert
        result.State.Should().Be(SaslState.Failure);
        result.ErrorMessage.Should().Contain("No client certificate");
    }

    [Fact]
    public async Task ProcessAsyncWithUnregisteredCertificateFails()
    {
        // Arrange
        var context = CreateContext(
            certificateFingerprint: "SHA256:unknown",
            getAccountByFingerprint: (_, _) => Task.FromResult<string?>(null));

        // Act
        var result = await _mechanism.ProcessAsync(Array.Empty<byte>(), context);

        // Assert
        result.State.Should().Be(SaslState.Failure);
        result.ErrorMessage.Should().Contain("Certificate not registered");
    }

    private static SaslContext CreateContext(
        string? certificateFingerprint = null,
        Func<string, CancellationToken, Task<string?>>? getAccountByFingerprint = null)
    {
        return new SaslContext(
            Guid.NewGuid(),
            certificateFingerprint,
            (_, _, _) => Task.FromResult(false),
            getAccountByFingerprint ?? ((_, _) => Task.FromResult<string?>(null)));
    }
}

/// <summary>
/// Tests for the SaslManager.
/// </summary>
public class SaslManagerTests
{
    [Fact]
    public void ConstructorRegistersBuiltInMechanisms()
    {
        // Arrange & Act
        var manager = new SaslManager();

        // Assert
        manager.GetMechanism("PLAIN").Should().NotBeNull();
        manager.GetMechanism("EXTERNAL").Should().NotBeNull();
    }

    [Fact]
    public void GetMechanismIsCaseInsensitive()
    {
        // Arrange
        var manager = new SaslManager();

        // Assert
        manager.GetMechanism("plain").Should().NotBeNull();
        manager.GetMechanism("PLAIN").Should().NotBeNull();
        manager.GetMechanism("Plain").Should().NotBeNull();
    }

    [Fact]
    public void GetMechanismReturnsNullForUnknown()
    {
        // Arrange
        var manager = new SaslManager();

        // Assert
        manager.GetMechanism("UNKNOWN").Should().BeNull();
    }

    [Fact]
    public void GetMechanismListReturnsAvailableMechanisms()
    {
        // Arrange
        var manager = new SaslManager();

        // Act
        var list = manager.GetMechanismList(isSecure: true);

        // Assert
        list.Should().Contain("PLAIN");
        list.Should().Contain("EXTERNAL");
    }

    [Fact]
    public void GetMechanismListFiltersNonSecureMechanisms()
    {
        // Arrange
        var manager = new SaslManager();

        // Act - PLAIN and EXTERNAL both require TLS
        var list = manager.GetMechanismList(isSecure: false);

        // Assert - Both require TLS, so should be empty or only contain non-TLS mechanisms
        // Since both built-in mechanisms require TLS, list should be empty
        list.Should().BeEmpty();
    }

    [Fact]
    public void RegisterAddsCustomMechanism()
    {
        // Arrange
        var manager = new SaslManager();
        var customMechanism = new TestMechanism();

        // Act
        manager.Register(customMechanism);

        // Assert
        manager.GetMechanism("TEST").Should().Be(customMechanism);
    }

    [Fact]
    public void GetMechanismNamesReturnsAllNames()
    {
        // Arrange
        var manager = new SaslManager();

        // Act
        var names = manager.GetMechanismNames().ToList();

        // Assert
        names.Should().Contain("PLAIN");
        names.Should().Contain("EXTERNAL");
    }

    private sealed class TestMechanism : ISaslMechanism
    {
        public string Name => "TEST";
        public bool RequiresTls => false;

        public Task<SaslStepResult> ProcessAsync(byte[] clientResponse, SaslContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SaslStepResult.Success("testaccount"));
        }
    }
}

/// <summary>
/// Tests for SaslStepResult.
/// </summary>
public class SaslStepResultTests
{
    [Fact]
    public void SuccessCreatesResultWithSuccessState()
    {
        // Act
        var result = SaslStepResult.Success("account");

        // Assert
        result.State.Should().Be(SaslState.Success);
        result.AccountName.Should().Be("account");
        result.Challenge.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void FailureCreatesResultWithFailureState()
    {
        // Act
        var result = SaslStepResult.Failure("error message");

        // Assert
        result.State.Should().Be(SaslState.Failure);
        result.ErrorMessage.Should().Be("error message");
        result.AccountName.Should().BeNull();
    }

    [Fact]
    public void ContinueCreatesResultWithContinueState()
    {
        // Arrange
        var challenge = new byte[] { 1, 2, 3 };

        // Act
        var result = SaslStepResult.Continue(challenge);

        // Assert
        result.State.Should().Be(SaslState.Continue);
        result.Challenge.Should().BeEquivalentTo(challenge);
    }
}

/// <summary>
/// Tests for SaslContext.
/// </summary>
public class SaslContextTests
{
    [Fact]
    public void ConstructorSetsAllProperties()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        var fingerprint = "SHA256:test";
        Func<string, string, CancellationToken, Task<bool>> validateCreds = (_, _, _) => Task.FromResult(true);
        Func<string, CancellationToken, Task<string?>> getAccount = (_, _) => Task.FromResult<string?>("test");

        // Act
        var context = new SaslContext(connectionId, fingerprint, validateCreds, getAccount);

        // Assert
        context.ConnectionId.Should().Be(connectionId);
        context.CertificateFingerprint.Should().Be(fingerprint);
        context.ValidateCredentials.Should().Be(validateCreds);
        context.GetAccountByFingerprint.Should().Be(getAccount);
    }
}
