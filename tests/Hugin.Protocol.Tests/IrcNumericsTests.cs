using FluentAssertions;
using Hugin.Core.Enums;
using Hugin.Protocol;
using Xunit;

namespace Hugin.Protocol.Tests;

/// <summary>
/// Tests for the IrcNumerics static class.
/// </summary>
public class IrcNumericsTests
{
    private const string Server = "irc.test.com";
    private const string Nick = "TestNick";

    #region CreateNumeric Tests

    [Fact]
    public void CreateNumericFormatsNumericCorrectly()
    {
        // Act
        var message = IrcNumerics.CreateNumeric(Server, NumericReply.RplWelcome, Nick, "Welcome!");

        // Assert
        message.Command.Should().Be("001");
        message.Source.Should().Be(Server);
        message.Parameters.Should().HaveCount(2);
        message.Parameters[0].Should().Be(Nick);
        message.Parameters[1].Should().Be("Welcome!");
    }

    [Fact]
    public void CreateNumericPadsNumericToThreeDigits()
    {
        // Act - RPL_WELCOME is 001
        var message = IrcNumerics.CreateNumeric(Server, NumericReply.RplWelcome, Nick);

        // Assert
        message.Command.Should().Be("001");
    }

    #endregion

    #region Connection Registration Numerics (001-005) Tests

    [Fact]
    public void WelcomeCreates001Message()
    {
        // Act
        var message = IrcNumerics.Welcome(Server, Nick, "TestNick!user@host");

        // Assert
        message.Command.Should().Be("001");
        message.Source.Should().Be(Server);
        message.Parameters.Should().Contain(p => p.Contains("Welcome"));
        message.Parameters.Should().Contain(p => p.Contains("TestNick!user@host"));
    }

    [Fact]
    public void YourHostCreates002Message()
    {
        // Act
        var message = IrcNumerics.YourHost(Server, Nick, "irc.test.com", "1.0.0");

        // Assert
        message.Command.Should().Be("002");
        message.Parameters.Should().Contain(p => p.Contains("irc.test.com"));
        message.Parameters.Should().Contain(p => p.Contains("1.0.0"));
    }

    [Fact]
    public void CreatedCreates003Message()
    {
        // Arrange
        var created = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

        // Act
        var message = IrcNumerics.Created(Server, Nick, created);

        // Assert
        message.Command.Should().Be("003");
        message.Parameters.Should().Contain(p => p.Contains("2024"));
    }

    [Fact]
    public void MyInfoCreates004Message()
    {
        // Act
        var message = IrcNumerics.MyInfo(Server, Nick, "irc.test.com", "1.0.0", "iowsr", "biklmnopstv");

        // Assert
        message.Command.Should().Be("004");
        message.Parameters.Should().Contain("irc.test.com");
        message.Parameters.Should().Contain("1.0.0");
        message.Parameters.Should().Contain("iowsr");
        message.Parameters.Should().Contain("biklmnopstv");
    }

    [Fact]
    public void ISupportCreates005Message()
    {
        // Act
        var message = IrcNumerics.ISupport(Server, Nick, "CHANTYPES=#", "PREFIX=(ov)@+");

        // Assert
        message.Command.Should().Be("005");
        message.Parameters.Should().Contain("CHANTYPES=#");
        message.Parameters.Should().Contain("PREFIX=(ov)@+");
        message.Parameters.Should().Contain(p => p.Contains("supported by this server"));
    }

    #endregion

    #region LUSERS Numerics (251-255, 265-266) Tests

    [Fact]
    public void LuserClientCreates251Message()
    {
        // Act
        var message = IrcNumerics.LuserClient(Server, Nick, 100, 20, 3);

        // Assert
        message.Command.Should().Be("251");
        message.Parameters.Should().Contain(p => p.Contains("100") && p.Contains("20") && p.Contains('3'));
    }

    [Fact]
    public void LuserOpCreates252Message()
    {
        // Act
        var message = IrcNumerics.LuserOp(Server, Nick, 5);

        // Assert
        message.Command.Should().Be("252");
        message.Parameters.Should().Contain("5");
    }

    [Fact]
    public void LuserUnknownCreates253Message()
    {
        // Act
        var message = IrcNumerics.LuserUnknown(Server, Nick, 2);

        // Assert
        message.Command.Should().Be("253");
        message.Parameters.Should().Contain("2");
    }

    [Fact]
    public void LuserChannelsCreates254Message()
    {
        // Act
        var message = IrcNumerics.LuserChannels(Server, Nick, 50);

        // Assert
        message.Command.Should().Be("254");
        message.Parameters.Should().Contain("50");
    }

