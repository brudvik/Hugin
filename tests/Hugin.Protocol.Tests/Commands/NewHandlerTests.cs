using System.Net;
using FluentAssertions;
using Hugin.Core.Entities;
using Hugin.Core.Enums;
using Hugin.Core.ValueObjects;
using Hugin.Protocol.Commands;
using Hugin.Protocol.Commands.Handlers;
using Xunit;

namespace Hugin.Protocol.Tests.Commands;

/// <summary>
/// Tests for the QueryHandlers (WHOIS, WHO, LIST, NAMES).
/// </summary>
public class QueryHandlerTests
{
    private static readonly ServerId TestServerId = ServerId.Create("001", "test.server.com");

    #region WHOIS Handler Tests

    [Fact]
    public async Task WhoisHandlerReturnsUserInfoForValidNickname()
    {
        // Arrange
        var handler = new WhoisHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var targetNick = Nickname.TryCreate("target", out var nick, out _) ? nick! : null!;
        var targetUser = new User(Guid.NewGuid(), IPAddress.Loopback, "target.host", TestServerId, true);
        targetUser.SetNickname(targetNick);
        targetUser.SetUserInfo("targetuser", "Target User");
        builder.Users.Add(targetUser);

        var message = IrcMessage.Create("WHOIS", "target");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("311")); // RPL_WHOISUSER
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("318")); // RPL_ENDOFWHOIS
    }

    [Fact]
    public async Task WhoisHandlerReturnsNoSuchNickForInvalidNickname()
    {
        // Arrange
        var handler = new WhoisHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("WHOIS", "nonexistent");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("401")); // ERR_NOSUCHNICK
    }

    [Fact]
    public async Task WhoisHandlerShowsSecureConnectionStatus()
    {
        // Arrange
        var handler = new WhoisHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var targetNick = Nickname.TryCreate("secureuser", out var nick, out _) ? nick! : null!;
        var targetUser = new User(Guid.NewGuid(), IPAddress.Loopback, "secure.host", TestServerId, true);
        targetUser.SetNickname(targetNick);
        targetUser.SetUserInfo("secureuser", "Secure User");
        builder.Users.Add(targetUser);

        var message = IrcMessage.Create("WHOIS", "secureuser");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("671")); // RPL_WHOISSECURE
    }

    [Fact]
    public async Task WhoisHandlerShowsAwayMessage()
    {
        // Arrange
        var handler = new WhoisHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var targetNick = Nickname.TryCreate("awayuser", out var nick, out _) ? nick! : null!;
        var targetUser = new User(Guid.NewGuid(), IPAddress.Loopback, "away.host", TestServerId, false);
        targetUser.SetNickname(targetNick);
        targetUser.SetUserInfo("awayuser", "Away User");
        targetUser.SetAway("Gone for lunch");
        builder.Users.Add(targetUser);

        var message = IrcMessage.Create("WHOIS", "awayuser");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("301")); // RPL_AWAY
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("Gone for lunch"));
    }

    #endregion

    #region WHO Handler Tests

    [Fact]
    public async Task WhoHandlerReturnsChannelMembers()
    {
        // Arrange
        var handler = new WhoHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var channelName = ChannelName.TryCreate("#test", out var cn, out _) ? cn! : null!;
        var channel = builder.Channels.Create(channelName);

        var targetNick = Nickname.TryCreate("member", out var nick, out _) ? nick! : null!;
        var targetUser = new User(Guid.NewGuid(), IPAddress.Loopback, "member.host", TestServerId, false);
        targetUser.SetNickname(targetNick);
        targetUser.SetUserInfo("memberuser", "Member User");
        builder.Users.Add(targetUser);
        channel.AddMember(targetUser, ChannelMemberMode.Op);

        var message = IrcMessage.Create("WHO", "#test");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("352")); // RPL_WHOREPLY
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("315")); // RPL_ENDOFWHO
    }

    [Fact]
    public async Task WhoHandlerReturnsEndOfWhoForNonexistentChannel()
    {
        // Arrange
        var handler = new WhoHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("WHO", "#nonexistent");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("315")); // RPL_ENDOFWHO
    }

    [Fact]
    public async Task WhoHandlerWithWhoxReturnsExtendedReply()
    {
        // Arrange
        var handler = new WhoHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();
        builder.User.AddMode(UserMode.Operator); // Oper can see IP

        var channelName = ChannelName.TryCreate("#test", out var cn, out _) ? cn! : null!;
        var channel = builder.Channels.Create(channelName);

        var targetNick = Nickname.TryCreate("member", out var nick, out _) ? nick! : null!;
        var targetUser = new User(Guid.NewGuid(), IPAddress.Parse("192.168.1.100"), "member.host", TestServerId, false);
        targetUser.SetNickname(targetNick);
        targetUser.SetUserInfo("memberuser", "Member User");
        builder.Users.Add(targetUser);
        channel.AddMember(targetUser, ChannelMemberMode.Op);

        // WHOX format: WHO #test %tcuihsnfdlar,123
        var message = IrcMessage.Create("WHO", "#test", "%tcuihsnfar,123");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert - WHOX uses 354 instead of 352
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("354")); // RPL_WHOSPCRPL
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("315")); // RPL_ENDOFWHO
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("123")); // Query type token
    }

    [Fact]
    public async Task WhoHandlerWithWhoxAccountFieldReturnsAccountInfo()
    {
        // Arrange
        var handler = new WhoHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var channelName = ChannelName.TryCreate("#test", out var cn, out _) ? cn! : null!;
        var channel = builder.Channels.Create(channelName);

        var targetNick = Nickname.TryCreate("member", out var nick, out _) ? nick! : null!;
        var targetUser = new User(Guid.NewGuid(), IPAddress.Loopback, "member.host", TestServerId, false);
        targetUser.SetNickname(targetNick);
        targetUser.SetUserInfo("memberuser", "Member User");
        targetUser.SetAuthenticated("memberaccount");
        builder.Users.Add(targetUser);
        channel.AddMember(targetUser, ChannelMemberMode.Voice);

        // WHOX with only account field
        var message = IrcMessage.Create("WHO", "#test", "%na");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("354")); // RPL_WHOSPCRPL
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("memberaccount"));
    }

    [Fact]
    public void WhoxRequestParseWithValidFormatReturnsRequest()
    {
        // Act
        var result = WhoxRequest.Parse("o%tcuihsnfdlar,999");

        // Assert
        result.Should().NotBeNull();
        result!.OperOnly.Should().BeTrue();
        result.QueryType.Should().Be("999");
        result.HasQueryType.Should().BeTrue();
        result.HasChannel.Should().BeTrue();
        result.HasUsername.Should().BeTrue();
        result.HasIp.Should().BeTrue();
        result.HasHost.Should().BeTrue();
        result.HasServer.Should().BeTrue();
        result.HasNick.Should().BeTrue();
        result.HasFlags.Should().BeTrue();
        result.HasHopcount.Should().BeTrue();
        result.HasIdle.Should().BeTrue();
        result.HasAccount.Should().BeTrue();
        result.HasRealname.Should().BeTrue();
    }

    [Fact]
    public void WhoxRequestParseWithoutPercentReturnsNull()
    {
        // Act
        var result = WhoxRequest.Parse("o");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void WhoxRequestParseWithEmptyInputReturnsNull()
    {
        // Act
        var result = WhoxRequest.Parse("");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region LIST Handler Tests

    [Fact]
    public async Task ListHandlerReturnsAllChannels()
    {
        // Arrange
        var handler = new ListHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var channelName1 = ChannelName.TryCreate("#channel1", out var cn1, out _) ? cn1! : null!;
        var channelName2 = ChannelName.TryCreate("#channel2", out var cn2, out _) ? cn2! : null!;
        builder.Channels.Create(channelName1);
        builder.Channels.Create(channelName2);

        var message = IrcMessage.Create("LIST");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("323")); // RPL_LISTEND
    }

    [Fact]
    public async Task ListHandlerHidesSecretChannels()
    {
        // Arrange
        var handler = new ListHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var channelName = ChannelName.TryCreate("#secret", out var cn, out _) ? cn! : null!;
        var channel = builder.Channels.Create(channelName);
        channel.AddMode(ChannelMode.Secret);

        var message = IrcMessage.Create("LIST");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        // The 322 (RPL_LIST) response should not contain the secret channel
        builder.Broker.ConnectionMessages.Where(m => m.Message.Contains("322"))
            .Should().NotContain(m => m.Message.Contains("#secret"));
    }

    #endregion

    #region NAMES Handler Tests

    [Fact]
    public async Task NamesHandlerReturnsChannelMembers()
    {
        // Arrange
        var handler = new NamesHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var channelName = ChannelName.TryCreate("#test", out var cn, out _) ? cn! : null!;
        var channel = builder.Channels.Create(channelName);

        var targetNick = Nickname.TryCreate("member", out var nick, out _) ? nick! : null!;
        var targetUser = new User(Guid.NewGuid(), IPAddress.Loopback, "member.host", TestServerId, false);
        targetUser.SetNickname(targetNick);
        targetUser.SetUserInfo("memberuser", "Member User");
        builder.Users.Add(targetUser);
        channel.AddMember(targetUser, ChannelMemberMode.Op);

        var message = IrcMessage.Create("NAMES", "#test");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("353")); // RPL_NAMREPLY
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("366")); // RPL_ENDOFNAMES
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("@member")); // Op prefix
    }

    [Fact]
    public async Task NamesHandlerReturnsEndOfNamesForNonexistentChannel()
    {
        // Arrange
        var handler = new NamesHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("NAMES", "#nonexistent");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("366")); // RPL_ENDOFNAMES
    }

    #endregion
}

