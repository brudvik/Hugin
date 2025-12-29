using System.Net;
using FluentAssertions;
using Hugin.Core.Entities;
using Hugin.Core.Enums;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using Hugin.Protocol;
using Hugin.Protocol.Commands;
using Hugin.Protocol.Commands.Handlers;
using Xunit;

namespace Hugin.Protocol.Tests.Commands;

/// <summary>
/// Test fixtures and mocks for command handler testing.
/// </summary>
public class MockClientConnection : IClientConnection
{
    public Guid ConnectionId { get; set; } = Guid.NewGuid();
    public bool IsActive { get; set; } = true;
    public bool IsSecure { get; set; }
    public string? CertificateFingerprint { get; set; }
    public EndPoint? RemoteEndPoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 12345);

    public List<byte[]> SentData { get; } = new();
    public bool IsClosed { get; private set; }

    public ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        SentData.Add(data.ToArray());
        return ValueTask.CompletedTask;
    }

    public ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        IsClosed = true;
        IsActive = false;
        return ValueTask.CompletedTask;
    }
}

public class MockUserRepository : IUserRepository
{
    private readonly Dictionary<Guid, User> _users = new();
    private readonly Dictionary<string, User> _nicknames = new(StringComparer.OrdinalIgnoreCase);
    private int _maxCount;

    public User? GetByConnectionId(Guid connectionId) => _users.GetValueOrDefault(connectionId);
    public User? GetByNickname(Nickname nickname) => _nicknames.GetValueOrDefault(nickname.Value);
    public IEnumerable<User> GetAll() => _users.Values;
    public IEnumerable<User> FindByHostmask(Hostmask pattern) => _users.Values;
    public IEnumerable<User> GetUsersInChannel(ChannelName channelName) => _users.Values.Where(u => u.Channels.ContainsKey(channelName));
    public IEnumerable<User> GetByAccount(string accountName) => _users.Values.Where(u => u.Account == accountName);
    public bool IsNicknameInUse(Nickname nickname) => _nicknames.ContainsKey(nickname.Value);

    public void Add(User user)
    {
        _users[user.ConnectionId] = user;
        if (user.Nickname is not null)
        {
            _nicknames[user.Nickname.Value] = user;
        }
        _maxCount = Math.Max(_maxCount, _users.Count);
    }

    public void Remove(Guid connectionId)
    {
        if (_users.TryGetValue(connectionId, out var user))
        {
            _users.Remove(connectionId);
            if (user.Nickname is not null)
            {
                _nicknames.Remove(user.Nickname.Value);
            }
        }
    }

    public int GetCount() => _users.Count;
    public int GetInvisibleCount() => _users.Values.Count(u => u.Modes.HasFlag(UserMode.Invisible));
    public int GetOperatorCount() => _users.Values.Count(u => u.IsOperator);
    public IEnumerable<User> GetByServer(ServerId serverId) => _users.Values.Where(u => u.Server.Equals(serverId));
    public int GetMaxUserCount() => _maxCount;

    public void UpdateNickname(User user, Nickname oldNick, Nickname newNick)
    {
        if (oldNick is not null)
        {
            _nicknames.Remove(oldNick.Value);
        }
        _nicknames[newNick.Value] = user;
    }
}

public class MockChannelRepository : IChannelRepository
{
    private readonly Dictionary<string, Channel> _channels = new(StringComparer.OrdinalIgnoreCase);

    public Channel? GetByName(ChannelName name) => _channels.GetValueOrDefault(name.Value);
    public IEnumerable<Channel> GetAll() => _channels.Values;
    public IEnumerable<Channel> Search(string pattern) => _channels.Values;
    public bool Exists(ChannelName name) => _channels.ContainsKey(name.Value);

    public Channel Create(ChannelName name)
    {
        var channel = new Channel(name);
        _channels[name.Value] = channel;
        return channel;
    }

    public void Add(Channel channel) => _channels[channel.Name.Value] = channel;
    public void Remove(ChannelName name) => _channels.Remove(name.Value);
    public IEnumerable<Channel> GetChannelsForUser(Guid connectionId) => 
        _channels.Values.Where(c => c.HasMember(connectionId));
    public int GetCount() => _channels.Count;
    public IEnumerable<Channel> GetVisibleChannels(User? user) => _channels.Values;
}

public class MockMessageBroker : IMessageBroker
{
    public List<(Guid ConnectionId, string Message)> ConnectionMessages { get; } = new();
    public List<(string ChannelName, string Message, Guid? Except)> ChannelMessages { get; } = new();
    public List<string> Broadcasts { get; } = new();

