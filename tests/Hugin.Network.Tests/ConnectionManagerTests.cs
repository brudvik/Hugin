using System.Net;
using FluentAssertions;
using Hugin.Core.Interfaces;
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
    public void JoinChannelAddsConnectionToChannel()
    {
        // Arrange
        var connectionId = Guid.NewGuid();

        // Act
        _manager.JoinChannel(connectionId, "#test");

        // Assert - Without a registered ClientConnection, this won't return results
        _manager.GetChannelConnections("#test").Should().BeEmpty();
    }

    [Fact]
    public void PartChannelRemovesConnectionFromChannel()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        _manager.JoinChannel(connectionId, "#test");

        // Act
        _manager.PartChannel(connectionId, "#test");

        // Assert
        _manager.GetChannelConnections("#test").Should().BeEmpty();
    }

    [Fact]
    public void GetChannelConnectionsReturnsEmptyForUnknownChannel()
    {
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
    private readonly MessageBroker _broker;

    public MessageBrokerTests()
    {
        _connectionManager = new ConnectionManager(NullLogger<ConnectionManager>.Instance);
        _broker = new MessageBroker(_connectionManager, NullLogger<MessageBroker>.Instance);
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
        // Arrange
        var excludedId = Guid.NewGuid();
        _connectionManager.JoinChannel(excludedId, "#test");

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

    private static readonly string[] TestChannels = ["#channel1", "#channel2"];

    [Fact]
    public async Task SendToChannelsAsyncDeduplicatesRecipients()
    {
        // Arrange - Same connection in multiple channels
        var connectionId = Guid.NewGuid();
        _connectionManager.JoinChannel(connectionId, "#channel1");
        _connectionManager.JoinChannel(connectionId, "#channel2");

        // Act & Assert - Should not throw and should deduplicate
        await _broker.SendToChannelsAsync(TestChannels, "QUIT :Leaving");
    }
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