/// <summary>
/// Tests for the UtilityHandlers (TOPIC, AWAY, INVITE, MOTD, LUSERS, PASS).
/// </summary>
public class UtilityHandlerTests
{
    private static readonly ServerId TestServerId = ServerId.Create("001", "test.server.com");

    #region TOPIC Handler Tests

    [Fact]
    public async Task TopicHandlerReturnsTopic()
    {
        // Arrange
        var handler = new TopicHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var channelName = ChannelName.TryCreate("#test", out var cn, out _) ? cn! : null!;
        var channel = builder.Channels.Create(channelName);
        channel.SetTopic("Test topic", "setter!user@host");

        var message = IrcMessage.Create("TOPIC", "#test");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("332")); // RPL_TOPIC
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("Test topic"));
    }

    [Fact]
    public async Task TopicHandlerReturnsNoTopic()
    {
        // Arrange
        var handler = new TopicHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var channelName = ChannelName.TryCreate("#test", out var cn, out _) ? cn! : null!;
        builder.Channels.Create(channelName);

        var message = IrcMessage.Create("TOPIC", "#test");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("331")); // RPL_NOTOPIC
    }

    [Fact]
    public async Task TopicHandlerSetsTopic()
    {
        // Arrange
        var handler = new TopicHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var channelName = ChannelName.TryCreate("#test", out var cn, out _) ? cn! : null!;
        var channel = builder.Channels.Create(channelName);
        channel.AddMember(builder.User, ChannelMemberMode.Op);
        builder.User.JoinChannel(channelName, ChannelMemberMode.Op);

        var message = IrcMessage.Create("TOPIC", "#test", "New topic");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        channel.Topic.Should().Be("New topic");
    }

    [Fact]
    public async Task TopicHandlerDeniesNonOpInProtectedChannel()
    {
        // Arrange
        var handler = new TopicHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var channelName = ChannelName.TryCreate("#test", out var cn, out _) ? cn! : null!;
        var channel = builder.Channels.Create(channelName);
        channel.AddMode(ChannelMode.TopicProtected);
        channel.AddMember(builder.User, ChannelMemberMode.None);
        builder.User.JoinChannel(channelName, ChannelMemberMode.None);

        var message = IrcMessage.Create("TOPIC", "#test", "New topic");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("482")); // ERR_CHANOPRIVSNEEDED
    }

    #endregion

    #region AWAY Handler Tests

    [Fact]
    public async Task AwayHandlerSetsAway()
    {
        // Arrange
        var handler = new AwayHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("AWAY", "Gone for lunch");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("306")); // RPL_NOWAWAY
        builder.User.IsAway.Should().BeTrue();
        builder.User.AwayMessage.Should().Be("Gone for lunch");
    }

    [Fact]
    public async Task AwayHandlerUnsetsAway()
    {
        // Arrange
        var handler = new AwayHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();
        builder.User.SetAway("Was away");

        var message = IrcMessage.Create("AWAY");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("305")); // RPL_UNAWAY
        builder.User.IsAway.Should().BeFalse();
    }

    #endregion

    #region INVITE Handler Tests

    [Fact]
    public async Task InviteHandlerInvitesUser()
    {
        // Arrange
        var handler = new InviteHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var channelName = ChannelName.TryCreate("#test", out var cn, out _) ? cn! : null!;
        var channel = builder.Channels.Create(channelName);
        channel.AddMember(builder.User, ChannelMemberMode.Op);
        builder.User.JoinChannel(channelName, ChannelMemberMode.Op);

        var targetNick = Nickname.TryCreate("target", out var nick, out _) ? nick! : null!;
        var targetUser = new User(Guid.NewGuid(), IPAddress.Loopback, "target.host", TestServerId, false);
        targetUser.SetNickname(targetNick);
        targetUser.SetUserInfo("targetuser", "Target User");
        builder.Users.Add(targetUser);

        var message = IrcMessage.Create("INVITE", "target", "#test");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("341")); // RPL_INVITING
        channel.IsInvited(targetUser.ConnectionId).Should().BeTrue();
    }

    [Fact]
    public async Task InviteHandlerReturnsUserOnChannel()
    {
        // Arrange
        var handler = new InviteHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var channelName = ChannelName.TryCreate("#test", out var cn, out _) ? cn! : null!;
        var channel = builder.Channels.Create(channelName);
        channel.AddMember(builder.User, ChannelMemberMode.Op);
        builder.User.JoinChannel(channelName, ChannelMemberMode.Op);

        var targetNick = Nickname.TryCreate("target", out var nick, out _) ? nick! : null!;
        var targetUser = new User(Guid.NewGuid(), IPAddress.Loopback, "target.host", TestServerId, false);
        targetUser.SetNickname(targetNick);
        targetUser.SetUserInfo("targetuser", "Target User");
        builder.Users.Add(targetUser);
        channel.AddMember(targetUser);

        var message = IrcMessage.Create("INVITE", "target", "#test");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("443")); // ERR_USERONCHANNEL
    }

    #endregion

    #region MOTD Handler Tests

    [Fact]
    public async Task MotdHandlerReturnsMOTD()
    {
        // Arrange
        var handler = new MotdHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("MOTD");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("375")); // RPL_MOTDSTART
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("372")); // RPL_MOTD
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("376")); // RPL_ENDOFMOTD
    }

    #endregion

    #region LUSERS Handler Tests

    [Fact]
    public async Task LusersHandlerReturnsStats()
    {
        // Arrange
        var handler = new LusersHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("LUSERS");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("251")); // RPL_LUSERCLIENT
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("254")); // RPL_LUSERCHANNELS
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("255")); // RPL_LUSERME
    }

    #endregion

    #region PASS Handler Tests

    [Fact]
    public async Task PassHandlerAcceptsPassword()
    {
        // Arrange
        var handler = new PassHandler();
        var builder = new CommandContextBuilder();

        var message = IrcMessage.Create("PASS", "serverpassword");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        // PASS should not produce any output on success
        builder.Broker.ConnectionMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task PassHandlerRejectsAfterRegistration()
    {
        // Arrange
        var handler = new PassHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("PASS", "serverpassword");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("462")); // ERR_ALREADYREGISTERED
    }

    #endregion
}

