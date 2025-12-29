using FluentAssertions;
using Hugin.Protocol;
using Xunit;

namespace Hugin.Protocol.Tests;

public class IrcMessageTests
{
    [Fact]
    public void TryParseSimpleCommandReturnsTrue()
    {
        // Arrange
        var line = "PING :server";

        // Act
        var result = IrcMessage.TryParse(line, out var message);

        // Assert
        result.Should().BeTrue();
        message.Should().NotBeNull();
        message!.Command.Should().Be("PING");
        message.Parameters.Should().HaveCount(1);
        message.Parameters[0].Should().Be("server");
    }

    [Fact]
    public void TryParseMessageWithSourceParsesCorrectly()
    {
        // Arrange
        var line = ":nick!user@host PRIVMSG #channel :Hello, World!";

        // Act
        var result = IrcMessage.TryParse(line, out var message);

        // Assert
        result.Should().BeTrue();
        message.Should().NotBeNull();
        message!.Source.Should().Be("nick!user@host");
        message.Command.Should().Be("PRIVMSG");
        message.Parameters.Should().HaveCount(2);
        message.Parameters[0].Should().Be("#channel");
        message.Parameters[1].Should().Be("Hello, World!");
    }

    [Fact]
    public void TryParseMessageWithTagsParsesCorrectly()
    {
        // Arrange
        var line = "@time=2023-10-15T10:30:00.000Z;msgid=abc123 :nick!user@host PRIVMSG #channel :Hello";

        // Act
        var result = IrcMessage.TryParse(line, out var message);

        // Assert
        result.Should().BeTrue();
        message.Should().NotBeNull();
        message!.HasTags.Should().BeTrue();
        message.Tags.Should().ContainKey("time");
        message.Tags["time"].Should().Be("2023-10-15T10:30:00.000Z");
        message.Tags.Should().ContainKey("msgid");
        message.Tags["msgid"].Should().Be("abc123");
    }

    [Fact]
    public void TryParseTagWithoutValueParsesAsNull()
    {
        // Arrange
        var line = "@draft/reply :nick!user@host PRIVMSG #channel :test";

        // Act
        var result = IrcMessage.TryParse(line, out var message);

        // Assert
        result.Should().BeTrue();
        message!.Tags.Should().ContainKey("draft/reply");
        message.Tags["draft/reply"].Should().BeNull();
    }

    [Fact]
    public void TryParseTagValueWithEscapesUnescapesCorrectly()
    {
        // Arrange
        var line = @"@test=hello\sworld\:\) :nick PRIVMSG #test :hi";

        // Act
        var result = IrcMessage.TryParse(line, out var message);

        // Assert
        result.Should().BeTrue();
        message!.Tags["test"].Should().Be("hello world;)");
    }

    [Fact]
    public void TryParseEmptyLineReturnsFalse()
    {
        var result = IrcMessage.TryParse("", out var message);
        result.Should().BeFalse();
        message.Should().BeNull();
    }

    [Fact]
    public void TryParseJoinCommandParsesCorrectly()
    {
        // Arrange
        var line = "JOIN #channel1,#channel2 key1,key2";

        // Act
        var result = IrcMessage.TryParse(line, out var message);

        // Assert
        result.Should().BeTrue();
        message!.Command.Should().Be("JOIN");
        message.Parameters.Should().HaveCount(2);
        message.Parameters[0].Should().Be("#channel1,#channel2");
        message.Parameters[1].Should().Be("key1,key2");
    }

    [Fact]
    public void TryParseNumericReplyParsesCorrectly()
    {
        // Arrange
        var line = ":server.name 001 nick :Welcome to the IRC Network";

        // Act
        var result = IrcMessage.TryParse(line, out var message);

        // Assert
        result.Should().BeTrue();
        message!.Source.Should().Be("server.name");
        message.Command.Should().Be("001");
        message.Parameters[0].Should().Be("nick");
        message.Parameters[1].Should().Be("Welcome to the IRC Network");
    }

    [Fact]
    public void ToStringSimpleMessageFormatsCorrectly()
    {
        // Arrange
        var message = IrcMessage.Create("PING", "server");

        // Act
        var result = message.ToString();

        // Assert
        result.Should().Be("PING :server");
    }

    [Fact]
    public void ToStringMessageWithSourceFormatsCorrectly()
    {
        // Arrange
        var message = IrcMessage.CreateWithSource("nick!user@host", "PRIVMSG", "#channel", "Hello");

        // Act
        var result = message.ToString();

        // Assert
        result.Should().Be(":nick!user@host PRIVMSG #channel :Hello");
    }

    [Fact]
    public void ToStringMessageWithTagsFormatsCorrectly()
    {
        // Arrange
        var tags = new Dictionary<string, string?> { ["time"] = "2023-10-15T10:30:00.000Z" };
        var message = IrcMessage.CreateFull(tags, "nick", "PRIVMSG", "#channel", "Hi");

        // Act
        var result = message.ToString();

        // Assert
        result.Should().StartWith("@time=2023-10-15T10:30:00.000Z ");
    }

    [Fact]
    public void CreateCommandWithParamsCreatesValidMessage()
    {
        // Act
        var message = IrcMessage.Create("PRIVMSG", "#channel", "Hello World");

        // Assert
        message.Command.Should().Be("PRIVMSG");
        message.Parameters.Should().HaveCount(2);
        message.Source.Should().BeNull();
    }

    [Fact]
    public void WithSourceAddsSourceToMessage()
    {
        // Arrange
        var message = IrcMessage.Create("PRIVMSG", "#channel", "Hello");

        // Act
        var withSource = message.WithSource("nick!user@host");

        // Assert
        withSource.Source.Should().Be("nick!user@host");
        withSource.Command.Should().Be("PRIVMSG");
    }

    [Fact]
    public void WithTagsAddsTagsToMessage()
    {
        // Arrange
        var message = IrcMessage.Create("PRIVMSG", "#channel", "Hello");
        var tags = new Dictionary<string, string?> { ["msgid"] = "test123" };

        // Act
        var withTags = message.WithTags(tags);

        // Assert
        withTags.Tags.Should().ContainKey("msgid");
        withTags.Tags["msgid"].Should().Be("test123");
    }

    [Fact]
    public void ToBytesReturnsUtf8WithCrLf()
    {
        // Arrange
        var message = IrcMessage.Create("PING", "test");

        // Act
        var bytes = message.ToBytes();
        var str = System.Text.Encoding.UTF8.GetString(bytes);

        // Assert
        str.Should().EndWith("\r\n");
    }

    [Fact]
    public void TryParseMultipleSpacesParsesCorrectly()
    {
        // Some IRCv3 servers may have multiple spaces - we should handle gracefully
        var line = ":server  001  nick  :Welcome";
        var result = IrcMessage.TryParse(line, out var message);

        result.Should().BeTrue();
    }
}
