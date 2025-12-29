using FluentAssertions;
using Hugin.Core.ValueObjects;
using Microsoft.Extensions.Logging;
using Moq;

namespace Hugin.Protocol.S2S.Tests;

/// <summary>
/// Tests for S2SHandshakeManager.
/// </summary>
public class S2SHandshakeManagerTests
{
    private readonly ServerId _localServerId;
    private readonly Mock<ILogger<S2SHandshakeManager>> _mockLogger;
    private readonly List<(Guid ConnectionId, S2SMessage Message)> _sentMessages;
    private readonly S2SHandshakeManager _manager;

    public S2SHandshakeManagerTests()
    {
        _localServerId = ServerId.Create("001", "local.server.com");
        _mockLogger = new Mock<ILogger<S2SHandshakeManager>>();
        _sentMessages = new List<(Guid, S2SMessage)>();

        ValueTask SendAsync(Guid connId, S2SMessage msg, CancellationToken ct)
        {
            _sentMessages.Add((connId, msg));
            return ValueTask.CompletedTask;
        }

        _manager = new S2SHandshakeManager(
            _localServerId,
            "Hugin IRC Server",
            SendAsync,
            _mockLogger.Object);
    }

    [Fact]
    public async Task InitiateHandshakeSendsPassCapabServer()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        var password = "testpassword";

        // Act
        await _manager.InitiateHandshakeAsync(connectionId, password);

        // Assert
        _sentMessages.Should().HaveCount(3);
        
        _sentMessages[0].Message.Command.Should().Be("PASS");
        _sentMessages[0].Message.Parameters[0].Should().Be(password);
        
        _sentMessages[1].Message.Command.Should().Be("CAPAB");
        
        _sentMessages[2].Message.Command.Should().Be("SERVER");
        _sentMessages[2].Message.Parameters[0].Should().Be("local.server.com");
    }

    [Fact]
    public async Task InitiateHandshakeSetsStateCorrectly()
    {
        // Arrange
        var connectionId = Guid.NewGuid();

        // Act
        await _manager.InitiateHandshakeAsync(connectionId, "password");

        // Assert
        _manager.IsHandshaking(connectionId).Should().BeTrue();
        var state = _manager.GetState(connectionId);
        state.Should().NotBeNull();
        state!.IsOutgoing.Should().BeTrue();
        state.PassSent.Should().BeTrue();
        state.CapabSent.Should().BeTrue();
        state.ServerSent.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessPassMessageUpdatesState()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        await _manager.InitiateHandshakeAsync(connectionId, "password");
        
        var passMessage = S2SMessage.Create("PASS", "password", "TS", "6", "002");

        // Act
        var result = await _manager.ProcessHandshakeMessageAsync(connectionId, passMessage);

        // Assert
        result.Should().Be(HandshakeResult.InProgress);
        var state = _manager.GetState(connectionId);
        state!.PassReceived.Should().BeTrue();
        state.ReceivedPassword.Should().Be("password");
    }

    [Fact]
    public async Task ProcessCapabMessageUpdatesState()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        await _manager.InitiateHandshakeAsync(connectionId, "password");
        
        var capabMessage = S2SMessage.Create("CAPAB", "ENCAP TLS");

        // Act
        var result = await _manager.ProcessHandshakeMessageAsync(connectionId, capabMessage);

        // Assert
        result.Should().Be(HandshakeResult.InProgress);
        var state = _manager.GetState(connectionId);
        state!.CapabReceived.Should().BeTrue();
        state.ReceivedCapabilities.Should().Contain("ENCAP");
        state.ReceivedCapabilities.Should().Contain("TLS");
    }

    [Fact]
    public async Task ProcessServerMessageUpdatesState()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        await _manager.InitiateHandshakeAsync(connectionId, "password");
        
        var serverMessage = S2SMessage.Create("SERVER", "remote.server.com", "1", "002", "Remote Server");

        // Act
        var result = await _manager.ProcessHandshakeMessageAsync(connectionId, serverMessage);

        // Assert
        var state = _manager.GetState(connectionId);
        state!.ServerReceived.Should().BeTrue();
        state.RemoteServerName.Should().Be("remote.server.com");
        state.RemoteSid.Should().Be("002");
        state.RemoteDescription.Should().Be("Remote Server");
    }

    [Fact]
    public async Task HandshakeCompletesWhenAllMessagesReceived()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        await _manager.InitiateHandshakeAsync(connectionId, "password");
        
        // Act - receive all three messages
        await _manager.ProcessHandshakeMessageAsync(connectionId, 
            S2SMessage.Create("PASS", "password", "TS", "6", "002"));
        await _manager.ProcessHandshakeMessageAsync(connectionId, 
            S2SMessage.Create("CAPAB", "ENCAP TLS"));
        var result = await _manager.ProcessHandshakeMessageAsync(connectionId, 
            S2SMessage.Create("SERVER", "remote.server.com", "1", "002", "Remote Server"));

        // Assert
        result.Should().Be(HandshakeResult.Complete);
        var state = _manager.GetState(connectionId);
        state!.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessErrorMessageFailsHandshake()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        await _manager.InitiateHandshakeAsync(connectionId, "password");
        
        var errorMessage = S2SMessage.Create("ERROR", "Invalid password");

        // Act
        var result = await _manager.ProcessHandshakeMessageAsync(connectionId, errorMessage);

        // Assert
        result.Should().Be(HandshakeResult.Failed);
        var state = _manager.GetState(connectionId);
        state!.ErrorMessage.Should().Be("Invalid password");
    }

    [Fact]
    public async Task PasswordMismatchFailsHandshake()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        await _manager.InitiateHandshakeAsync(connectionId, "correctpassword");
        
        var passMessage = S2SMessage.Create("PASS", "wrongpassword", "TS", "6", "002");

        // Act
        var result = await _manager.ProcessHandshakeMessageAsync(connectionId, passMessage);

        // Assert
        result.Should().Be(HandshakeResult.Failed);
        // Should have sent ERROR message
        _sentMessages.Should().Contain(m => m.Message.Command == "ERROR");
    }

    [Fact]
    public async Task RemoveStateCleansUpCorrectly()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        await _manager.InitiateHandshakeAsync(connectionId, "password");

        // Act
        _manager.RemoveState(connectionId);

        // Assert
        _manager.IsHandshaking(connectionId).Should().BeFalse();
        _manager.GetState(connectionId).Should().BeNull();
    }

    [Fact]
    public async Task IncomingConnectionCreatesStateOnFirstMessage()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        var passMessage = S2SMessage.Create("PASS", "password", "TS", "6", "002");

        // Act
        await _manager.ProcessHandshakeMessageAsync(connectionId, passMessage);

        // Assert
        _manager.IsHandshaking(connectionId).Should().BeTrue();
        var state = _manager.GetState(connectionId);
        state!.IsOutgoing.Should().BeFalse();
    }
}