    [Fact]
    public void LuserMeCreates255Message()
    {
        // Act
        var message = IrcNumerics.LuserMe(Server, Nick, 75, 2);

        // Assert
        message.Command.Should().Be("255");
        message.Parameters.Should().Contain(p => p.Contains("75") && p.Contains('2'));
    }

    [Fact]
    public void LocalUsersCreates265Message()
    {
        // Act
        var message = IrcNumerics.LocalUsers(Server, Nick, 50, 100);

        // Assert
        message.Command.Should().Be("265");
        message.Parameters.Should().Contain(p => p.Contains("50"));
        message.Parameters.Should().Contain(p => p.Contains("100"));
    }

    [Fact]
    public void GlobalUsersCreates266Message()
    {
        // Act
        var message = IrcNumerics.GlobalUsers(Server, Nick, 200, 500);

        // Assert
        message.Command.Should().Be("266");
        message.Parameters.Should().Contain("200");
        message.Parameters.Should().Contain("500");
    }

    #endregion

    #region MOTD Numerics (372, 375, 376, 422) Tests

    [Fact]
    public void MotdStartCreates375Message()
    {
        // Act
        var message = IrcNumerics.MotdStart(Server, Nick, "irc.test.com");

        // Assert
        message.Command.Should().Be("375");
        message.Parameters.Should().Contain(p => p.Contains("irc.test.com"));
    }

    [Fact]
    public void MotdCreates372Message()
    {
        // Act
        var message = IrcNumerics.Motd(Server, Nick, "Welcome to our server!");

        // Assert
        message.Command.Should().Be("372");
        message.Parameters.Should().Contain(p => p.Contains("Welcome to our server!"));
    }

    [Fact]
    public void EndOfMotdCreates376Message()
    {
        // Act
        var message = IrcNumerics.EndOfMotd(Server, Nick);

        // Assert
        message.Command.Should().Be("376");
        message.Parameters.Should().Contain(p => p.Contains("End of"));
    }

    [Fact]
    public void NoMotdCreates422Message()
    {
        // Act
        var message = IrcNumerics.NoMotd(Server, Nick);

        // Assert
        message.Command.Should().Be("422");
        message.Parameters.Should().Contain(p => p.Contains("MOTD"));
    }

    #endregion

    #region Channel Mode and Topic Numerics Tests

    [Fact]
    public void UModeIsCreates221Message()
    {
        // Act
        var message = IrcNumerics.UModeIs(Server, Nick, "+iw");

        // Assert
        message.Command.Should().Be("221");
        message.Parameters.Should().Contain("+iw");
    }

    [Fact]
    public void ChannelModeIsCreates324Message()
    {
        // Act
        var message = IrcNumerics.ChannelModeIs(Server, Nick, "#test", "+nt");

        // Assert
        message.Command.Should().Be("324");
        message.Parameters.Should().Contain("#test");
        message.Parameters.Should().Contain("+nt");
    }

    [Fact]
    public void TopicCreates332Message()
    {
        // Act
        var message = IrcNumerics.Topic(Server, Nick, "#test", "Channel topic!");

        // Assert
        message.Command.Should().Be("332");
        message.Parameters.Should().Contain("#test");
        message.Parameters.Should().Contain("Channel topic!");
    }

    [Fact]
    public void NoTopicCreates331Message()
    {
        // Act
        var message = IrcNumerics.NoTopic(Server, Nick, "#test");

        // Assert
        message.Command.Should().Be("331");
        message.Parameters.Should().Contain("#test");
        message.Parameters.Should().Contain(p => p.Contains("No topic"));
    }

    #endregion

    #region NAMES Numerics (353, 366) Tests

    [Fact]
    public void NamReplyCreates353Message()
    {
        // Act
        var message = IrcNumerics.NamReply(Server, Nick, "=", "#test", "@Op +Voice User");

        // Assert
        message.Command.Should().Be("353");
        message.Parameters.Should().Contain("=");
        message.Parameters.Should().Contain("#test");
        message.Parameters.Should().Contain("@Op +Voice User");
    }

    [Fact]
    public void EndOfNamesCreates366Message()
    {
        // Act
        var message = IrcNumerics.EndOfNames(Server, Nick, "#test");

        // Assert
        message.Command.Should().Be("366");
        message.Parameters.Should().Contain("#test");
        message.Parameters.Should().Contain(p => p.Contains("End of"));
    }

    #endregion

    #region WHO/WHOIS Numerics Tests

    [Fact]
    public void WhoisUserCreates311Message()
    {
        // Act
        var message = IrcNumerics.WhoisUser(Server, Nick, "Target", "user", "host.com", "Real Name");

        // Assert
        message.Command.Should().Be("311");
        message.Parameters.Should().Contain("Target");
        message.Parameters.Should().Contain("user");
        message.Parameters.Should().Contain("host.com");
        message.Parameters.Should().Contain("Real Name");
    }

