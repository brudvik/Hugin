using FluentAssertions;
using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using Moq;

namespace Hugin.Protocol.S2S.Tests;

/// <summary>
/// Tests for ServerLinkManager.
/// </summary>
public class ServerLinkManagerTests
{
    private readonly Mock<IMessageBroker> _mockBroker;
    private readonly ServerId _localServerId;
    private readonly ServerLinkManager _manager;

    public ServerLinkManagerTests()
    {
        _mockBroker = new Mock<IMessageBroker>();
        _localServerId = ServerId.Create("001", "local.server.com");
        _manager = new ServerLinkManager(_mockBroker.Object, _localServerId);
    }

    [Fact]
    public void AddDirectLinkAddsServerToCollections()
    {
        // Arrange
        var serverId = ServerId.Create("002", "remote.server.com");
        var server = new LinkedServer(serverId, "Remote Server", "Hugin-1.0", hopCount: 1);
        var connectionId = Guid.NewGuid();

        // Act
        _manager.AddDirectLink(server, connectionId);

        // Assert
        _manager.GetBySid("002").Should().Be(server);
        _manager.GetByName("remote.server.com").Should().Be(server);
        _manager.DirectLinks.Should().Contain(server);
        _manager.AllServers.Should().Contain(server);
        _manager.GetConnectionId(serverId).Should().Be(connectionId);
    }

    [Fact]
    public void AddRemoteServerAddsToAllServersButNotDirectLinks()
    {
        // Arrange
        var directServerId = ServerId.Create("002", "direct.server.com");
        var directServer = new LinkedServer(directServerId, "Direct Server", "Hugin-1.0", hopCount: 1);
        _manager.AddDirectLink(directServer, Guid.NewGuid());

        var remoteServerId = ServerId.Create("003", "remote.server.com");
        var remoteServer = new LinkedServer(remoteServerId, "Remote Server", "Hugin-1.0", hopCount: 2, learnedFrom: directServerId);

        // Act
        _manager.AddRemoteServer(remoteServer);

        // Assert
        _manager.GetBySid("003").Should().Be(remoteServer);
        _manager.AllServers.Should().Contain(remoteServer);
        _manager.DirectLinks.Should().NotContain(remoteServer);
        _manager.GetConnectionId(remoteServerId).Should().BeNull();
    }

    [Fact]
    public void RemoveServerRemovesServerAndCascades()
    {
        // Arrange
        var directServerId = ServerId.Create("002", "direct.server.com");
        var directServer = new LinkedServer(directServerId, "Direct Server", "Hugin-1.0", hopCount: 1);
        _manager.AddDirectLink(directServer, Guid.NewGuid());

        var remoteServerId = ServerId.Create("003", "remote.server.com");
        var remoteServer = new LinkedServer(remoteServerId, "Remote Server", "Hugin-1.0", hopCount: 2, learnedFrom: directServerId);
        _manager.AddRemoteServer(remoteServer);

        // Act
        var removed = _manager.RemoveServer(directServerId).ToList();

        // Assert
        removed.Should().Contain(directServer);
        removed.Should().Contain(remoteServer);
        _manager.GetBySid("002").Should().BeNull();
        _manager.GetBySid("003").Should().BeNull();
        _manager.AllServers.Should().BeEmpty();
    }

    [Fact]
    public void GetRouteToDirectServerReturnsServerId()
    {
        // Arrange
        var serverId = ServerId.Create("002", "direct.server.com");
        var server = new LinkedServer(serverId, "Direct Server", "Hugin-1.0", hopCount: 1);
        _manager.AddDirectLink(server, Guid.NewGuid());

        // Act
        var route = _manager.GetRouteTo(serverId);

        // Assert
        route.Should().Be(serverId);
    }

    [Fact]
    public void GetRouteToRemoteServerReturnsDirectLink()
    {
        // Arrange
        var directServerId = ServerId.Create("002", "direct.server.com");
        var directServer = new LinkedServer(directServerId, "Direct Server", "Hugin-1.0", hopCount: 1);
        _manager.AddDirectLink(directServer, Guid.NewGuid());

        var remoteServerId = ServerId.Create("003", "remote.server.com");
        var remoteServer = new LinkedServer(remoteServerId, "Remote Server", "Hugin-1.0", hopCount: 2, learnedFrom: directServerId);
        _manager.AddRemoteServer(remoteServer);

        // Act
        var route = _manager.GetRouteTo(remoteServerId);

        // Assert
        route.Should().Be(directServerId);
    }

    [Fact]
    public void GetRouteToUnknownServerReturnsNull()
    {
        // Arrange
        var unknownServerId = ServerId.Create("999", "unknown.server.com");

        // Act
        var route = _manager.GetRouteTo(unknownServerId);

        // Assert
        route.Should().BeNull();
    }

