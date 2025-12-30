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
/// Unit tests for BotServ service.
/// </summary>
public sealed class BotServTests
{
    private readonly Mock<IBotRepository> _botRepository;
    private readonly Mock<IChannelBotRepository> _channelBotRepository;
    private readonly Mock<IRegisteredChannelRepository> _channelRepository;
    private readonly Mock<IAccountRepository> _accountRepository;
    private readonly Mock<IServicesManager> _servicesManager;
    private readonly BotServ _botServ;
    private readonly ServerId _serverId;

    public BotServTests()
    {
        _botRepository = new Mock<IBotRepository>();
        _channelBotRepository = new Mock<IChannelBotRepository>();
        _channelRepository = new Mock<IRegisteredChannelRepository>();
        _accountRepository = new Mock<IAccountRepository>();
        _servicesManager = new Mock<IServicesManager>();
        _serverId = ServerId.Create("001", "irc.test.net");

        _botServ = new BotServ(
            () => _botRepository.Object,
            () => _channelBotRepository.Object,
            () => _channelRepository.Object,
            () => _accountRepository.Object,
            _serverId,
            "services.test.net",
            NullLogger<BotServ>.Instance);
    }

    [Fact]
    public void NicknameIsBotServ()
    {
        _botServ.Nickname.Should().Be("BotServ");
    }

    [Fact]
    public void IdentIsBotServ()
    {
        _botServ.Ident.Should().Be("BotServ");
    }

    [Fact]
    public void HostIsServiceHost()
    {
        _botServ.Host.Should().Be("services.test.net");
    }

    [Fact]
    public void RealnameIsDescriptive()
    {
        _botServ.Realname.Should().Be("Bot Hosting Service");
    }

    [Fact]
    public void UidStartsWithServerId()
    {
        _botServ.Uid.Should().StartWith("001");
        _botServ.Uid.Should().HaveLength(9);
    }