/// <summary>
/// Tests for the ModeHandler.
/// </summary>
public class ModeHandlerTests
{
    private static readonly ServerId TestServerId = ServerId.Create("001", "test.server.com");

    #region User Mode Tests

    [Fact]
    public async Task ModeHandlerReturnsUserModes()
    {
        // Arrange
        var handler = new ModeHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();
        builder.User.AddMode(UserMode.Invisible);

        var message = IrcMessage.Create("MODE", builder.User.Nickname.Value);
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("221")); // RPL_UMODEIS
    }

    [Fact]
    public async Task ModeHandlerSetsUserMode()
    {
        // Arrange
        var handler = new ModeHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("MODE", builder.User.Nickname.Value, "+i");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.User.Modes.HasFlag(UserMode.Invisible).Should().BeTrue();
    }

    [Fact]
    public async Task ModeHandlerRemovesUserMode()
    {
        // Arrange
        var handler = new ModeHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();
        builder.User.AddMode(UserMode.Invisible);

        var message = IrcMessage.Create("MODE", builder.User.Nickname.Value, "-i");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.User.Modes.HasFlag(UserMode.Invisible).Should().BeFalse();
    }

    [Fact]
    public async Task ModeHandlerRejectsChangingOtherUserModes()
    {
        // Arrange
        var handler = new ModeHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("MODE", "otheruser", "+i");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("502")); // ERR_USERSDONTMATCH
    }

    #endregion

    #region Channel Mode Tests

    [Fact]
    public async Task ModeHandlerReturnsChannelModes()
    {
        // Arrange
        var handler = new ModeHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var channelName = ChannelName.TryCreate("#test", out var cn, out _) ? cn! : null!;
        var channel = builder.Channels.Create(channelName);
        channel.AddMode(ChannelMode.NoExternalMessages);

        var message = IrcMessage.Create("MODE", "#test");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("324")); // RPL_CHANNELMODEIS
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("329")); // RPL_CREATIONTIME
    }

    [Fact]
    public async Task ModeHandlerSetsChannelMode()
    {
        // Arrange
        var handler = new ModeHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var channelName = ChannelName.TryCreate("#test", out var cn, out _) ? cn! : null!;
        var channel = builder.Channels.Create(channelName);
        channel.AddMember(builder.User, ChannelMemberMode.Op);
        builder.User.JoinChannel(channelName, ChannelMemberMode.Op);

        var message = IrcMessage.Create("MODE", "#test", "+m");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        channel.Modes.HasFlag(ChannelMode.Moderated).Should().BeTrue();
    }

    [Fact]
    public async Task ModeHandlerSetsMemberMode()
    {
        // Arrange
        var handler = new ModeHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var channelName = ChannelName.TryCreate("#test", out var cn, out _) ? cn! : null!;
        var channel = builder.Channels.Create(channelName);
        channel.AddMember(builder.User, ChannelMemberMode.Op);
        builder.User.JoinChannel(channelName, ChannelMemberMode.Op);

        var targetNick = Nickname.TryCreate("target", out var nick, out _) ? nick! : null!;
        var targetUser = new User(Guid.NewGuid(), IPAddress.Loopback, "target.host", TestServerId, false);
        targetUser.SetNickname(targetNick);
        targetUser.SetUserInfo("targetuser", "Target User");
        builder.Users.Add(targetUser);
        channel.AddMember(targetUser);

        var message = IrcMessage.Create("MODE", "#test", "+v", "target");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        var member = channel.GetMember(targetUser.ConnectionId);
        member!.Modes.HasFlag(ChannelMemberMode.Voice).Should().BeTrue();
    }

    [Fact]
    public async Task ModeHandlerRejectsNonOpChangingModes()
    {
        // Arrange
        var handler = new ModeHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var channelName = ChannelName.TryCreate("#test", out var cn, out _) ? cn! : null!;
        var channel = builder.Channels.Create(channelName);
        channel.AddMember(builder.User, ChannelMemberMode.None);
        builder.User.JoinChannel(channelName, ChannelMemberMode.None);

        var message = IrcMessage.Create("MODE", "#test", "+m");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("482")); // ERR_CHANOPRIVSNEEDED
    }

    [Fact]
    public async Task ModeHandlerSetsChannelKey()
    {
        // Arrange
        var handler = new ModeHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var channelName = ChannelName.TryCreate("#test", out var cn, out _) ? cn! : null!;
        var channel = builder.Channels.Create(channelName);
        channel.AddMember(builder.User, ChannelMemberMode.Op);
        builder.User.JoinChannel(channelName, ChannelMemberMode.Op);

        var message = IrcMessage.Create("MODE", "#test", "+k", "secret");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        channel.Key.Should().Be("secret");
        channel.Modes.HasFlag(ChannelMode.Key).Should().BeTrue();
    }

    [Fact]
    public async Task ModeHandlerSetsChannelLimit()
    {
        // Arrange
        var handler = new ModeHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var channelName = ChannelName.TryCreate("#test", out var cn, out _) ? cn! : null!;
        var channel = builder.Channels.Create(channelName);
        channel.AddMember(builder.User, ChannelMemberMode.Op);
        builder.User.JoinChannel(channelName, ChannelMemberMode.Op);

        var message = IrcMessage.Create("MODE", "#test", "+l", "50");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        channel.UserLimit.Should().Be(50);
        channel.Modes.HasFlag(ChannelMode.Limit).Should().BeTrue();
    }

    [Fact]
    public async Task ModeHandlerAddsChannelBan()
    {
        // Arrange
        var handler = new ModeHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var channelName = ChannelName.TryCreate("#test", out var cn, out _) ? cn! : null!;
        var channel = builder.Channels.Create(channelName);
        channel.AddMember(builder.User, ChannelMemberMode.Op);
        builder.User.JoinChannel(channelName, ChannelMemberMode.Op);

        var message = IrcMessage.Create("MODE", "#test", "+b", "*!*@evil.host");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        channel.Bans.Should().Contain(b => b.Mask == "*!*@evil.host");
    }

    #endregion
}