    [Fact]
    public void ServerLinkedEventFiresOnAddDirectLink()
    {
        // Arrange
        var serverId = ServerId.Create("002", "remote.server.com");
        var server = new LinkedServer(serverId, "Remote Server", "Hugin-1.0", hopCount: 1);
        ServerLinkedEventArgs? eventArgs = null;
        _manager.ServerLinked += (_, args) => eventArgs = args;

        // Act
        _manager.AddDirectLink(server, Guid.NewGuid());

        // Assert
        eventArgs.Should().NotBeNull();
        eventArgs!.Server.Should().Be(server);
        eventArgs.IsDirect.Should().BeTrue();
    }

    [Fact]
    public void ServerLinkedEventFiresOnAddRemoteServer()
    {
        // Arrange
        var serverId = ServerId.Create("002", "remote.server.com");
        var server = new LinkedServer(serverId, "Remote Server", "Hugin-1.0", hopCount: 2);
        ServerLinkedEventArgs? eventArgs = null;
        _manager.ServerLinked += (_, args) => eventArgs = args;

        // Act
        _manager.AddRemoteServer(server);

        // Assert
        eventArgs.Should().NotBeNull();
        eventArgs!.IsDirect.Should().BeFalse();
    }

    [Fact]
    public void ServerUnlinkedEventFiresOnRemoveServer()
    {
        // Arrange
        var serverId = ServerId.Create("002", "remote.server.com");
        var server = new LinkedServer(serverId, "Remote Server", "Hugin-1.0", hopCount: 1);
        _manager.AddDirectLink(server, Guid.NewGuid());

        ServerUnlinkedEventArgs? eventArgs = null;
        _manager.ServerUnlinked += (_, args) => eventArgs = args;

        // Act
        _manager.RemoveServer(serverId);

        // Assert
        eventArgs.Should().NotBeNull();
        eventArgs!.Server.Should().Be(server);
    }

    [Fact]
    public async Task SendToServerAsyncRoutesMessageCorrectly()
    {
        // Arrange
        var serverId = ServerId.Create("002", "remote.server.com");
        var server = new LinkedServer(serverId, "Remote Server", "Hugin-1.0", hopCount: 1);
        var connectionId = Guid.NewGuid();
        _manager.AddDirectLink(server, connectionId);

        var message = S2SMessage.Create("PING", "test");

        _mockBroker.Setup(b => b.SendToConnectionAsync(
            connectionId, 
            It.IsAny<string>(), 
            It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        // Act
        await _manager.SendToServerAsync(serverId, message);

        // Assert
        _mockBroker.Verify(b => b.SendToConnectionAsync(
            connectionId,
            It.Is<string>(s => s.Contains("PING")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendToServerBySidStringRoutesCorrectly()
    {
        // Arrange
        var serverId = ServerId.Create("002", "remote.server.com");
        var server = new LinkedServer(serverId, "Remote Server", "Hugin-1.0", hopCount: 1);
        var connectionId = Guid.NewGuid();
        _manager.AddDirectLink(server, connectionId);

        var message = S2SMessage.Create("PING", "test");

        _mockBroker.Setup(b => b.SendToConnectionAsync(
            connectionId,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        // Act
        await _manager.SendToServerAsync("002", message);

        // Assert
        _mockBroker.Verify(b => b.SendToConnectionAsync(
            connectionId,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BroadcastAsyncSendsToAllDirectLinks()
    {
        // Arrange
        var server1Id = ServerId.Create("002", "server1.com");
        var server1 = new LinkedServer(server1Id, "Server 1", "Hugin-1.0", hopCount: 1);
        var conn1 = Guid.NewGuid();
        _manager.AddDirectLink(server1, conn1);

        var server2Id = ServerId.Create("003", "server2.com");
        var server2 = new LinkedServer(server2Id, "Server 2", "Hugin-1.0", hopCount: 1);
        var conn2 = Guid.NewGuid();
        _manager.AddDirectLink(server2, conn2);

        var message = S2SMessage.Create("PING", "test");

        _mockBroker.Setup(b => b.SendToConnectionAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        // Act
        await _manager.BroadcastAsync(message);

        // Assert
        _mockBroker.Verify(b => b.SendToConnectionAsync(
            conn1, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockBroker.Verify(b => b.SendToConnectionAsync(
            conn2, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BroadcastAsyncExcludesSpecifiedServer()
    {
        // Arrange
        var server1Id = ServerId.Create("002", "server1.com");
        var server1 = new LinkedServer(server1Id, "Server 1", "Hugin-1.0", hopCount: 1);
        var conn1 = Guid.NewGuid();
        _manager.AddDirectLink(server1, conn1);

        var server2Id = ServerId.Create("003", "server2.com");
        var server2 = new LinkedServer(server2Id, "Server 2", "Hugin-1.0", hopCount: 1);
        var conn2 = Guid.NewGuid();
        _manager.AddDirectLink(server2, conn2);

        var message = S2SMessage.Create("PING", "test");

        _mockBroker.Setup(b => b.SendToConnectionAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        // Act - exclude server1
        await _manager.BroadcastAsync(message, exceptServerId: server1Id);

        // Assert
        _mockBroker.Verify(b => b.SendToConnectionAsync(
            conn1, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockBroker.Verify(b => b.SendToConnectionAsync(
            conn2, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
