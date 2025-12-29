using FluentAssertions;
using Hugin.Core.ValueObjects;
using Xunit;

namespace Hugin.Core.Tests.ValueObjects;

public class NicknameTests
{
    [Theory]
    [InlineData("Nick")]
    [InlineData("nick")]
    [InlineData("Nick123")]
    [InlineData("Nick_Name")]
    [InlineData("Nick-Name")]
    [InlineData("[Nick]")]
    [InlineData("Nick|Away")]
    [InlineData("_Nick")]
    public void TryCreateWithValidNicknamesReturnsTrue(string nick)
    {
        var result = Nickname.TryCreate(nick, out var nickname, out _);

        result.Should().BeTrue();
        nickname.Should().NotBeNull();
        nickname!.Value.Should().Be(nick);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123Nick")] // Can't start with digit
    [InlineData("-Nick")] // Can't start with hyphen
    [InlineData("Nick Name")] // No spaces
    [InlineData("Nick@Name")] // No @
    [InlineData("Nick!Name")] // No !
    [InlineData("Nick.Name")] // No dots (reserved for server names)
    public void TryCreateWithInvalidNicknamesReturnsFalse(string nick)
    {
        var result = Nickname.TryCreate(nick, out var nickname, out _);

        result.Should().BeFalse();
        nickname.Should().BeNull();
    }

    [Fact]
    public void TryCreateWithTooLongNicknameReturnsFalse()
    {
        var longNick = new string('a', 31);

        var result = Nickname.TryCreate(longNick, out _, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void EqualsWithSameNickDifferentCaseReturnsTrue()
    {
        Nickname.TryCreate("Nick", out var nick1, out _);
        Nickname.TryCreate("NICK", out var nick2, out _);

        nick1!.Equals(nick2).Should().BeTrue();
        (nick1 == nick2).Should().BeTrue();
    }

    [Fact]
    public void GetHashCodeWithSameNickDifferentCaseReturnsSameHash()
    {
        Nickname.TryCreate("Nick", out var nick1, out _);
        Nickname.TryCreate("NICK", out var nick2, out _);

        nick1!.GetHashCode().Should().Be(nick2!.GetHashCode());
    }

    [Fact]
    public void ToStringReturnsValue()
    {
        Nickname.TryCreate("Nick", out var nick, out _);

        nick!.ToString().Should().Be("Nick");
    }
}

public class ChannelNameTests
{
    [Theory]
    [InlineData("#channel")]
    [InlineData("#Channel")]
    [InlineData("#channel123")]
    [InlineData("#channel-name")]
    [InlineData("#日本語")] // Unicode support
    [InlineData("&localchannel")]
    public void TryCreateWithValidChannelNamesReturnsTrue(string name)
    {
        var result = ChannelName.TryCreate(name, out var channel, out _);

        result.Should().BeTrue();
        channel.Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("channel")] // Must start with # or &
    [InlineData("#")] // Just prefix
    [InlineData("# channel")] // No spaces
    [InlineData("#channel,other")] // No commas
    public void TryCreateWithInvalidChannelNamesReturnsFalse(string name)
    {
        var result = ChannelName.TryCreate(name, out var channel, out _);

        result.Should().BeFalse();
        channel.Should().BeNull();
    }

    [Fact]
    public void IsLocalWithLocalChannelReturnsTrue()
    {
        ChannelName.TryCreate("&local", out var channel, out _);

        channel!.IsLocal.Should().BeTrue();
    }

    [Fact]
    public void IsLocalWithNetworkChannelReturnsFalse()
    {
        ChannelName.TryCreate("#global", out var channel, out _);

        channel!.IsLocal.Should().BeFalse();
    }

    [Fact]
    public void EqualsWithSameChannelDifferentCaseReturnsTrue()
    {
        ChannelName.TryCreate("#Channel", out var ch1, out _);
        ChannelName.TryCreate("#CHANNEL", out var ch2, out _);

        ch1!.Equals(ch2).Should().BeTrue();
    }
}

public class HostmaskTests
{
    [Fact]
    public void CreateWithValidHostmaskParsesCorrectly()
    {
        var hostmask = Hostmask.Create("nick", "user", "host.example.com");

        hostmask.Nick.Should().Be("nick");
        hostmask.User.Should().Be("user");
        hostmask.Host.Should().Be("host.example.com");
    }

    [Fact]
    public void TryParseWithValidHostmaskReturnsTrue()
    {
        var result = Hostmask.TryParse("nick!user@host.com", out var hostmask);

        result.Should().BeTrue();
        hostmask!.Nick.Should().Be("nick");
        hostmask.User.Should().Be("user");
        hostmask.Host.Should().Be("host.com");
    }

    [Fact]
    public void TryParseWithInvalidHostmaskReturnsFalse()
    {
        var result = Hostmask.TryParse("not-a-hostmask", out var hostmask);

        result.Should().BeFalse();
        hostmask.Should().BeNull();
    }

    [Fact]
    public void ToStringFormatsCorrectly()
    {
        var hostmask = Hostmask.Create("nick", "user", "host.com");

        hostmask.ToString().Should().Be("nick!user@host.com");
    }

    [Theory]
    [InlineData("nick!user@host.com", "*!*@*", true)]
    [InlineData("nick!user@host.com", "nick!*@*", true)]
    [InlineData("nick!user@host.com", "*!user@*", true)]
    [InlineData("nick!user@host.com", "*!*@host.com", true)]
    [InlineData("nick!user@host.com", "nick!user@host.com", true)]
    [InlineData("nick!user@host.com", "other!*@*", false)]
    [InlineData("nick!user@host.com", "*!*@other.com", false)]
    [InlineData("nick!user@192.168.1.1", "*!*@192.168.*", true)]
    public void MatchesWildcardPatternsMatchesCorrectly(string hostmask, string pattern, bool expected)
    {
        var result = Hostmask.TryParse(hostmask, out var hm);
        result.Should().BeTrue();

        hm!.Matches(pattern).Should().Be(expected);
    }

    [Fact]
    public void WithCloakedHostReturnsNewHostmask()
    {
        var original = Hostmask.Create("nick", "user", "real.host.com");

        var cloaked = original.WithCloakedHost("abc123.cloak");

        cloaked.Nick.Should().Be("nick");
        cloaked.User.Should().Be("user");
        cloaked.Host.Should().Be("abc123.cloak");
        original.Host.Should().Be("real.host.com"); // Original unchanged
    }
}

public class ServerIdTests
{
    [Fact]
    public void CreateWithValidServerIdSucceeds()
    {
        var serverId = ServerId.Create("001", "irc.example.com");

        serverId.Sid.Should().Be("001");
        serverId.Name.Should().Be("irc.example.com");
    }

    [Fact]
    public void CreateWithInvalidSidThrowsArgumentException()
    {
        var act = () => ServerId.Create("1234", "irc.example.com");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EqualsWithSameServerIdReturnsTrue()
    {
        var id1 = ServerId.Create("001", "irc.example.com");
        var id2 = ServerId.Create("001", "irc.example.com");

        id1.Equals(id2).Should().BeTrue();
    }

    [Fact]
    public void EqualsWithDifferentSidReturnsFalse()
    {
        var id1 = ServerId.Create("001", "irc.example.com");
        var id2 = ServerId.Create("002", "irc.example.com");

        id1.Equals(id2).Should().BeFalse();
    }
}