/// <summary>
/// Tests for the ServerInfoHandlers (VERSION, TIME, INFO, ADMIN, USERHOST, ISON, SETNAME, OPER).
/// </summary>
public class ServerInfoHandlerTests
{
    private static readonly ServerId TestServerId = ServerId.Create("001", "test.server.com");

    #region VERSION Handler Tests

    [Fact]
    public async Task VersionHandlerReturnsVersionInfo()
    {
        // Arrange
        var handler = new VersionHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("VERSION");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("351")); // RPL_VERSION
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("hugin"));
    }

    #endregion

    #region TIME Handler Tests

    [Fact]
    public async Task TimeHandlerReturnsServerTime()
    {
        // Arrange
        var handler = new TimeHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("TIME");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("391")); // RPL_TIME
    }

    #endregion

    #region INFO Handler Tests

    [Fact]
    public async Task InfoHandlerReturnsServerInfo()
    {
        // Arrange
        var handler = new InfoHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("INFO");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("371")); // RPL_INFO
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("374")); // RPL_ENDOFINFO
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("Hugin"));
    }

    #endregion

    #region ADMIN Handler Tests

    [Fact]
    public async Task AdminHandlerReturnsAdminInfo()
    {
        // Arrange
        var handler = new AdminHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("ADMIN");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("256")); // RPL_ADMINME
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("257")); // RPL_ADMINLOC1
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("258")); // RPL_ADMINLOC2
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("259")); // RPL_ADMINEMAIL
    }

    #endregion

    #region USERHOST Handler Tests

    [Fact]
    public async Task UserhostHandlerReturnsUserInfo()
    {
        // Arrange
        var handler = new UserhostHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        // Add a target user
        var targetNick = Nickname.TryCreate("targetuser", out var nick, out _) ? nick! : null!;
        var targetUser = new User(Guid.NewGuid(), IPAddress.Loopback, "target.host", TestServerId, false);
        targetUser.SetNickname(targetNick);
        targetUser.SetUserInfo("targetuser", "Target User");
        builder.Users.Add(targetUser);

        var message = IrcMessage.Create("USERHOST", "targetuser");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("302")); // RPL_USERHOST
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("targetuser"));
    }

    [Fact]
    public async Task UserhostHandlerReturnsEmptyForNonexistentUser()
    {
        // Arrange
        var handler = new UserhostHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("USERHOST", "nonexistent");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("302")); // RPL_USERHOST
    }

    [Fact]
    public async Task UserhostHandlerShowsOperatorStatus()
    {
        // Arrange
        var handler = new UserhostHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        // Add an operator user
        var targetNick = Nickname.TryCreate("operuser", out var nick, out _) ? nick! : null!;
        var targetUser = new User(Guid.NewGuid(), IPAddress.Loopback, "oper.host", TestServerId, false);
        targetUser.SetNickname(targetNick);
        targetUser.SetUserInfo("operuser", "Oper User");
        targetUser.AddMode(UserMode.Operator);
        builder.Users.Add(targetUser);

        var message = IrcMessage.Create("USERHOST", "operuser");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("302")); // RPL_USERHOST
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("*=")); // Operator flag
    }

    #endregion

    #region ISON Handler Tests

    [Fact]
    public async Task IsonHandlerReturnsOnlineUsers()
    {
        // Arrange
        var handler = new IsonHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        // Add target users
        var nick1 = Nickname.TryCreate("user1", out var n1, out _) ? n1! : null!;
        var user1 = new User(Guid.NewGuid(), IPAddress.Loopback, "user1.host", TestServerId, false);
        user1.SetNickname(nick1);
        user1.SetUserInfo("user1", "User One");
        builder.Users.Add(user1);

        var nick2 = Nickname.TryCreate("user2", out var n2, out _) ? n2! : null!;
        var user2 = new User(Guid.NewGuid(), IPAddress.Loopback, "user2.host", TestServerId, false);
        user2.SetNickname(nick2);
        user2.SetUserInfo("user2", "User Two");
        builder.Users.Add(user2);

        var message = IrcMessage.Create("ISON", "user1", "user2", "nonexistent");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("303")); // RPL_ISON
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("user1"));
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("user2"));
    }

    [Fact]
    public async Task IsonHandlerReturnsEmptyForNoOnlineUsers()
    {
        // Arrange
        var handler = new IsonHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("ISON", "nonexistent1", "nonexistent2");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("303")); // RPL_ISON
    }

    #endregion

    #region SETNAME Handler Tests

    [Fact]
    public async Task SetnameHandlerChangesRealname()
    {
        // Arrange
        var handler = new SetnameHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("SETNAME", "New Realname");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.User.RealName.Should().Be("New Realname");
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("SETNAME"));
    }

    [Fact]
    public async Task SetnameHandlerRejectsEmptyRealname()
    {
        // Arrange
        var handler = new SetnameHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("SETNAME", "   ");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("FAIL"));
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("INVALID_REALNAME"));
    }

    #endregion

    #region OPER Handler Tests

    [Fact]
    public async Task OperHandlerGrantsOperatorStatus()
    {
        // Arrange
        var handler = new OperHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("OPER", "admin", "admin123");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.User.Modes.HasFlag(UserMode.Operator).Should().BeTrue();
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("381")); // RPL_YOUREOPER
    }

    [Fact]
    public async Task OperHandlerRejectsInvalidPassword()
    {
        // Arrange
        var handler = new OperHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("OPER", "admin", "wrongpassword");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.User.Modes.HasFlag(UserMode.Operator).Should().BeFalse();
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("464")); // ERR_PASSWDMISMATCH
    }

    [Fact]
    public async Task OperHandlerRejectsUnknownOperName()
    {
        // Arrange
        var handler = new OperHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("OPER", "unknownoper", "password");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.User.Modes.HasFlag(UserMode.Operator).Should().BeFalse();
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("464")); // ERR_PASSWDMISMATCH
    }

    #endregion
}

