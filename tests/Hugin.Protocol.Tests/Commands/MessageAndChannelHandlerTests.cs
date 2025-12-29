using System.Net;
using FluentAssertions;
using Hugin.Core.Entities;
using Hugin.Core.Enums;
using Hugin.Core.ValueObjects;
using Hugin.Protocol;
using Hugin.Protocol.Commands.Handlers;
using Xunit;

namespace Hugin.Protocol.Tests.Commands;

/// <summary>
/// Tests for the PrivmsgHandler.
/// </summary>
public class PrivmsgHandlerTests
{
    private readonly PrivmsgHandler _handler = new();

    [Fact]
    public void CommandReturnsPrivmsg()
    {
        _handler.Command.Should().Be("PRIVMSG");
    }

    [Fact]
    public void MinimumParametersIsTwo()
    {
        _handler.MinimumParameters.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsyncWithEmptyTextSendsNoTextToSend()
    {
        // Arrange
        var builder = new CommandContextBuilder("TestNick");
        builder.SetUserRegistered();
        var message = IrcMessage.Create("PRIVMSG", "#test", "");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().ContainSingle();
        builder.Broker.ConnectionMessages[0].Message.Should().Contain("412"); // ERR_NOTEXTTOSEND
    }

    [Fact]
    public async Task HandleAsyncToChannelSendsToChannel()
    {
        // Arrange
        var builder = new CommandContextBuilder("TestNick");
        builder.SetUserRegistered();
        
        // Create channel and add user
        ChannelName.TryCreate("#test", out var channelName, out _);
        var channel = builder.Channels.Create(channelName!);
        channel.AddMember(builder.User);
        builder.User.JoinChannel(channelName!);

        var message = IrcMessage.Create("PRIVMSG", "#test", "Hello channel!");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.Broker.ChannelMessages.Should().ContainSingle();
        builder.Broker.ChannelMessages[0].ChannelName.Should().Be("#test");
        builder.Broker.ChannelMessages[0].Message.Should().Contain("Hello channel!");
    }

    [Fact]
    public async Task HandleAsyncToNonExistentChannelSendsNoSuchChannel()
    {
        // Arrange
        var builder = new CommandContextBuilder("TestNick");
        builder.SetUserRegistered();
        var message = IrcMessage.Create("PRIVMSG", "#nonexistent", "Hello!");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().ContainSingle();
        builder.Broker.ConnectionMessages[0].Message.Should().Contain("403"); // ERR_NOSUCHCHANNEL
    }

    [Fact]
    public async Task HandleAsyncToUserSendsToUser()
    {
        // Arrange
        var builder = new CommandContextBuilder("Sender");
        builder.SetUserRegistered();

        // Add target user
        var targetUser = new User(
            Guid.NewGuid(),
            IPAddress.Parse("10.0.0.1"),
            "target.example.com",
            ServerId.Create("001", "test.server.com"),
            isSecure: false);
        Nickname.TryCreate("Recipient", out var targetNick, out _);
        targetUser.SetNickname(targetNick!);
        targetUser.SetUserInfo("target", "Target User");
        targetUser.SetRegistrationState(RegistrationState.Registered);
        builder.Users.Add(targetUser);

        var message = IrcMessage.Create("PRIVMSG", "Recipient", "Private message!");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().ContainSingle();
        builder.Broker.ConnectionMessages[0].ConnectionId.Should().Be(targetUser.ConnectionId);
        builder.Broker.ConnectionMessages[0].Message.Should().Contain("Private message!");
    }

    [Fact]
    public async Task HandleAsyncToNonExistentUserSendsNoSuchNick()
    {
        // Arrange
        var builder = new CommandContextBuilder("TestNick");
        builder.SetUserRegistered();
        var message = IrcMessage.Create("PRIVMSG", "NoOneHere", "Hello?");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().ContainSingle();
        builder.Broker.ConnectionMessages[0].Message.Should().Contain("401"); // ERR_NOSUCHNICK
    }

    [Fact]
    public async Task HandleAsyncToAwayUserSendsAwayMessage()
    {
        // Arrange
        var builder = new CommandContextBuilder("Sender");
        builder.SetUserRegistered();

        // Add away target user
        var targetUser = new User(
            Guid.NewGuid(),
            IPAddress.Parse("10.0.0.1"),
            "target.example.com",
            ServerId.Create("001", "test.server.com"),
            isSecure: false);
        Nickname.TryCreate("AwayUser", out var targetNick, out _);
        targetUser.SetNickname(targetNick!);
        targetUser.SetUserInfo("target", "Target User");
        targetUser.SetRegistrationState(RegistrationState.Registered);
        targetUser.SetAway("Gone fishing");
        builder.Users.Add(targetUser);

        var message = IrcMessage.Create("PRIVMSG", "AwayUser", "Are you there?");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().HaveCount(2);
        builder.Broker.ConnectionMessages[0].Message.Should().Contain("301"); // RPL_AWAY
        builder.Broker.ConnectionMessages[0].Message.Should().Contain("Gone fishing");
    }

    [Fact]
    public async Task HandleAsyncWithModeratedChannelAndNoVoiceRejectsMessage()
    {
        // Arrange
        var builder = new CommandContextBuilder("TestNick");
        builder.SetUserRegistered();
        
        // Create moderated channel
        ChannelName.TryCreate("#moderated", out var channelName, out _);
        var channel = builder.Channels.Create(channelName!);
        channel.AddMode(ChannelMode.Moderated);
        channel.AddMember(builder.User, ChannelMemberMode.None);
        builder.User.JoinChannel(channelName!);

        var message = IrcMessage.Create("PRIVMSG", "#moderated", "Can I speak?");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().ContainSingle();
        builder.Broker.ConnectionMessages[0].Message.Should().Contain("404"); // ERR_CANNOTSENDTOCHAN
    }
}

/// <summary>
/// Tests for the NoticeHandler.
/// </summary>
public class NoticeHandlerTests
{
    private readonly NoticeHandler _handler = new();