    [Fact]
    public void WhoisOperatorCreates313Message()
    {
        // Act
        var message = IrcNumerics.WhoisOperator(Server, Nick, "Target");

        // Assert
        message.Command.Should().Be("313");
        message.Parameters.Should().Contain("Target");
        message.Parameters.Should().Contain(p => p.Contains("IRC operator"));
    }

    [Fact]
    public void WhoisSecureCreates671Message()
    {
        // Act
        var message = IrcNumerics.WhoisSecure(Server, Nick, "Target");

        // Assert
        message.Command.Should().Be("671");
        message.Parameters.Should().Contain("Target");
        message.Parameters.Should().Contain(p => p.Contains("secure connection"));
    }

    [Fact]
    public void EndOfWhoisCreates318Message()
    {
        // Act
        var message = IrcNumerics.EndOfWhois(Server, Nick, "Target");

        // Assert
        message.Command.Should().Be("318");
        message.Parameters.Should().Contain("Target");
        message.Parameters.Should().Contain(p => p.Contains("End of"));
    }

    #endregion

    #region AWAY Numerics (301, 305, 306) Tests

    [Fact]
    public void AwayCreates301Message()
    {
        // Act
        var message = IrcNumerics.Away(Server, Nick, "Target", "Gone fishing");

        // Assert
        message.Command.Should().Be("301");
        message.Parameters.Should().Contain("Target");
        message.Parameters.Should().Contain("Gone fishing");
    }

    [Fact]
    public void UnAwayCreates305Message()
    {
        // Act
        var message = IrcNumerics.UnAway(Server, Nick);

        // Assert
        message.Command.Should().Be("305");
        message.Parameters.Should().Contain(p => p.Contains("no longer marked"));
    }

    [Fact]
    public void NowAwayCreates306Message()
    {
        // Act
        var message = IrcNumerics.NowAway(Server, Nick);

        // Assert
        message.Command.Should().Be("306");
        message.Parameters.Should().Contain(p => p.Contains("marked as being away"));
    }

    #endregion

    #region SASL Numerics (900-907) Tests

    [Fact]
    public void LoggedInCreates900Message()
    {
        // Act
        var message = IrcNumerics.LoggedIn(Server, Nick, "Nick!user@host", "accountname");

        // Assert
        message.Command.Should().Be("900");
        message.Parameters.Should().Contain("Nick!user@host");
        message.Parameters.Should().Contain("accountname");
    }

    [Fact]
    public void SaslSuccessCreates903Message()
    {
        // Act
        var message = IrcNumerics.SaslSuccess(Server, Nick);

        // Assert
        message.Command.Should().Be("903");
        message.Parameters.Should().Contain(p => p.Contains("successful"));
    }

    [Fact]
    public void SaslFailCreates904Message()
    {
        // Act
        var message = IrcNumerics.SaslFail(Server, Nick);

        // Assert
        message.Command.Should().Be("904");
        message.Parameters.Should().Contain(p => p.Contains("failed"));
    }

    [Fact]
    public void SaslAbortedCreates906Message()
    {
        // Act
        var message = IrcNumerics.SaslAborted(Server, Nick);

        // Assert
        message.Command.Should().Be("906");
        message.Parameters.Should().Contain(p => p.Contains("aborted"));
    }

    [Fact]
    public void SaslAlreadyCreates907Message()
    {
        // Act
        var message = IrcNumerics.SaslAlready(Server, Nick);

        // Assert
        message.Command.Should().Be("907");
        message.Parameters.Should().Contain(p => p.Contains("already authenticated"));
    }

    #endregion

    #region Error Numerics (400-499) Tests

    [Fact]
    public void NoSuchNickCreates401Message()
    {
        // Act
        var message = IrcNumerics.NoSuchNick(Server, Nick, "Unknown");

        // Assert
        message.Command.Should().Be("401");
        message.Parameters.Should().Contain("Unknown");
        message.Parameters.Should().Contain(p => p.Contains("No such nick"));
    }

    [Fact]
    public void NoSuchChannelCreates403Message()
    {
        // Act
        var message = IrcNumerics.NoSuchChannel(Server, Nick, "#invalid");

        // Assert
        message.Command.Should().Be("403");
        message.Parameters.Should().Contain("#invalid");
        message.Parameters.Should().Contain(p => p.Contains("No such channel"));
    }

    [Fact]
    public void CannotSendToChannelCreates404Message()
    {
        // Act
        var message = IrcNumerics.CannotSendToChannel(Server, Nick, "#moderated");

        // Assert
        message.Command.Should().Be("404");
        message.Parameters.Should().Contain("#moderated");
        message.Parameters.Should().Contain(p => p.Contains("Cannot send"));
    }

