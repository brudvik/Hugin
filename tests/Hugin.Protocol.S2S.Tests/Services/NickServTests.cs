using FluentAssertions;
using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using Hugin.Protocol.S2S.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Hugin.Protocol.S2S.Tests.Services;

/// <summary>
/// Unit tests for NickServ service.
/// </summary>
public sealed class NickServTests
{
    private readonly Mock<IAccountRepository> _accountRepository;
    private readonly Mock<IServicesManager> _servicesManager;
    private readonly NickServ _nickServ;
    private readonly ServerId _serverId;

    public NickServTests()
    {
        _accountRepository = new Mock<IAccountRepository>();
        _servicesManager = new Mock<IServicesManager>();
        _serverId = ServerId.Create("001", "irc.test.net");
        _nickServ = new NickServ(
            _accountRepository.Object,
            _serverId,
            "services.test.net",
            NullLogger<NickServ>.Instance);
    }

    [Fact]
    public void NicknameIsNickServ()
    {
        _nickServ.Nickname.Should().Be("NickServ");
    }

    [Fact]
    public void IdentIsNickServ()
    {
        _nickServ.Ident.Should().Be("NickServ");
    }

    [Fact]
    public void HostIsServiceHost()
    {
        _nickServ.Host.Should().Be("services.test.net");
    }

    [Fact]
    public void RealnameIsDescriptive()
    {
        _nickServ.Realname.Should().Be("Nickname Registration Service");
    }

    [Fact]
    public void UidStartsWithServerId()
    {
        _nickServ.Uid.Should().StartWith("001");
        _nickServ.Uid.Should().HaveLength(9);
    }