    [Fact]
    public void CommandReturnsNotice()
    {
        _handler.Command.Should().Be("NOTICE");
    }

    [Fact]
    public async Task HandleAsyncWithEmptyTextDoesNothing()
    {
        // Arrange
        var builder = new CommandContextBuilder("TestNick");
        builder.SetUserRegistered();
        var message = IrcMessage.Create("NOTICE", "#test", "");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert - NOTICE should not generate replies for errors per RFC
        builder.Broker.ConnectionMessages.Should().BeEmpty();
        builder.Broker.ChannelMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsyncToChannelSendsToChannel()
    {
        // Arrange
        var builder = new CommandContextBuilder("TestNick");
        builder.SetUserRegistered();
        
        ChannelName.TryCreate("#test", out var channelName, out _);
        var channel = builder.Channels.Create(channelName!);
        channel.AddMember(builder.User);

        var message = IrcMessage.Create("NOTICE", "#test", "Channel notice");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.Broker.ChannelMessages.Should().ContainSingle();
        builder.Broker.ChannelMessages[0].Message.Should().Contain("NOTICE");
        builder.Broker.ChannelMessages[0].Message.Should().Contain("Channel notice");
    }
}

/// <summary>
/// Tests for the JoinHandler.
/// </summary>
public class JoinHandlerTests
{
    private readonly JoinHandler _handler = new();

    [Fact]
    public void CommandReturnsJoin()
    {
        _handler.Command.Should().Be("JOIN");
    }

