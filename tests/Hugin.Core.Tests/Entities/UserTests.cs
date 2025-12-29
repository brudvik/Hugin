using System.Net;
using FluentAssertions;
using Hugin.Core.Entities;
using Hugin.Core.Enums;
using Hugin.Core.ValueObjects;
using Xunit;

namespace Hugin.Core.Tests.Entities;

/// <summary>
/// Tests for the User entity.
/// </summary>
public class UserTests
{
    private static User CreateTestUser(bool isSecure = false)
    {
        var serverId = ServerId.Create("001", "test.server.com");
        return new User(
            Guid.NewGuid(),
            IPAddress.Parse("192.168.1.100"),
            "example.com",
            serverId,
            isSecure);
    }

    [Fact]
    public void ConstructorInitializesPropertiesCorrectly()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        var ip = IPAddress.Parse("10.0.0.1");
        var hostname = "client.example.com";
        var serverId = ServerId.Create("001", "test.server.com");

        // Act
        var user = new User(connectionId, ip, hostname, serverId, isSecure: true);

        // Assert
        user.ConnectionId.Should().Be(connectionId);
        user.IpAddress.Should().Be(ip);
        user.Hostname.Should().Be(hostname);
        user.DisplayedHostname.Should().Be(hostname);
        user.Server.Should().Be(serverId);
        user.IsSecure.Should().BeTrue();
        user.RegistrationState.Should().Be(RegistrationState.None);
        user.ConnectedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void SetNicknameSetsNicknameAndUpdatesActivity()
    {
        // Arrange
        var user = CreateTestUser();
        Nickname.TryCreate("TestNick", out var nickname, out _);

        // Act
        user.SetNickname(nickname!);

        // Assert
        user.Nickname.Should().Be(nickname);
        user.LastActivity.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void SetUserInfoSetsUsernameAndRealName()
    {
        // Arrange
        var user = CreateTestUser();

        // Act
        user.SetUserInfo("testuser", "Test User Real Name");

        // Assert
        user.Username.Should().Be("testuser");
        user.RealName.Should().Be("Test User Real Name");
    }

    [Fact]
    public void SetRegistrationStateSetsStateToRegistered()
    {
        // Arrange
        var user = CreateTestUser();
        Nickname.TryCreate("Nick", out var nickname, out _);
        user.SetNickname(nickname!);
        user.SetUserInfo("user", "Real Name");

        // Act
        user.SetRegistrationState(RegistrationState.Registered);

        // Assert
        user.RegistrationState.Should().Be(RegistrationState.Registered);
        user.IsRegistered.Should().BeTrue();
    }

    [Fact]
    public void SetAwaySetsAwayMessage()
    {
        // Arrange
        var user = CreateTestUser();

        // Act
        user.SetAway("Gone for lunch");

        // Assert
        user.AwayMessage.Should().Be("Gone for lunch");
        user.IsAway.Should().BeTrue();
    }

    [Fact]
    public void SetBackClearsAwayMessage()
    {
        // Arrange
        var user = CreateTestUser();
        user.SetAway("Away");

        // Act
        user.SetBack();

        // Assert
        user.AwayMessage.Should().BeNull();
        user.IsAway.Should().BeFalse();
    }

    [Fact]
    public void JoinChannelAddsChannelToUserChannels()
    {
        // Arrange
        var user = CreateTestUser();
        ChannelName.TryCreate("#test", out var channelName, out _);

        // Act
        user.JoinChannel(channelName!, ChannelMemberMode.Op);

        // Assert
        user.Channels.Should().ContainKey(channelName!);
        user.Channels[channelName!].Should().Be(ChannelMemberMode.Op);
    }

    [Fact]
    public void PartChannelRemovesChannelFromUserChannels()
    {
        // Arrange
        var user = CreateTestUser();
        ChannelName.TryCreate("#test", out var channelName, out _);
        user.JoinChannel(channelName!, ChannelMemberMode.None);

        // Act
        user.PartChannel(channelName!);

        // Assert
        user.Channels.Should().NotContainKey(channelName!);
    }

    [Fact]
    public void AddModeAddsUserMode()
    {
        // Arrange
        var user = CreateTestUser();

        // Act
        user.AddMode(UserMode.Invisible);

        // Assert
        user.Modes.HasFlag(UserMode.Invisible).Should().BeTrue();
    }

    [Fact]
    public void RemoveModeRemovesUserMode()
    {
        // Arrange
        var user = CreateTestUser();
        user.AddMode(UserMode.Invisible);

        // Act
        user.RemoveMode(UserMode.Invisible);

        // Assert
        user.Modes.HasFlag(UserMode.Invisible).Should().BeFalse();
    }

    [Fact]
    public void AddOperatorModeSetsOperatorModeAndPrivileges()
    {
        // Arrange
        var user = CreateTestUser();

        // Act
        user.AddMode(UserMode.Operator);

        // Assert
        user.IsOperator.Should().BeTrue();
        user.Modes.HasFlag(UserMode.Operator).Should().BeTrue();
    }

    [Fact]
    public void SetAuthenticatedSetsAccountName()
    {
        // Arrange
        var user = CreateTestUser();

        // Act
        user.SetAuthenticated("myaccount");

        // Assert
        user.Account.Should().Be("myaccount");
        user.Modes.HasFlag(UserMode.Registered).Should().BeTrue();
    }

    [Fact]
    public void SetCloakedHostnameUpdatesDisplayedHostname()
    {
        // Arrange
        var user = CreateTestUser();

        // Act
        user.SetCloakedHostname("abc123.hugin.cloak");

        // Assert
        user.DisplayedHostname.Should().Be("abc123.hugin.cloak");
    }

    [Fact]
    public void HostmaskReturnsCorrectFormat()
    {
        // Arrange
        var user = CreateTestUser();
        Nickname.TryCreate("TestNick", out var nickname, out _);
        user.SetNickname(nickname!);
        user.SetUserInfo("testuser", "Real Name");

        // Act
        var hostmask = user.Hostmask;

        // Assert
        hostmask.ToString().Should().Be("TestNick!testuser@example.com");
    }

    [Fact]
    public void SecureConnectionHasSecureMode()
    {
        // Arrange & Act
        var user = CreateTestUser(isSecure: true);

        // Assert
        user.IsSecure.Should().BeTrue();
        user.Modes.HasFlag(UserMode.Secure).Should().BeTrue();
    }
}
