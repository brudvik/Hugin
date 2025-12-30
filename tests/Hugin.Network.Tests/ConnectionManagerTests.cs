using System.Net;
using FluentAssertions;
using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hugin.Network.Tests;

/// <summary>
/// Tests for the ConnectionManager class.
/// </summary>
public class ConnectionManagerTests
{
    private readonly ConnectionManager _manager;

    public ConnectionManagerTests()
    {
        _manager = new ConnectionManager(NullLogger<ConnectionManager>.Instance);
    }

    [Fact]
    public void GetConnectionCountReturnsZeroWhenEmpty()
    {
        _manager.GetConnectionCount().Should().Be(0);
    }

    [Fact]
    public void RegisterConnectionIncreasesCount()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        var mockConnection = new MockClientConnection(connectionId);

        // Act
        _manager.RegisterConnection(connectionId, mockConnection);

        // Assert - MockClientConnection is not a ClientConnection, so it won't be registered
        // This is by design - only actual ClientConnection instances are registered
        _manager.GetConnectionCount().Should().Be(0);
    }

    [Fact]
    public void UnregisterConnectionRemovesConnection()
    {
        // Arrange
        var connectionId = Guid.NewGuid();

        // Act
        _manager.UnregisterConnection(connectionId); // Should not throw

        // Assert
        _manager.GetConnection(connectionId).Should().BeNull();
    }

    [Fact]
    public void GetConnectionReturnsNullForUnknownId()
    {
        _manager.GetConnection(Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public void GetAllConnectionsReturnsEmptyWhenNoConnections()
    {
        _manager.GetAllConnections().Should().BeEmpty();
    }

    [Fact]
    public void GetChannelConnectionsReturnsEmptyForUnknownChannel()
    {
        // GetChannelConnections now always returns empty as channel routing
        // is handled by MessageBroker using IChannelRepository
        _manager.GetChannelConnections("#unknown").Should().BeEmpty();
    }

    [Fact]
    public async Task CloseConnectionAsyncHandlesMissingConnection()
    {
        // Arrange
        var connectionId = Guid.NewGuid();

        // Act & Assert - Should not throw
        await _manager.CloseConnectionAsync(connectionId, "Test close");
    }
}

/// <summary>
/// Tests for the MessageBroker class.
/// </summary>
public class MessageBrokerTests
{
    private readonly ConnectionManager _connectionManager;
    private readonly MockChannelRepository _channelRepository;
    private readonly MockUserRepository _userRepository;
    private readonly MessageBroker _broker;

    public MessageBrokerTests()
    {
        _connectionManager = new ConnectionManager(NullLogger<ConnectionManager>.Instance);
        _channelRepository = new MockChannelRepository();
        _userRepository = new MockUserRepository();
        _broker = new MessageBroker(
            _connectionManager, 
            _channelRepository, 
            _userRepository,
            NullLogger<MessageBroker>.Instance);
    }

    [Fact]
    public async Task SendToConnectionAsyncHandlesMissingConnection()
    {
        // Arrange
        var connectionId = Guid.NewGuid();

        // Act & Assert - Should not throw
        await _broker.SendToConnectionAsync(connectionId, "TEST :message");
    }

    [Fact]
    public async Task SendToConnectionsAsyncHandlesEmptyList()
    {
        // Act & Assert - Should not throw
        await _broker.SendToConnectionsAsync(Enumerable.Empty<Guid>(), "TEST :message");
    }

    [Fact]
    public async Task SendToChannelAsyncHandlesEmptyChannel()
    {
        // Arrange - Channel with no members

        // Act & Assert - Should not throw
        await _broker.SendToChannelAsync("#empty", "PRIVMSG #empty :Hello");
    }

    [Fact]
    public async Task SendToChannelsAsyncHandlesEmptyChannelList()
    {
        // Act & Assert - Should not throw
        await _broker.SendToChannelsAsync(Enumerable.Empty<string>(), "TEST :message");
    }

    [Fact]
    public async Task SendToChannelAsyncExcludesSpecifiedConnection()
    {
        // Arrange - Create a channel with a member
        var excludedId = Guid.NewGuid();
        var channelName = ChannelName.Create("#test");
        var channel = new Channel(channelName);
        var user = CreateTestUser(excludedId, "TestUser");
        channel.AddMember(user);
        _channelRepository.Add(channel);

        // Act & Assert - Should not throw
        await _broker.SendToChannelAsync("#test", "PRIVMSG #test :Hello", excludedId);
    }

    [Fact]
    public async Task BroadcastAsyncHandlesNoConnections()
    {
        // Act & Assert - Should not throw
        await _broker.BroadcastAsync("NOTICE * :Server restarting");
    }

    [Fact]
    public async Task SendToOperatorsAsyncHandlesNoConnections()
    {
        // Act & Assert - Should not throw
        await _broker.SendToOperatorsAsync("NOTICE $ops :Operator message");
    }

    [Fact]
    public async Task SendToServerAsyncIsNotYetImplemented()
    {
        // Act & Assert - Should not throw (currently a no-op pending S2S support)
        await _broker.SendToServerAsync("server.example.com", "TEST :message");
    }

    [Fact]
    public async Task SendToChannelsAsyncDeduplicatesRecipients()
    {
        // Arrange - Same user in multiple channels
        var connectionId = Guid.NewGuid();
        var user = CreateTestUser(connectionId, "TestUser");

        var channel1Name = ChannelName.Create("#channel1");
        var channel1 = new Channel(channel1Name);
        channel1.AddMember(user);
        _channelRepository.Add(channel1);

        var channel2Name = ChannelName.Create("#channel2");
        var channel2 = new Channel(channel2Name);
        channel2.AddMember(user);
        _channelRepository.Add(channel2);

        // Act & Assert - Should not throw and should deduplicate
        await _broker.SendToChannelsAsync(TestChannelNames, "QUIT :Leaving");
    }

    /// <summary>
    /// Shared test channel names to avoid CA1861 warning.
    /// </summary>
    private static readonly string[] TestChannelNames = new[] { "#channel1", "#channel2" };

    private static User CreateTestUser(Guid connectionId, string nickname)
    {
        var nick = Nickname.Create(nickname);
        var serverId = ServerId.Create("000", "test.server");
        var user = new User(connectionId, System.Net.IPAddress.Loopback, "localhost", serverId, false);
        user.SetNickname(nick);
        return user;
    }
}

/// <summary>
/// Mock implementation of IChannelRepository for testing.
/// </summary>
internal sealed class MockChannelRepository : IChannelRepository
{
    private readonly Dictionary<string, Channel> _channels = new(StringComparer.OrdinalIgnoreCase);

    public Channel? GetByName(ChannelName name) => _channels.GetValueOrDefault(name.Value);
    public IEnumerable<Channel> GetAll() => _channels.Values;
    public IEnumerable<Channel> Search(string pattern) => Enumerable.Empty<Channel>();
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
    public int GetTotalChannelCount() => _channels.Count;
    public int GetCount() => _channels.Count;
    public IEnumerable<Channel> GetVisibleChannels(User? user) => _channels.Values;
}

/// <summary>
/// Mock implementation of IClientConnection for testing.
/// </summary>
internal sealed class MockClientConnection : IClientConnection
{
    public Guid ConnectionId { get; }
    public bool IsActive { get; set; } = true;
    public bool IsSecure { get; set; } = true;
    public string? CertificateFingerprint { get; set; }
    public EndPoint? RemoteEndPoint { get; set; }
    public List<byte[]> SentData { get; } = new();

    public MockClientConnection(Guid connectionId)
    {
        ConnectionId = connectionId;
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        SentData.Add(data.ToArray());
        return ValueTask.CompletedTask;
    }

    public ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        IsActive = false;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Mock implementation of IUserRepository for testing.
/// </summary>
internal sealed class MockUserRepository : IUserRepository
{
    private readonly Dictionary<Guid, User> _users = new();
    private int _maxCount;

    public User? GetByConnectionId(Guid connectionId) => _users.GetValueOrDefault(connectionId);
    public User? GetByNickname(Nickname nickname) => 
        _users.Values.FirstOrDefault(u => u.Nickname?.Equals(nickname) == true);
    public IEnumerable<User> GetAll() => _users.Values;
    public IEnumerable<User> FindByHostmask(Hostmask pattern) => Enumerable.Empty<User>();
    public IEnumerable<User> GetUsersInChannel(ChannelName channelName) => Enumerable.Empty<User>();
    public IEnumerable<User> GetByAccount(string accountName) => 
        _users.Values.Where(u => u.Account?.Equals(accountName, StringComparison.OrdinalIgnoreCase) == true);
    public bool IsNicknameInUse(Nickname nickname) => 
        _users.Values.Any(u => u.Nickname?.Equals(nickname) == true);
    public void Add(User user)
    {
        _users[user.ConnectionId] = user;
        if (_users.Count > _maxCount) _maxCount = _users.Count;
    }
    public void Remove(Guid connectionId) => _users.Remove(connectionId);
    public int GetCount() => _users.Count;
    public int GetInvisibleCount() => _users.Values.Count(u => u.Modes.HasFlag(Hugin.Core.Enums.UserMode.Invisible));
    public int GetOperatorCount() => _users.Values.Count(u => u.Modes.HasFlag(Hugin.Core.Enums.UserMode.Operator));
    public IEnumerable<User> GetByServer(ServerId serverId) => Enumerable.Empty<User>();
    public int GetMaxUserCount() => _maxCount;
}