    [Fact]
    public void MinimumParametersIsOne()
    {
        _handler.MinimumParameters.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsyncCreatesNewChannel()
    {
        // Arrange
        var builder = new CommandContextBuilder("TestNick");
        builder.SetUserRegistered();
        var message = IrcMessage.Create("JOIN", "#newchannel");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        ChannelName.TryCreate("#newchannel", out var channelName, out _);
        var channel = builder.Channels.GetByName(channelName!);
        channel.Should().NotBeNull();
        channel!.HasMember(builder.User.ConnectionId).Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsyncGivesOpToChannelCreator()
    {
        // Arrange
        var builder = new CommandContextBuilder("TestNick");
        builder.SetUserRegistered();
        var message = IrcMessage.Create("JOIN", "#newchannel");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        ChannelName.TryCreate("#newchannel", out var channelName, out _);
        var channel = builder.Channels.GetByName(channelName!);
        var member = channel!.GetMember(builder.User.ConnectionId);
        member!.Modes.HasFlag(ChannelMemberMode.Op).Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsyncSendsJoinMessageToChannel()
    {
        // Arrange
        var builder = new CommandContextBuilder("TestNick");
        builder.SetUserRegistered();
        var message = IrcMessage.Create("JOIN", "#newchannel");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.Broker.ChannelMessages.Should().ContainSingle();
        builder.Broker.ChannelMessages[0].Message.Should().Contain("JOIN");
    }

    [Fact]
    public async Task HandleAsyncRejectsInviteOnlyChannel()
    {
        // Arrange
        var builder = new CommandContextBuilder("TestNick");
        builder.SetUserRegistered();

        // Create invite-only channel
        ChannelName.TryCreate("#private", out var channelName, out _);
        var channel = builder.Channels.Create(channelName!);
        channel.AddMode(ChannelMode.InviteOnly);

        var message = IrcMessage.Create("JOIN", "#private");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().ContainSingle();
        builder.Broker.ConnectionMessages[0].Message.Should().Contain("473"); // ERR_INVITEONLYCHAN
    }

    [Fact]
    public async Task HandleAsyncRejectsBadChannelKey()
    {
        // Arrange
        var builder = new CommandContextBuilder("TestNick");
        builder.SetUserRegistered();

        // Create keyed channel
        ChannelName.TryCreate("#secret", out var channelName, out _);
        var channel = builder.Channels.Create(channelName!);
        channel.SetKey("correctkey");

        var message = IrcMessage.Create("JOIN", "#secret", "wrongkey");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().ContainSingle();
        builder.Broker.ConnectionMessages[0].Message.Should().Contain("475"); // ERR_BADCHANNELKEY
    }

    [Fact]
    public async Task HandleAsyncAllowsJoinWithCorrectKey()
    {
        // Arrange
        var builder = new CommandContextBuilder("TestNick");
        builder.SetUserRegistered();

        // Create keyed channel
        ChannelName.TryCreate("#secret", out var channelName, out _);
        var channel = builder.Channels.Create(channelName!);
        channel.SetKey("correctkey");

        var message = IrcMessage.Create("JOIN", "#secret", "correctkey");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        channel.HasMember(builder.User.ConnectionId).Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsyncRejectsBannedUser()
    {
        // Arrange
        var builder = new CommandContextBuilder("TestNick");
        builder.SetUserRegistered();

        // Create channel with ban
        ChannelName.TryCreate("#banned", out var channelName, out _);
        var channel = builder.Channels.Create(channelName!);
        channel.AddBan("*!*@client.example.com", "admin");

        var message = IrcMessage.Create("JOIN", "#banned");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().ContainSingle();
        builder.Broker.ConnectionMessages[0].Message.Should().Contain("474"); // ERR_BANNEDFROMCHAN
    }

    [Fact]
    public async Task HandleAsyncRejectsFullChannel()
    {
        // Arrange
        var builder = new CommandContextBuilder("TestNick");
        builder.SetUserRegistered();

        // Create channel with limit of 1
        ChannelName.TryCreate("#full", out var channelName, out _);
        var channel = builder.Channels.Create(channelName!);
        channel.SetLimit(1);

        // Add one user to fill it
        var otherUser = new User(
            Guid.NewGuid(),
            IPAddress.Parse("10.0.0.1"),
            "other.example.com",
            ServerId.Create("001", "test.server.com"),
            isSecure: false);
        Nickname.TryCreate("OtherUser", out var otherNick, out _);
        otherUser.SetNickname(otherNick!);
        channel.AddMember(otherUser);

        var message = IrcMessage.Create("JOIN", "#full");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().ContainSingle();
        builder.Broker.ConnectionMessages[0].Message.Should().Contain("471"); // ERR_CHANNELISFULL
    }

    [Fact]
    public async Task HandleAsyncWithMultipleChannelsJoinsAll()
    {
        // Arrange
        var builder = new CommandContextBuilder("TestNick");
        builder.SetUserRegistered();
        var message = IrcMessage.Create("JOIN", "#chan1,#chan2,#chan3");
        var context = builder.Build(message);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        builder.User.Channels.Count.Should().Be(3);
    }
}
