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
/// Unit tests for MemoServ service.
/// </summary>
public sealed class MemoServTests
{
    private readonly Mock<IMemoRepository> _memoRepository;
    private readonly Mock<IAccountRepository> _accountRepository;
    private readonly Mock<IServicesManager> _servicesManager;
    private readonly MemoServ _memoServ;
    private readonly ServerId _serverId;

    public MemoServTests()
    {
        _memoRepository = new Mock<IMemoRepository>();
        _accountRepository = new Mock<IAccountRepository>();
        _servicesManager = new Mock<IServicesManager>();
        _serverId = ServerId.Create("001", "irc.test.net");

        _memoServ = new MemoServ(
            () => _memoRepository.Object,
            () => _accountRepository.Object,
            _serverId,
            "services.test.net",
            NullLogger<MemoServ>.Instance);
    }

    [Fact]
    public void NicknameIsMemoServ()
    {
        _memoServ.Nickname.Should().Be("MemoServ");
    }

    [Fact]
    public void IdentIsMemoServ()
    {
        _memoServ.Ident.Should().Be("MemoServ");
    }

    [Fact]
    public void HostIsServiceHost()
    {
        _memoServ.Host.Should().Be("services.test.net");
    }

    [Fact]
    public void RealnameIsDescriptive()
    {
        _memoServ.Realname.Should().Be("Memo/Offline Message Service");
    }

    [Fact]
    public void UidStartsWithServerId()
    {
        _memoServ.Uid.Should().StartWith("001");
        _memoServ.Uid.Should().HaveLength(9);
    }