    [Fact]
    public void GetHelpWithNoCommandReturnsGeneralHelp()
    {
        var help = _botServ.GetHelp(null).ToList();

        help.Should().NotBeEmpty();
        help.Should().Contain(l => l.Contains("BotServ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetHelpWithBotListCommandReturnsBotListHelp()
    {
        var help = _botServ.GetHelp("BOTLIST").ToList();

        help.Should().NotBeEmpty();
        help.Should().Contain(l => l.Contains("BOTLIST", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HandleBotListCommandShowsAvailableBots()
    {
        // Arrange
        var context = CreateContext("BOTLIST");
        var bots = new List<Bot>
        {
            new Bot(Guid.NewGuid(), "FriendlyBot", "bot", "A friendly bot", "001AAAAA1"),
            new Bot(Guid.NewGuid(), "HelperBot", "bot", "A helpful bot", "001AAAAA2")
        };

        _botRepository.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(bots);

        // Act
        await _botServ.HandleMessageAsync(context);

        // Assert
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _botServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("FriendlyBot")),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _botServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("HelperBot")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleBotListCommandWithNoBotsShowsEmpty()
    {
        // Arrange
        var context = CreateContext("BOTLIST");
        _botRepository.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Bot>());

        // Act
        await _botServ.HandleMessageAsync(context);

        // Assert
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _botServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("No bots")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAssignCommandWithoutAuthenticationFails()
    {
        // Arrange
        var context = CreateContext("ASSIGN #test FriendlyBot", sourceAccount: null);

        // Act
        await _botServ.HandleMessageAsync(context);

        // Assert
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _botServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("identified")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAssignCommandWithInsufficientParametersFails()
    {
        // Arrange
        var context = CreateContext("ASSIGN #test", sourceAccount: "user");

        // Act
        await _botServ.HandleMessageAsync(context);

        // Assert
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _botServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("Syntax")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAssignCommandToUnregisteredChannelFails()
    {
        // Arrange
        var context = CreateContext("ASSIGN #test FriendlyBot", sourceAccount: "user");
        
        var account = new Account(Guid.NewGuid(), "user", "hashed_password");
        _accountRepository.Setup(r => r.GetByNameAsync("user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        
        _channelRepository.Setup(r => r.GetByNameAsync("#test", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegisteredChannel?)null);

        // Act
        await _botServ.HandleMessageAsync(context);

        // Assert
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _botServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("not registered")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAssignCommandByNonFounderFails()
    {
        // Arrange
        var founderId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var context = CreateContext("ASSIGN #test FriendlyBot", sourceAccount: "user");
        
        var account = new Account(userId, "user", "hashed_password");
        _accountRepository.Setup(r => r.GetByNameAsync("user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        
        var channel = new RegisteredChannel(Guid.NewGuid(), "#test", founderId);
        _channelRepository.Setup(r => r.GetByNameAsync("#test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel);

        // Act
        await _botServ.HandleMessageAsync(context);

        // Assert
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _botServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("Access denied")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAssignCommandWithNonExistentBotFails()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var context = CreateContext("ASSIGN #test UnknownBot", sourceAccount: "user");
        
        var account = new Account(userId, "user", "hashed_password");
        _accountRepository.Setup(r => r.GetByNameAsync("user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        
        var channel = new RegisteredChannel(Guid.NewGuid(), "#test", userId);
        _channelRepository.Setup(r => r.GetByNameAsync("#test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel);

        _botRepository.Setup(r => r.GetByNicknameAsync("UnknownBot", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Bot?)null);

        // Act
        await _botServ.HandleMessageAsync(context);

        // Assert
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _botServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("not found")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAssignCommandSucceedsWithValidParameters()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var botId = Guid.NewGuid();
        var context = CreateContext("ASSIGN #test FriendlyBot", sourceAccount: "user");
        
        var account = new Account(userId, "user", "hashed_password");
        _accountRepository.Setup(r => r.GetByNameAsync("user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        
        var channel = new RegisteredChannel(Guid.NewGuid(), "#test", userId);
        _channelRepository.Setup(r => r.GetByNameAsync("#test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel);

        var bot = new Bot(botId, "FriendlyBot", "bot", "A friendly bot", "001AAAAA1");
        _botRepository.Setup(r => r.GetByNicknameAsync("FriendlyBot", It.IsAny<CancellationToken>()))
            .ReturnsAsync(bot);

        _channelBotRepository.Setup(r => r.GetAssignmentAsync(botId, "#test", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChannelBot?)null);

        var assignment = new ChannelBot(Guid.NewGuid(), botId, "#test", userId);
        _channelBotRepository.Setup(r => r.AssignAsync(botId, "#test", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignment);

        // Act
        await _botServ.HandleMessageAsync(context);

        // Assert
        _channelBotRepository.Verify(
            r => r.AssignAsync(botId, "#test", userId, It.IsAny<CancellationToken>()),
            Times.Once);

        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _botServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("assigned")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleUnassignCommandSucceedsWithValidParameters()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var botId = Guid.NewGuid();
        var context = CreateContext("UNASSIGN #test FriendlyBot", sourceAccount: "user");
        
        var account = new Account(userId, "user", "hashed_password");
        _accountRepository.Setup(r => r.GetByNameAsync("user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        
        var channel = new RegisteredChannel(Guid.NewGuid(), "#test", userId);
        _channelRepository.Setup(r => r.GetByNameAsync("#test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(channel);

        var bot = new Bot(botId, "FriendlyBot", "bot", "A friendly bot", "001AAAAA1");
        _botRepository.Setup(r => r.GetByNicknameAsync("FriendlyBot", It.IsAny<CancellationToken>()))
            .ReturnsAsync(bot);

        _channelBotRepository.Setup(r => r.UnassignAsync(botId, "#test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _botServ.HandleMessageAsync(context);

        // Assert
        _channelBotRepository.Verify(
            r => r.UnassignAsync(botId, "#test", It.IsAny<CancellationToken>()),
            Times.Once);

        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _botServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("removed")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleInfoCommandShowsAssignedBots()
    {
        // Arrange
        var botId = Guid.NewGuid();
        var context = CreateContext("INFO #test");
        
        var assignment = new ChannelBot(Guid.NewGuid(), botId, "#test", Guid.NewGuid())
        {
            AutoGreet = true,
            GreetMessage = "Welcome!"
        };

        _channelBotRepository.Setup(r => r.GetByChannelAsync("#test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChannelBot> { assignment });

        var bot = new Bot(botId, "FriendlyBot", "bot", "A friendly bot", "001AAAAA1");
        _botRepository.Setup(r => r.GetByIdAsync(botId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bot);

        // Act
        await _botServ.HandleMessageAsync(context);

        // Assert
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _botServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("FriendlyBot")),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _botServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("Welcome!")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleSayCommandWithoutAuthenticationFails()
    {
        // Arrange
        var context = CreateContext("SAY #test FriendlyBot Hello!", sourceAccount: null);

        // Act
        await _botServ.HandleMessageAsync(context);

        // Assert
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _botServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("identified")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleUnknownCommandShowsError()
    {
        // Arrange
        var context = CreateContext("INVALID");

        // Act
        await _botServ.HandleMessageAsync(context);

        // Assert
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _botServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("Unknown command")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private ServiceMessageContext CreateContext(string message, string? sourceAccount = null, bool isOperator = false)
    {
        return new ServiceMessageContext(
            "001AAAAAB",
            "TestNick",
            sourceAccount,
            message,
            _servicesManager.Object,
            isOperator);
    }
}