/// <summary>
/// Tests for the OperatorHandlers (WHOWAS, KILL, WALLOPS, STATS).
/// </summary>
public class OperatorHandlerTests
{
    private static readonly ServerId TestServerId = ServerId.Create("001", "test.server.com");

    #region WHOWAS Handler Tests

    [Fact]
    public async Task WhowasHandlerReturnsWasNoSuchNickForUnknownNickname()
    {
        // Arrange
        var handler = new WhowasHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("WHOWAS", "unknownnick");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("406")); // ERR_WASNOSUCHNICK
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("369")); // RPL_ENDOFWHOWAS
    }

    [Fact]
    public async Task WhowasHandlerRecordsUserCorrectly()
    {
        // Arrange
        var targetNick = Nickname.TryCreate("departing", out var nick, out _) ? nick! : null!;
        var targetUser = new User(Guid.NewGuid(), System.Net.IPAddress.Loopback, "departing.host", TestServerId, false);
        targetUser.SetNickname(targetNick);
        targetUser.SetUserInfo("departinguser", "Departing User");

        // Record the user
        WhowasHandler.RecordUser(targetUser, "test.server.com");

        var handler = new WhowasHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("WHOWAS", "departing");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("314")); // RPL_WHOWASUSER
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("312")); // RPL_WHOISSERVER
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("369")); // RPL_ENDOFWHOWAS
    }

    [Fact]
    public async Task WhowasHandlerRespectsCountParameter()
    {
        // Arrange
        var targetNick = Nickname.TryCreate("multientry", out var nick, out _) ? nick! : null!;
        
        // Record multiple entries
        for (int i = 0; i < 5; i++)
        {
            var user = new User(Guid.NewGuid(), System.Net.IPAddress.Loopback, $"host{i}.example.com", TestServerId, false);
            user.SetNickname(targetNick);
            user.SetUserInfo($"user{i}", $"Real Name {i}");
            WhowasHandler.RecordUser(user, "test.server.com");
        }

        var handler = new WhowasHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        // Request only 2 entries
        var message = IrcMessage.Create("WHOWAS", "multientry", "2");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        var whowasUserResponses = builder.Broker.ConnectionMessages.Count(m => m.Message.Contains("314"));
        whowasUserResponses.Should().BeLessOrEqualTo(2);
    }

    #endregion

    #region KILL Handler Tests

    [Fact]
    public async Task KillHandlerRequiresOperatorPrivileges()
    {
        // Arrange
        var handler = new KillHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();
        // NOT an operator

        var targetNick = Nickname.TryCreate("victim", out var nick, out _) ? nick! : null!;
        var targetUser = new User(Guid.NewGuid(), System.Net.IPAddress.Loopback, "victim.host", TestServerId, false);
        targetUser.SetNickname(targetNick);
        targetUser.SetUserInfo("victim", "Victim User");
        builder.Users.Add(targetUser);

        var message = IrcMessage.Create("KILL", "victim", "You are being killed");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("481")); // ERR_NOPRIVILEGES
    }

    [Fact]
    public async Task KillHandlerRemovesUserWhenOperator()
    {
        // Arrange
        var handler = new KillHandler();
        var builder = new CommandContextBuilder("operuser");
        builder.SetUserRegistered();
        builder.User.AddMode(UserMode.Operator);

        var targetNick = Nickname.TryCreate("victim", out var nick, out _) ? nick! : null!;
        var targetUser = new User(Guid.NewGuid(), System.Net.IPAddress.Loopback, "victim.host", TestServerId, false);
        targetUser.SetNickname(targetNick);
        targetUser.SetUserInfo("victim", "Victim User");
        builder.Users.Add(targetUser);

        var message = IrcMessage.Create("KILL", "victim", "Goodbye!");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Users.GetByConnectionId(targetUser.ConnectionId).Should().BeNull();
    }

    [Fact]
    public async Task KillHandlerReturnsNoSuchNickForUnknownUser()
    {
        // Arrange
        var handler = new KillHandler();
        var builder = new CommandContextBuilder("operuser");
        builder.SetUserRegistered();
        builder.User.AddMode(UserMode.Operator);

        var message = IrcMessage.Create("KILL", "unknownuser", "Goodbye!");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("401")); // ERR_NOSUCHNICK
    }

    #endregion

    #region WALLOPS Handler Tests

    [Fact]
    public async Task WallopsHandlerRequiresOperatorPrivileges()
    {
        // Arrange
        var handler = new WallopsHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();
        // NOT an operator

        var message = IrcMessage.Create("WALLOPS", "Important message!");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("481")); // ERR_NOPRIVILEGES
    }

    [Fact]
    public async Task WallopsHandlerSendsToOperators()
    {
        // Arrange
        var handler = new WallopsHandler();
        var builder = new CommandContextBuilder("operuser");
        builder.SetUserRegistered();
        builder.User.AddMode(UserMode.Operator);

        // Add an operator who should receive the message
        var operNick = Nickname.TryCreate("oper2", out var nick, out _) ? nick! : null!;
        var oper2 = new User(Guid.NewGuid(), System.Net.IPAddress.Loopback, "oper2.host", TestServerId, false);
        oper2.SetNickname(operNick);
        oper2.SetUserInfo("oper2", "Operator 2");
        oper2.AddMode(UserMode.Operator);
        builder.Users.Add(oper2);

        // Add a user with wallops mode who should also receive
        var wallopsNick = Nickname.TryCreate("wallopsuser", out var wNick, out _) ? wNick! : null!;
        var wallopsUser = new User(Guid.NewGuid(), System.Net.IPAddress.Loopback, "wallops.host", TestServerId, false);
        wallopsUser.SetNickname(wallopsNick);
        wallopsUser.SetUserInfo("wallopsuser", "Wallops User");
        wallopsUser.AddMode(UserMode.Wallops);
        builder.Users.Add(wallopsUser);

        var message = IrcMessage.Create("WALLOPS", "Important announcement!");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert - messages should be sent to operators and users with +w
        var wallopsMessages = builder.Broker.ConnectionMessages.Where(m => m.Message.Contains("WALLOPS"));
        wallopsMessages.Should().HaveCountGreaterOrEqualTo(2);
    }

    #endregion

    #region STATS Handler Tests

    [Fact]
    public async Task StatsHandlerReturnsUptimeForU()
    {
        // Arrange
        var handler = new StatsHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("STATS", "U");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("242")); // RPL_STATSUPTIME
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("219")); // RPL_ENDOFSTATS
    }

    [Fact]
    public async Task StatsHandlerReturnsCommandStatsForM()
    {
        // Arrange
        var handler = new StatsHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("STATS", "M");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("212")); // RPL_STATSCOMMANDS
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("219")); // RPL_ENDOFSTATS
    }

    [Fact]
    public async Task StatsHandlerOlineRequiresOperator()
    {
        // Arrange
        var handler = new StatsHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();
        // NOT an operator

        var message = IrcMessage.Create("STATS", "O");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("481")); // ERR_NOPRIVILEGES
    }

    [Fact]
    public async Task StatsHandlerOlineWorksForOperator()
    {
        // Arrange
        var handler = new StatsHandler();
        var builder = new CommandContextBuilder("operuser");
        builder.SetUserRegistered();
        builder.User.AddMode(UserMode.Operator);

        var message = IrcMessage.Create("STATS", "O");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("243")); // RPL_STATSOLINE
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("219")); // RPL_ENDOFSTATS
    }

    [Fact]
    public async Task StatsHandlerReturnsEndOfStatsForUnknownQuery()
    {
        // Arrange
        var handler = new StatsHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();

        var message = IrcMessage.Create("STATS", "X");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("219")); // RPL_ENDOFSTATS
    }

    #endregion

    #region REHASH Handler Tests

    [Fact]
    public async Task RehashHandlerRequiresOperatorPrivileges()
    {
        // Arrange
        var handler = new RehashHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();
        // NOT an operator

        var message = IrcMessage.Create("REHASH");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("481")); // ERR_NOPRIVILEGES
    }

    [Fact]
    public async Task RehashHandlerSendsRehashingReply()
    {
        // Arrange
        var handler = new RehashHandler();
        var builder = new CommandContextBuilder("operuser");
        builder.SetUserRegistered();
        builder.User.AddMode(UserMode.Operator);

        var message = IrcMessage.Create("REHASH");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("382")); // RPL_REHASHING
    }

    #endregion

    #region DIE Handler Tests

    [Fact]
    public async Task DieHandlerRequiresOperatorPrivileges()
    {
        // Arrange
        var handler = new DieHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();
        // NOT an operator

        var message = IrcMessage.Create("DIE");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("481")); // ERR_NOPRIVILEGES
    }

    [Fact]
    public async Task DieHandlerBroadcastsShutdownNotice()
    {
        // Arrange
        var handler = new DieHandler();
        var builder = new CommandContextBuilder("operuser");
        builder.SetUserRegistered();
        builder.User.AddMode(UserMode.Operator);

        // Add another user to receive the broadcast
        var otherNick = Nickname.TryCreate("otheruser", out var nick, out _) ? nick! : null!;
        var otherUser = new User(Guid.NewGuid(), System.Net.IPAddress.Loopback, "other.host", TestServerId, false);
        otherUser.SetNickname(otherNick);
        otherUser.SetUserInfo("otheruser", "Other User");
        builder.Users.Add(otherUser);

        var message = IrcMessage.Create("DIE");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert - shutdown notice should be sent to all users
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("shutting down"));
    }

    #endregion

    #region RESTART Handler Tests

    [Fact]
    public async Task RestartHandlerRequiresOperatorPrivileges()
    {
        // Arrange
        var handler = new RestartHandler();
        var builder = new CommandContextBuilder("testuser");
        builder.SetUserRegistered();
        // NOT an operator

        var message = IrcMessage.Create("RESTART");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("481")); // ERR_NOPRIVILEGES
    }

    [Fact]
    public async Task RestartHandlerBroadcastsRestartNotice()
    {
        // Arrange
        var handler = new RestartHandler();
        var builder = new CommandContextBuilder("operuser");
        builder.SetUserRegistered();
        builder.User.AddMode(UserMode.Operator);

        // Add another user to receive the broadcast
        var otherNick = Nickname.TryCreate("otheruser2", out var nick, out _) ? nick! : null!;
        var otherUser = new User(Guid.NewGuid(), System.Net.IPAddress.Loopback, "other2.host", TestServerId, false);
        otherUser.SetNickname(otherNick);
        otherUser.SetUserInfo("otheruser2", "Other User 2");
        builder.Users.Add(otherUser);

        var message = IrcMessage.Create("RESTART");
        var context = builder.Build(message);

        // Act
        await handler.HandleAsync(context);

        // Assert - restart notice should be sent to all users
        builder.Broker.ConnectionMessages.Should().Contain(m => m.Message.Contains("restarting"));
    }

    #endregion
}