    [Fact]
    public void GetHelpWithNoCommandReturnsGeneralHelp()
    {
        var help = _memoServ.GetHelp(null).ToList();

        help.Should().NotBeEmpty();
        help.Should().Contain(l => l.Contains("MemoServ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetHelpWithSendCommandReturnsSendHelp()
    {
        var help = _memoServ.GetHelp("SEND").ToList();

        help.Should().NotBeEmpty();
        help.Should().Contain(l => l.Contains("SEND", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetHelpWithListCommandReturnsListHelp()
    {
        var help = _memoServ.GetHelp("LIST").ToList();

        help.Should().NotBeEmpty();
        help.Should().Contain(l => l.Contains("LIST", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HandleSendCommandWithoutAuthenticationFails()
    {
        // Arrange
        var context = CreateContext("SEND TestUser Hello there", sourceAccount: null);

        // Act
        await _memoServ.HandleMessageAsync(context);

        // Assert
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _memoServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("identified")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleSendCommandWithInsufficientParametersFails()
    {
        // Arrange
        var context = CreateContext("SEND TestUser", sourceAccount: "sender");

        // Act
        await _memoServ.HandleMessageAsync(context);

        // Assert
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _memoServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("Syntax")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleSendCommandToUnregisteredUserFails()
    {
        // Arrange
        var context = CreateContext("SEND UnknownUser Hello", sourceAccount: "sender");
        
        var senderAccount = new Account(Guid.NewGuid(), "sender", "hashed_password");
        _accountRepository.Setup(r => r.GetByNameAsync("sender", It.IsAny<CancellationToken>()))
            .ReturnsAsync(senderAccount);
        
        _accountRepository.Setup(r => r.GetByNicknameAsync("UnknownUser", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Account?)null);

        // Act
        await _memoServ.HandleMessageAsync(context);

        // Assert
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _memoServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("not registered")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleSendCommandToSelfFails()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var context = CreateContext("SEND TestUser Hello", sourceAccount: "sender");
        
        var account = new Account(accountId, "sender", "hashed_password");
        _accountRepository.Setup(r => r.GetByNameAsync("sender", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        
        _accountRepository.Setup(r => r.GetByNicknameAsync("TestUser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        // Act
        await _memoServ.HandleMessageAsync(context);

        // Assert
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _memoServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("yourself")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleSendCommandSucceedsWithValidParameters()
    {
        // Arrange
        var senderId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var context = CreateContext("SEND TestUser Hello there, how are you?", sourceAccount: "sender");
        
        var senderAccount = new Account(senderId, "sender", "hashed_password");
        var recipientAccount = new Account(recipientId, "recipient", "hashed_password");
        
        _accountRepository.Setup(r => r.GetByNameAsync("sender", It.IsAny<CancellationToken>()))
            .ReturnsAsync(senderAccount);
        
        _accountRepository.Setup(r => r.GetByNicknameAsync("TestUser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(recipientAccount);

        var memo = new Memo(Guid.NewGuid(), senderId, "SenderNick", recipientId, "Hello there, how are you?", DateTimeOffset.UtcNow);
        _memoRepository.Setup(r => r.CreateAsync(
                senderId,
                It.IsAny<string>(),
                recipientId,
                "Hello there, how are you?",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(memo);

        // Act
        await _memoServ.HandleMessageAsync(context);

        // Assert
        _memoRepository.Verify(
            r => r.CreateAsync(
                senderId,
                It.IsAny<string>(),
                recipientId,
                "Hello there, how are you?",
                It.IsAny<CancellationToken>()),
            Times.Once);

        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _memoServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("sent")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleSendCommandRejectsTooLongMessage()
    {
        // Arrange
        var longMessage = new string('X', 1001);
        var context = CreateContext($"SEND TestUser {longMessage}", sourceAccount: "sender");
        
        var senderAccount = new Account(Guid.NewGuid(), "sender", "hashed_password");
        _accountRepository.Setup(r => r.GetByNameAsync("sender", It.IsAny<CancellationToken>()))
            .ReturnsAsync(senderAccount);

        // Act
        await _memoServ.HandleMessageAsync(context);

        // Assert
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _memoServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("too long")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleListCommandWithoutAuthenticationFails()
    {
        // Arrange
        var context = CreateContext("LIST", sourceAccount: null);

        // Act
        await _memoServ.HandleMessageAsync(context);

        // Assert
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _memoServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("identified")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleListCommandWithNoMemosShowsEmpty()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var context = CreateContext("LIST", sourceAccount: "testuser");
        
        var account = new Account(accountId, "testuser", "hashed_password");
        _accountRepository.Setup(r => r.GetByNameAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        
        _memoRepository.Setup(r => r.GetByRecipientAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Memo>());

        // Act
        await _memoServ.HandleMessageAsync(context);

        // Assert
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _memoServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("no memos")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleListCommandShowsMemosWithPreview()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var context = CreateContext("LIST", sourceAccount: "testuser");
        
        var account = new Account(accountId, "testuser", "hashed_password");
        _accountRepository.Setup(r => r.GetByNameAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        
        var memos = new List<Memo>
        {
            new Memo(Guid.NewGuid(), Guid.NewGuid(), "Alice", accountId, "First memo", DateTimeOffset.UtcNow.AddHours(-2)),
            new Memo(Guid.NewGuid(), Guid.NewGuid(), "Bob", accountId, "Second memo", DateTimeOffset.UtcNow.AddHours(-1))
        };
        
        _memoRepository.Setup(r => r.GetByRecipientAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(memos);

        // Act
        await _memoServ.HandleMessageAsync(context);

        // Assert - should show count and each memo
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _memoServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("2 memo")),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _memoServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("Alice")),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _memoServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("Bob")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleReadCommandWithoutAuthenticationFails()
    {
        // Arrange
        var context = CreateContext("READ 1", sourceAccount: null);

        // Act
        await _memoServ.HandleMessageAsync(context);

        // Assert
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _memoServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("identified")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleDeleteCommandWithoutAuthenticationFails()
    {
        // Arrange
        var context = CreateContext("DEL 1", sourceAccount: null);

        // Act
        await _memoServ.HandleMessageAsync(context);

        // Assert
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _memoServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("identified")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleClearCommandWithoutAuthenticationFails()
    {
        // Arrange
        var context = CreateContext("CLEAR", sourceAccount: null);

        // Act
        await _memoServ.HandleMessageAsync(context);

        // Assert
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _memoServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("identified")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleClearCommandDeletesAllMemos()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var context = CreateContext("CLEAR", sourceAccount: "testuser");
        
        var account = new Account(accountId, "testuser", "hashed_password");
        _accountRepository.Setup(r => r.GetByNameAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        
        _memoRepository.Setup(r => r.DeleteAllByRecipientAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        // Act
        await _memoServ.HandleMessageAsync(context);

        // Assert
        _memoRepository.Verify(
            r => r.DeleteAllByRecipientAsync(accountId, It.IsAny<CancellationToken>()),
            Times.Once);

        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _memoServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains('5')),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleUnknownCommandShowsError()
    {
        // Arrange
        var context = CreateContext("INVALID");

        // Act
        await _memoServ.HandleMessageAsync(context);

        // Assert
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _memoServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("Unknown command")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private ServiceMessageContext CreateContext(string message, string? sourceAccount = null)
    {
        return new ServiceMessageContext(
            "001AAAAAB",
            "TestNick",
            sourceAccount,
            message,
            _servicesManager.Object,
            false);
    }
}