    [Fact]
    public void GetHelpWithNoCommandReturnsGeneralHelp()
    {
        var help = _nickServ.GetHelp(null).ToList();

        help.Should().NotBeEmpty();
        help.Should().Contain(l => l.Contains("NickServ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetHelpWithRegisterCommandReturnsRegisterHelp()
    {
        var help = _nickServ.GetHelp("REGISTER").ToList();

        help.Should().NotBeEmpty();
        help.Should().Contain(l => l.Contains("REGISTER", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetHelpWithIdentifyCommandReturnsIdentifyHelp()
    {
        var help = _nickServ.GetHelp("IDENTIFY").ToList();

        help.Should().NotBeEmpty();
        help.Should().Contain(l => l.Contains("IDENTIFY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HandleHelpCommandReturnsHelpText()
    {
        var context = CreateContext("HELP");

        await _nickServ.HandleMessageAsync(context);

        _servicesManager.Verify(
            s => s.SendNoticeAsync(_nickServ.Uid, "001AAAAAB", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task HandleRegisterWithNoPasswordReturnsError()
    {
        var context = CreateContext("REGISTER");

        await _nickServ.HandleMessageAsync(context);

        VerifyErrorReply("Syntax:");
    }

    [Fact]
    public async Task HandleRegisterWhenAlreadyRegisteredReturnsError()
    {
        var existingAccount = CreateAccount("TestUser");
        _accountRepository.Setup(r => r.GetByNicknameAsync("TestUser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAccount);

        var context = CreateContext("REGISTER password123");

        await _nickServ.HandleMessageAsync(context);

        VerifyErrorReply("already registered");
    }

    [Fact]
    public async Task HandleRegisterWithValidPasswordCreatesAccount()
    {
        _accountRepository.Setup(r => r.GetByNicknameAsync("TestUser", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Account?)null);
        _accountRepository.Setup(r => r.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccount("TestUser"));

        var context = CreateContext("REGISTER password123 test@example.com");

        await _nickServ.HandleMessageAsync(context);

        _accountRepository.Verify(
            r => r.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        VerifySuccessReply("registered");
    }

    [Fact]
    public async Task HandleIdentifyWithNoPasswordReturnsError()
    {
        var context = CreateContext("IDENTIFY");

        await _nickServ.HandleMessageAsync(context);

        VerifyErrorReply("Syntax:");
    }

    [Fact]
    public async Task HandleIdentifyWithNonExistentAccountReturnsError()
    {
        _accountRepository.Setup(r => r.GetByNicknameAsync("TestUser", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Account?)null);

        var context = CreateContext("IDENTIFY password123");

        await _nickServ.HandleMessageAsync(context);

        VerifyErrorReply("not registered");
    }

    [Fact]
    public async Task HandleIdentifyWithWrongPasswordReturnsError()
    {
        var account = CreateAccount("TestUser");
        _accountRepository.Setup(r => r.GetByNicknameAsync("TestUser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _accountRepository.Setup(r => r.ValidatePasswordAsync(account.Id, "wrongpassword", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var context = CreateContext("IDENTIFY wrongpassword");

        await _nickServ.HandleMessageAsync(context);

        VerifyErrorReply("Invalid password");
    }

    [Fact]
    public async Task HandleIdentifyWithCorrectPasswordSucceeds()
    {
        var account = CreateAccount("TestUser");
        _accountRepository.Setup(r => r.GetByNicknameAsync("TestUser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _accountRepository.Setup(r => r.ValidatePasswordAsync(account.Id, "correctpassword", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var context = CreateContext("IDENTIFY correctpassword");

        await _nickServ.HandleMessageAsync(context);

        _accountRepository.Verify(
            r => r.UpdateLastSeenAsync(account.Id, It.IsAny<CancellationToken>()),
            Times.Once);

        VerifySuccessReply("now identified");
    }

    [Fact]
    public async Task HandleInfoWithNoNicknameDefaultsToSourceNick()
    {
        // When no argument is provided, INFO uses source nick
        var account = CreateAccount("TestUser");
        _accountRepository.Setup(r => r.GetByNicknameAsync("TestUser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var context = CreateContext("INFO");

        await _nickServ.HandleMessageAsync(context);

        VerifyReply("Information for");
    }

    [Fact]
    public async Task HandleInfoWithNonExistentAccountReturnsError()
    {
        _accountRepository.Setup(r => r.GetByNicknameAsync("UnknownUser", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Account?)null);

        var context = CreateContext("INFO UnknownUser");

        await _nickServ.HandleMessageAsync(context);

        VerifyErrorReply("not registered");
    }

    [Fact]
    public async Task HandleInfoWithValidAccountReturnsInfo()
    {
        var account = CreateAccount("TestUser");
        _accountRepository.Setup(r => r.GetByNicknameAsync("TestUser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var context = CreateContext("INFO TestUser");

        await _nickServ.HandleMessageAsync(context);

        VerifyReply("Information for");
    }

    [Fact]
    public async Task HandleSetWithNoOptionReturnsError()
    {
        var context = CreateContext("SET", account: "TestUser");

        await _nickServ.HandleMessageAsync(context);

        VerifyErrorReply("Syntax:");
    }

    [Fact]
    public async Task HandleSetWithoutIdentificationReturnsError()
    {
        var context = CreateContext("SET EMAIL new@email.com", account: null);

        await _nickServ.HandleMessageAsync(context);

        VerifyErrorReply("must be identified");
    }

    [Fact]
    public async Task HandleDropWithNoConfirmationReturnsError()
    {
        var context = CreateContext("DROP", account: "TestUser");

        await _nickServ.HandleMessageAsync(context);

        VerifyErrorReply("Syntax:");
    }

    [Fact]
    public async Task HandleDropWithoutIdentificationReturnsError()
    {
        var context = CreateContext("DROP TestUser", account: null);

        await _nickServ.HandleMessageAsync(context);

        VerifyErrorReply("must be identified");
    }

    [Fact]
    public async Task HandleGhostWithNoNicknameReturnsError()
    {
        var context = CreateContext("GHOST");

        await _nickServ.HandleMessageAsync(context);

        VerifyErrorReply("Syntax:");
    }

    [Fact]
    public async Task HandleUnknownCommandReturnsError()
    {
        var context = CreateContext("INVALIDCMD");

        await _nickServ.HandleMessageAsync(context);

        VerifyErrorReply("Unknown command");
    }

    private ServiceMessageContext CreateContext(string message, string? account = null)
    {
        return new ServiceMessageContext(
            sourceUid: "001AAAAAB",
            sourceNick: "TestUser",
            sourceAccount: account,
            message: message,
            services: _servicesManager.Object);
    }

    private static Account CreateAccount(string name)
    {
        return new Account(Guid.NewGuid(), name, "hashedPassword");
    }

    private void VerifyErrorReply(string containsText)
    {
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _nickServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains(containsText, StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    private void VerifySuccessReply(string containsText)
    {
        VerifyReply(containsText);
    }

    private void VerifyReply(string containsText)
    {
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _nickServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains(containsText, StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }
}
