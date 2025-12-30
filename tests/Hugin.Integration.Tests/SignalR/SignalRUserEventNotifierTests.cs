using FluentAssertions;
using Hugin.Core.Interfaces;
using Hugin.Server.Api.Hubs;
using Hugin.Server.Api.Models;
using Hugin.Server.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hugin.Integration.Tests.SignalR;

/// <summary>
/// Tests for SignalRUserEventNotifier.
/// </summary>
public sealed class SignalRUserEventNotifierTests
{
    private readonly Mock<IAdminHubService> _mockHubService;
    private readonly Mock<ILogger<SignalRUserEventNotifier>> _mockLogger;
    private readonly SignalRUserEventNotifier _notifier;

    public SignalRUserEventNotifierTests()
    {
        _mockHubService = new Mock<IAdminHubService>();
        _mockLogger = new Mock<ILogger<SignalRUserEventNotifier>>();
        _notifier = new SignalRUserEventNotifier(_mockHubService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task OnUserConnectedAsyncBroadcastsConnectedEvent()
    {
        // Arrange
        UserEventDto? capturedEvent = null;
        _mockHubService
            .Setup(x => x.BroadcastUserEventAsync(It.IsAny<UserEventDto>(), It.IsAny<CancellationToken>()))
            .Callback<UserEventDto, CancellationToken>((e, _) => capturedEvent = e)
            .Returns(Task.CompletedTask);

        // Act
        await _notifier.OnUserConnectedAsync("TestUser", "test.host.com", "user-123");

        // Assert
        capturedEvent.Should().NotBeNull();
        capturedEvent!.EventType.Should().Be("Connected");
        capturedEvent.Nickname.Should().Be("TestUser");
        capturedEvent.Hostname.Should().Be("test.host.com");
        capturedEvent.UserId.Should().Be("user-123");
    }

    [Fact]
    public async Task OnUserDisconnectedAsyncBroadcastsDisconnectedEvent()
    {
        // Arrange
        UserEventDto? capturedEvent = null;
        _mockHubService
            .Setup(x => x.BroadcastUserEventAsync(It.IsAny<UserEventDto>(), It.IsAny<CancellationToken>()))
            .Callback<UserEventDto, CancellationToken>((e, _) => capturedEvent = e)
            .Returns(Task.CompletedTask);

        // Act
        await _notifier.OnUserDisconnectedAsync("TestUser", "test.host.com", "Client quit", "user-123");

        // Assert
        capturedEvent.Should().NotBeNull();
        capturedEvent!.EventType.Should().Be("Disconnected");
        capturedEvent.Nickname.Should().Be("TestUser");
        capturedEvent.Details.Should().Be("Client quit");
    }

    [Fact]
    public async Task OnNickChangeAsyncBroadcastsNickChangeEvent()
    {
        // Arrange
        UserEventDto? capturedEvent = null;
        _mockHubService
            .Setup(x => x.BroadcastUserEventAsync(It.IsAny<UserEventDto>(), It.IsAny<CancellationToken>()))
            .Callback<UserEventDto, CancellationToken>((e, _) => capturedEvent = e)
            .Returns(Task.CompletedTask);

        // Act
        await _notifier.OnNickChangeAsync("OldNick", "NewNick", "test.host.com", "user-123");

        // Assert
        capturedEvent.Should().NotBeNull();
        capturedEvent!.EventType.Should().Be("NickChange");
        capturedEvent.Nickname.Should().Be("NewNick");
        capturedEvent.Details.Should().Contain("OldNick");
    }

    [Fact]
    public async Task OnUserJoinAsyncBroadcastsJoinEvent()
    {
        // Arrange
        UserEventDto? capturedEvent = null;
        _mockHubService
            .Setup(x => x.BroadcastUserEventAsync(It.IsAny<UserEventDto>(), It.IsAny<CancellationToken>()))
            .Callback<UserEventDto, CancellationToken>((e, _) => capturedEvent = e)
            .Returns(Task.CompletedTask);

        // Act
        await _notifier.OnUserJoinAsync("TestUser", "#channel", "test.host.com", "user-123");

        // Assert
        capturedEvent.Should().NotBeNull();
        capturedEvent!.EventType.Should().Be("Join");
        capturedEvent.Nickname.Should().Be("TestUser");
        capturedEvent.Channel.Should().Be("#channel");
    }

    [Fact]
    public async Task OnUserPartAsyncBroadcastsPartEvent()
    {
        // Arrange
        UserEventDto? capturedEvent = null;
        _mockHubService
            .Setup(x => x.BroadcastUserEventAsync(It.IsAny<UserEventDto>(), It.IsAny<CancellationToken>()))
            .Callback<UserEventDto, CancellationToken>((e, _) => capturedEvent = e)
            .Returns(Task.CompletedTask);

        // Act
        await _notifier.OnUserPartAsync("TestUser", "#channel", "test.host.com", "Leaving", "user-123");

        // Assert
        capturedEvent.Should().NotBeNull();
        capturedEvent!.EventType.Should().Be("Part");
        capturedEvent.Nickname.Should().Be("TestUser");
        capturedEvent.Channel.Should().Be("#channel");
        capturedEvent.Details.Should().Be("Leaving");
    }

    [Fact]
    public async Task NotifierHandlesHubServiceExceptionGracefully()
    {
        // Arrange
        _mockHubService
            .Setup(x => x.BroadcastUserEventAsync(It.IsAny<UserEventDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Hub unavailable"));

        // Act & Assert - should not throw
        var exception = await Record.ExceptionAsync(async () => 
            await _notifier.OnUserConnectedAsync("Test", "host", "id"));
        
        exception.Should().BeNull();
    }

    [Fact]
    public async Task EventTimestampIsSetToCurrentTime()
    {
        // Arrange
        var beforeTest = DateTime.UtcNow;
        UserEventDto? capturedEvent = null;
        _mockHubService
            .Setup(x => x.BroadcastUserEventAsync(It.IsAny<UserEventDto>(), It.IsAny<CancellationToken>()))
            .Callback<UserEventDto, CancellationToken>((e, _) => capturedEvent = e)
            .Returns(Task.CompletedTask);

        // Act
        await _notifier.OnUserConnectedAsync("TestUser", "host");
        var afterTest = DateTime.UtcNow;

        // Assert
        capturedEvent.Should().NotBeNull();
        capturedEvent!.Timestamp.Should().BeOnOrAfter(beforeTest);
        capturedEvent.Timestamp.Should().BeOnOrBefore(afterTest);
    }
}

/// <summary>
/// Tests for IUserEventNotifier interface compliance.
/// </summary>
public sealed class UserEventNotifierInterfaceTests
{
    [Fact]
    public void SignalRUserEventNotifierImplementsIUserEventNotifier()
    {
        // Arrange
        var mockHubService = new Mock<IAdminHubService>();
        var mockLogger = new Mock<ILogger<SignalRUserEventNotifier>>();

        // Act
        var notifier = new SignalRUserEventNotifier(mockHubService.Object, mockLogger.Object);

        // Assert
        notifier.Should().BeAssignableTo<IUserEventNotifier>();
    }
}