    public ValueTask SendToConnectionAsync(Guid connectionId, string message, CancellationToken cancellationToken = default)
    {
        ConnectionMessages.Add((connectionId, message));
        return ValueTask.CompletedTask;
    }

    public ValueTask SendToConnectionsAsync(IEnumerable<Guid> connectionIds, string message, CancellationToken cancellationToken = default)
    {
        foreach (var id in connectionIds)
        {
            ConnectionMessages.Add((id, message));
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask SendToChannelAsync(string channelName, string message, Guid? exceptConnectionId = null, CancellationToken cancellationToken = default)
    {
        ChannelMessages.Add((channelName, message, exceptConnectionId));
        return ValueTask.CompletedTask;
    }

    public ValueTask SendToChannelsAsync(IEnumerable<string> channelNames, string message, Guid? exceptConnectionId = null, CancellationToken cancellationToken = default)
    {
        foreach (var channelName in channelNames)
        {
            ChannelMessages.Add((channelName, message, exceptConnectionId));
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask BroadcastAsync(string message, CancellationToken cancellationToken = default)
    {
        Broadcasts.Add(message);
        return ValueTask.CompletedTask;
    }

    public ValueTask SendToOperatorsAsync(string message, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask SendToServerAsync(string serverId, string message, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Helper class to build test contexts for command handlers.
/// </summary>
public class CommandContextBuilder
{
    private static readonly ServerId DefaultServerId = ServerId.Create("001", "test.server.com");
    private const string DefaultServerName = "test.server.com";

    public MockUserRepository Users { get; } = new();
    public MockChannelRepository Channels { get; } = new();
    public MockMessageBroker Broker { get; } = new();
    public MockClientConnection Connection { get; } = new();
    public CapabilityManager Capabilities { get; } = new();
    public User User { get; }

    public CommandContextBuilder(string? nickname = null)
    {
        User = new User(
            Connection.ConnectionId,
            IPAddress.Parse("192.168.1.100"),
            "client.example.com",
            DefaultServerId,
            isSecure: false);

        if (nickname is not null)
        {
            Nickname.TryCreate(nickname, out var nick, out _);
            User.SetNickname(nick!);
        }

        Users.Add(User);
    }

    public CommandContext Build(IrcMessage message)
    {
        return new CommandContext(
            message,
            User,
            Connection,
            Users,
            Channels,
            Broker,
            Capabilities,
            DefaultServerName,
            DefaultServerId,
            _ => null);
    }

    public void SetUserRegistered()
    {
        User.SetUserInfo("testuser", "Test User");
        User.SetRegistrationState(RegistrationState.Registered);
    }
}

/// <summary>
/// Tests for the NickHandler.
/// </summary>
public class NickHandlerTests
{
    private readonly NickHandler _handler = new();

    [Fact]
    public void CommandReturnsNick()
    {
        _handler.Command.Should().Be("NICK");
    }

    [Fact]
    public void MinimumParametersIsOne()
    {
        _handler.MinimumParameters.Should().Be(1);
    }

    [Fact]
    public void RequiresRegistrationIsFalse()
    {
        _handler.RequiresRegistration.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsyncWithValidNickSetsNickname()
    {
        // Arrange
        var builder = new CommandContextBuilder();
        var message = IrcMessage.Create("NICK", "TestNick");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.User.Nickname.Value.Should().Be("TestNick");
    }

    [Fact]
    public async Task HandleAsyncWithInvalidNickSendsErroneusNickname()
    {
        // Arrange
        var builder = new CommandContextBuilder();
        var message = IrcMessage.Create("NICK", "123Invalid"); // Starts with number
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().ContainSingle();
        builder.Broker.ConnectionMessages[0].Message.Should().Contain("432"); // ERR_ERRONEUSNICKNAME
    }

    [Fact]
    public async Task HandleAsyncWithNickInUseSendsNicknameInUse()
    {
        // Arrange
        var builder = new CommandContextBuilder("ExistingNick");
        builder.SetUserRegistered();

        // Add another user with the target nickname
        var otherUser = new User(
            Guid.NewGuid(),
            IPAddress.Parse("10.0.0.1"),
            "other.example.com",
            ServerId.Create("001", "test.server.com"),
            isSecure: false);
        Nickname.TryCreate("TakenNick", out var takenNick, out _);
        otherUser.SetNickname(takenNick!);
        builder.Users.Add(otherUser);

        var message = IrcMessage.Create("NICK", "TakenNick");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().ContainSingle();
        builder.Broker.ConnectionMessages[0].Message.Should().Contain("433"); // ERR_NICKNAMEINUSE
    }

    [Fact]
    public async Task HandleAsyncUpdatesRegistrationStateFromNone()
    {
        // Arrange
        var builder = new CommandContextBuilder();
        var message = IrcMessage.Create("NICK", "TestNick");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.User.RegistrationState.Should().Be(RegistrationState.NickReceived);
    }
}

/// <summary>
/// Tests for the UserHandler.
/// </summary>
public class UserHandlerTests
{
    private readonly UserHandler _handler = new();

    [Fact]
    public void CommandReturnsUser()
    {
        _handler.Command.Should().Be("USER");
    }

    [Fact]
    public void MinimumParametersIsFour()
    {
        _handler.MinimumParameters.Should().Be(4);
    }

    [Fact]
    public async Task HandleAsyncSetsUserInfo()
    {
        // Arrange
        var builder = new CommandContextBuilder();
        var message = IrcMessage.Create("USER", "testuser", "0", "*", "Real Name");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.User.Username.Should().Be("testuser");
        builder.User.RealName.Should().Be("Real Name");
    }

    [Fact]
    public async Task HandleAsyncTruncatesLongUsername()
    {
        // Arrange
        var builder = new CommandContextBuilder();
        var message = IrcMessage.Create("USER", "verylongusernamethatexceedslimit", "0", "*", "Real Name");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.User.Username.Length.Should().BeLessOrEqualTo(10);
    }

    [Fact]
    public async Task HandleAsyncRejectsWhenAlreadyRegistered()
    {
        // Arrange
        var builder = new CommandContextBuilder("TestNick");
        builder.SetUserRegistered();
        var message = IrcMessage.Create("USER", "testuser", "0", "*", "Real Name");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().ContainSingle();
        builder.Broker.ConnectionMessages[0].Message.Should().Contain("462"); // ERR_ALREADYREGISTERED
    }

    [Fact]
    public async Task HandleAsyncUpdatesRegistrationStateFromNone()
    {
        // Arrange
        var builder = new CommandContextBuilder();
        var message = IrcMessage.Create("USER", "testuser", "0", "*", "Real Name");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.User.RegistrationState.Should().Be(RegistrationState.UserReceived);
    }
}

/// <summary>
/// Tests for the PingHandler.
/// </summary>
public class PingHandlerTests
{
    private readonly PingHandler _handler = new();

    [Fact]
    public void CommandReturnsPing()
    {
        _handler.Command.Should().Be("PING");
    }

    [Fact]
    public async Task HandleAsyncSendsPongResponse()
    {
        // Arrange
        var builder = new CommandContextBuilder("TestNick");
        var message = IrcMessage.Create("PING", "token123");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().ContainSingle();
        builder.Broker.ConnectionMessages[0].Message.Should().Contain("PONG");
        builder.Broker.ConnectionMessages[0].Message.Should().Contain("token123");
    }
}

/// <summary>
/// Tests for the PongHandler.
/// </summary>
public class PongHandlerTests
{
    private readonly PongHandler _handler = new();

    [Fact]
    public void CommandReturnsPong()
    {
        _handler.Command.Should().Be("PONG");
    }

    [Fact]
    public async Task HandleAsyncUpdatesLastActivity()
    {
        // Arrange
        var builder = new CommandContextBuilder("TestNick");
        var previousActivity = builder.User.LastActivity;
        await Task.Delay(10); // Small delay to ensure time difference

        var message = IrcMessage.Create("PONG", "token");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.User.LastActivity.Should().BeOnOrAfter(previousActivity);
    }
}

/// <summary>
/// Tests for the QuitHandler.
/// </summary>
public class QuitHandlerTests
{
    private readonly QuitHandler _handler = new();

    [Fact]
    public void CommandReturnsQuit()
    {
        _handler.Command.Should().Be("QUIT");
    }

    [Fact]
    public async Task HandleAsyncClosesConnection()
    {
        // Arrange
        var builder = new CommandContextBuilder("TestNick");
        builder.SetUserRegistered();
        var message = IrcMessage.Create("QUIT", "Goodbye!");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.Connection.IsClosed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsyncRemovesUserFromRepository()
    {
        // Arrange
        var builder = new CommandContextBuilder("TestNick");
        builder.SetUserRegistered();
        var connectionId = builder.User.ConnectionId;
        var message = IrcMessage.Create("QUIT");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.Users.GetByConnectionId(connectionId).Should().BeNull();
    }
}