    [Fact]
    public void UnknownCommandCreates421Message()
    {
        // Act
        var message = IrcNumerics.UnknownCommand(Server, Nick, "BADCMD");

        // Assert
        message.Command.Should().Be("421");
        message.Parameters.Should().Contain("BADCMD");
        message.Parameters.Should().Contain(p => p.Contains("Unknown command"));
    }

    [Fact]
    public void ErroneusNicknameCreates432Message()
    {
        // Act
        var message = IrcNumerics.ErroneusNickname(Server, Nick, "123invalid");

        // Assert
        message.Command.Should().Be("432");
        message.Parameters.Should().Contain("123invalid");
        message.Parameters.Should().Contain(p => p.Contains("Erroneous"));
    }

    [Fact]
    public void NicknameInUseCreates433Message()
    {
        // Act
        var message = IrcNumerics.NicknameInUse(Server, Nick, "TakenNick");

        // Assert
        message.Command.Should().Be("433");
        message.Parameters.Should().Contain("TakenNick");
        message.Parameters.Should().Contain(p => p.Contains("already in use"));
    }

    [Fact]
    public void NotOnChannelCreates442Message()
    {
        // Act
        var message = IrcNumerics.NotOnChannel(Server, Nick, "#test");

        // Assert
        message.Command.Should().Be("442");
        message.Parameters.Should().Contain("#test");
        message.Parameters.Should().Contain(p => p.Contains("not on that channel"));
    }

    [Fact]
    public void NotRegisteredCreates451Message()
    {
        // Act
        var message = IrcNumerics.NotRegistered(Server, Nick);

        // Assert
        message.Command.Should().Be("451");
        message.Parameters.Should().Contain(p => p.Contains("not registered"));
    }

    [Fact]
    public void NeedMoreParamsCreates461Message()
    {
        // Act
        var message = IrcNumerics.NeedMoreParams(Server, Nick, "JOIN");

        // Assert
        message.Command.Should().Be("461");
        message.Parameters.Should().Contain("JOIN");
        message.Parameters.Should().Contain(p => p.Contains("Not enough parameters"));
    }

    [Fact]
    public void AlreadyRegisteredCreates462Message()
    {
        // Act
        var message = IrcNumerics.AlreadyRegistered(Server, Nick);

        // Assert
        message.Command.Should().Be("462");
        message.Parameters.Should().Contain(p => p.Contains("may not reregister"));
    }

    [Fact]
    public void ChannelIsFullCreates471Message()
    {
        // Act
        var message = IrcNumerics.ChannelIsFull(Server, Nick, "#full");

        // Assert
        message.Command.Should().Be("471");
        message.Parameters.Should().Contain("#full");
        message.Parameters.Should().Contain(p => p.Contains("+l"));
    }

    [Fact]
    public void InviteOnlyChanCreates473Message()
    {
        // Act
        var message = IrcNumerics.InviteOnlyChan(Server, Nick, "#invite");

        // Assert
        message.Command.Should().Be("473");
        message.Parameters.Should().Contain("#invite");
        message.Parameters.Should().Contain(p => p.Contains("+i"));
    }

    [Fact]
    public void BannedFromChanCreates474Message()
    {
        // Act
        var message = IrcNumerics.BannedFromChan(Server, Nick, "#banned");

        // Assert
        message.Command.Should().Be("474");
        message.Parameters.Should().Contain("#banned");
        message.Parameters.Should().Contain(p => p.Contains("+b"));
    }

    [Fact]
    public void BadChannelKeyCreates475Message()
    {
        // Act
        var message = IrcNumerics.BadChannelKey(Server, Nick, "#keyed");

        // Assert
        message.Command.Should().Be("475");
        message.Parameters.Should().Contain("#keyed");
        message.Parameters.Should().Contain(p => p.Contains("+k"));
    }

    [Fact]
    public void ChanOpPrivsNeededCreates482Message()
    {
        // Act
        var message = IrcNumerics.ChanOpPrivsNeeded(Server, Nick, "#test");

        // Assert
        message.Command.Should().Be("482");
        message.Parameters.Should().Contain("#test");
        message.Parameters.Should().Contain(p => p.Contains("not channel operator"));
    }

    [Fact]
    public void NoPrivilegesCreates481Message()
    {
        // Act
        var message = IrcNumerics.NoPrivileges(Server, Nick);

        // Assert
        message.Command.Should().Be("481");
        message.Parameters.Should().Contain(p => p.Contains("not an IRC operator"));
    }

    #endregion
}
