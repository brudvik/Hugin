using FluentAssertions;
using Hugin.Core.Entities;
using Hugin.Core.Enums;
using Hugin.Core.ValueObjects;
using Xunit;

namespace Hugin.Core.Tests.Entities;

/// <summary>
/// Tests for the Channel entity.
/// </summary>
public class ChannelTests
{
    private static Channel CreateTestChannel(string name = "#test")
    {
        ChannelName.TryCreate(name, out var channelName, out _);
        return new Channel(channelName!);
    }

    private static User CreateTestUser(string nick = "TestUser")
    {
        var serverId = ServerId.Create("001", "test.server.com");
        var user = new User(
            Guid.NewGuid(),
            System.Net.IPAddress.Parse("127.0.0.1"),
            "localhost",
            serverId,
            isSecure: false);
        Nickname.TryCreate(nick, out var nickname, out _);
        user.SetNickname(nickname!);
        user.SetUserInfo("testuser", "Test User");
        return user;
    }

    [Fact]
    public void ConstructorInitializesWithDefaultModes()
    {
        // Arrange & Act
        var channel = CreateTestChannel("#mychannel");

        // Assert
        channel.Name.Value.Should().Be("#mychannel");
        channel.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        channel.Modes.HasFlag(ChannelMode.NoExternalMessages).Should().BeTrue();
        channel.Modes.HasFlag(ChannelMode.TopicProtected).Should().BeTrue();
        channel.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void SetTopicSetsTopic()
    {
        // Arrange
        var channel = CreateTestChannel();

        // Act
        channel.SetTopic("Welcome to #test!", "admin");

        // Assert
        channel.Topic.Should().Be("Welcome to #test!");
        channel.TopicSetBy.Should().Be("admin");
        channel.TopicSetAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void AddMemberAddsUserToChannel()
    {
        // Arrange
        var channel = CreateTestChannel();
        var user = CreateTestUser();

        // Act
        var member = channel.AddMember(user, ChannelMemberMode.Op);

        // Assert
        channel.HasMember(user.ConnectionId).Should().BeTrue();
        channel.MemberCount.Should().Be(1);
        channel.IsEmpty.Should().BeFalse();
        member.Nickname.Should().Be(user.Nickname);
        member.Modes.Should().Be(ChannelMemberMode.Op);
    }

    [Fact]
    public void RemoveMemberRemovesUserFromChannel()
    {
        // Arrange
        var channel = CreateTestChannel();
        var user = CreateTestUser();
        channel.AddMember(user);

        // Act
        var result = channel.RemoveMember(user.ConnectionId);

        // Assert
        result.Should().BeTrue();
        channel.HasMember(user.ConnectionId).Should().BeFalse();
        channel.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void GetMemberReturnsCorrectMember()
    {
        // Arrange
        var channel = CreateTestChannel();
        var user = CreateTestUser();
        channel.AddMember(user, ChannelMemberMode.Voice);

        // Act
        var member = channel.GetMember(user.ConnectionId);

        // Assert
        member.Should().NotBeNull();
        member!.Modes.Should().Be(ChannelMemberMode.Voice);
    }

    [Fact]
    public void UpdateMemberNicknameUpdatesNickname()
    {
        // Arrange
        var channel = CreateTestChannel();
        var user = CreateTestUser("OldNick");
        channel.AddMember(user);
        Nickname.TryCreate("NewNick", out var newNick, out _);

        // Act
        channel.UpdateMemberNickname(user.ConnectionId, newNick!);

        // Assert
        var member = channel.GetMember(user.ConnectionId);
        member!.Nickname.Value.Should().Be("NewNick");
    }

    [Fact]
    public void AddModeAddsModeToChannel()
    {
        // Arrange
        var channel = CreateTestChannel();

        // Act
        channel.AddMode(ChannelMode.InviteOnly);

        // Assert
        channel.Modes.HasFlag(ChannelMode.InviteOnly).Should().BeTrue();
    }

    [Fact]
    public void RemoveModeRemovesModeFromChannel()
    {
        // Arrange
        var channel = CreateTestChannel();
        channel.AddMode(ChannelMode.InviteOnly);

        // Act
        channel.RemoveMode(ChannelMode.InviteOnly);

        // Assert
        channel.Modes.HasFlag(ChannelMode.InviteOnly).Should().BeFalse();
    }

    [Fact]
    public void SetKeySetsKeyAndMode()
    {
        // Arrange
        var channel = CreateTestChannel();

        // Act
        channel.SetKey("secretkey");

        // Assert
        channel.Key.Should().Be("secretkey");
        channel.Modes.HasFlag(ChannelMode.Key).Should().BeTrue();
    }

    [Fact]
    public void RemoveKeyClearsKeyAndMode()
    {
        // Arrange
        var channel = CreateTestChannel();
        channel.SetKey("secretkey");

        // Act
        channel.RemoveKey();

        // Assert
        channel.Key.Should().BeNull();
        channel.Modes.HasFlag(ChannelMode.Key).Should().BeFalse();
    }

    [Fact]
    public void SetLimitSetsLimitAndMode()
    {
        // Arrange
        var channel = CreateTestChannel();

        // Act
        channel.SetLimit(50);

        // Assert
        channel.UserLimit.Should().Be(50);
        channel.Modes.HasFlag(ChannelMode.Limit).Should().BeTrue();
    }

    [Fact]
    public void RemoveLimitClearsLimitAndMode()
    {
        // Arrange
        var channel = CreateTestChannel();
        channel.SetLimit(50);

        // Act
        channel.RemoveLimit();

        // Assert
        channel.UserLimit.Should().BeNull();
        channel.Modes.HasFlag(ChannelMode.Limit).Should().BeFalse();
    }

    [Fact]
    public void AddBanAddsBanEntry()
    {
        // Arrange
        var channel = CreateTestChannel();

        // Act
        channel.AddBan("*!*@bad.host.com", "admin");

        // Assert
        channel.Bans.Should().HaveCount(1);
        channel.Bans[0].Mask.Should().Be("*!*@bad.host.com");
        channel.Bans[0].SetBy.Should().Be("admin");
    }

    [Fact]
    public void RemoveBanRemovesBanEntry()
    {
        // Arrange
        var channel = CreateTestChannel();
        channel.AddBan("*!*@bad.host.com", "admin");

        // Act
        var result = channel.RemoveBan("*!*@bad.host.com");

        // Assert
        result.Should().BeTrue();
        channel.Bans.Should().BeEmpty();
    }

    [Fact]
    public void IsBannedReturnsTrueForBannedUser()
    {
        // Arrange
        var channel = CreateTestChannel();
        channel.AddBan("*!*@*.bad.com", "admin");
        var hostmask = Hostmask.Create("BadUser", "user", "test.bad.com");

        // Act
        var isBanned = channel.IsBanned(hostmask);

        // Assert
        isBanned.Should().BeTrue();
    }

    [Fact]
    public void IsBannedReturnsFalseForNonBannedUser()
    {
        // Arrange
        var channel = CreateTestChannel();
        channel.AddBan("*!*@*.bad.com", "admin");
        var hostmask = Hostmask.Create("GoodUser", "user", "good.com");

        // Act
        var isBanned = channel.IsBanned(hostmask);

        // Assert
        isBanned.Should().BeFalse();
    }

    [Fact]
    public void AddInvitationAddsInvite()
    {
        // Arrange
        var channel = CreateTestChannel();
        var connectionId = Guid.NewGuid();

        // Act
        channel.AddInvitation(connectionId);

        // Assert
        channel.IsInvited(connectionId).Should().BeTrue();
    }

    [Fact]
    public void GetModeStringReturnsCorrectFormat()
    {
        // Arrange
        var channel = CreateTestChannel();
        channel.AddMode(ChannelMode.InviteOnly);
        channel.AddMode(ChannelMode.Secret);
        channel.SetKey("mykey");
        channel.SetLimit(25);

        // Act
        var modeString = channel.GetModeString();

        // Assert
        modeString.Should().Contain("+");
        modeString.Should().Contain("i");
        modeString.Should().Contain("s");
        modeString.Should().Contain("k");
        modeString.Should().Contain("l");
        modeString.Should().Contain("mykey");
        modeString.Should().Contain("25");
    }
}

/// <summary>
/// Tests for the ChannelMember class.
/// </summary>
public class ChannelMemberTests
{
    [Fact]
    public void ConstructorInitializesProperties()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        Nickname.TryCreate("TestNick", out var nickname, out _);

        // Act
        var member = new ChannelMember(connectionId, nickname!, ChannelMemberMode.Op);

        // Assert
        member.ConnectionId.Should().Be(connectionId);
        member.Nickname.Should().Be(nickname);
        member.Modes.Should().Be(ChannelMemberMode.Op);
        member.JoinedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void CanSpeakReturnsTrueForVoicedUser()
    {
        // Arrange
        Nickname.TryCreate("TestNick", out var nickname, out _);
        var member = new ChannelMember(Guid.NewGuid(), nickname!, ChannelMemberMode.Voice);

        // Assert
        member.CanSpeak.Should().BeTrue();
    }

    [Fact]
    public void CanSpeakReturnsFalseForRegularUser()
    {
        // Arrange
        Nickname.TryCreate("TestNick", out var nickname, out _);
        var member = new ChannelMember(Guid.NewGuid(), nickname!, ChannelMemberMode.None);

        // Assert
        member.CanSpeak.Should().BeFalse();
    }

    [Fact]
    public void IsOpOrHigherReturnsTrueForOp()
    {
        // Arrange
        Nickname.TryCreate("TestNick", out var nickname, out _);
        var member = new ChannelMember(Guid.NewGuid(), nickname!, ChannelMemberMode.Op);

        // Assert
        member.IsOpOrHigher.Should().BeTrue();
    }

    [Fact]
    public void AddModeAddsMode()
    {
        // Arrange
        Nickname.TryCreate("TestNick", out var nickname, out _);
        var member = new ChannelMember(Guid.NewGuid(), nickname!, ChannelMemberMode.None);

        // Act
        member.AddMode(ChannelMemberMode.Voice);

        // Assert
        member.Modes.HasFlag(ChannelMemberMode.Voice).Should().BeTrue();
    }

    [Fact]
    public void RemoveModeRemovesMode()
    {
        // Arrange
        Nickname.TryCreate("TestNick", out var nickname, out _);
        var member = new ChannelMember(Guid.NewGuid(), nickname!, ChannelMemberMode.Op | ChannelMemberMode.Voice);

        // Act
        member.RemoveMode(ChannelMemberMode.Voice);

        // Assert
        member.Modes.HasFlag(ChannelMemberMode.Voice).Should().BeFalse();
        member.Modes.HasFlag(ChannelMemberMode.Op).Should().BeTrue();
    }
}
