using FluentAssertions;

namespace Hugin.Protocol.S2S.Tests;

/// <summary>
/// Tests for S2SMessage parsing and serialization.
/// </summary>
public class S2SMessageTests
{
    [Fact]
    public void TryParseSimpleCommandSucceeds()
    {
        // Arrange
        var input = "PING :server.test";

        // Act
        var result = S2SMessage.TryParse(input, out var message);

        // Assert
        result.Should().BeTrue();
        message.Should().NotBeNull();
        message!.Command.Should().Be("PING");
        message.Parameters.Should().HaveCount(1);
        message.Parameters[0].Should().Be("server.test");
        message.Source.Should().BeNull();
        message.Tags.Should().BeEmpty();
    }

    [Fact]
    public void TryParseWithSourceSucceeds()
    {
        // Arrange
        var input = ":001 SERVER irc.hub.com 1 002 :Hub Server";

        // Act
        var result = S2SMessage.TryParse(input, out var message);

        // Assert
        result.Should().BeTrue();
        message.Should().NotBeNull();
        message!.Source.Should().Be("001");
        message.Command.Should().Be("SERVER");
        message.Parameters.Should().HaveCount(4);
        message.Parameters[0].Should().Be("irc.hub.com");
        message.Parameters[1].Should().Be("1");
        message.Parameters[2].Should().Be("002");
        message.Parameters[3].Should().Be("Hub Server");
    }

    [Fact]
    public void TryParseWithTagsSucceeds()
    {
        // Arrange
        var input = "@time=2024-01-01T00:00:00Z;label=test :001AAAAAB PRIVMSG #channel :Hello world";

        // Act
        var result = S2SMessage.TryParse(input, out var message);

        // Assert
        result.Should().BeTrue();
        message.Should().NotBeNull();
        message!.Tags.Should().HaveCount(2);
        message.Tags["time"].Should().Be("2024-01-01T00:00:00Z");
        message.Tags["label"].Should().Be("test");
        message.Source.Should().Be("001AAAAAB");
        message.Command.Should().Be("PRIVMSG");
        message.Parameters.Should().HaveCount(2);
    }

    [Fact]
    public void TryParseWithEscapedTagValuesSucceeds()
    {
        // Arrange - escaped semicolon, space, backslash, CR, LF
        var input = @"@test=semi\:colon\sspace\\backslash :001 PING :test";

        // Act
        var result = S2SMessage.TryParse(input, out var message);

        // Assert
        result.Should().BeTrue();
        message.Should().NotBeNull();
        message!.Tags["test"].Should().Be("semi;colon space\\backslash");
    }

    [Fact]
    public void TryParseUidCommandSucceeds()
    {
        // Arrange
        var input = ":001 UID nick 1 1234567890 user host 001AAAAAB 0 +i vhost :Real Name";

        // Act
        var result = S2SMessage.TryParse(input, out var message);

        // Assert
        result.Should().BeTrue();
        message.Should().NotBeNull();
        message!.Source.Should().Be("001");
        message.Command.Should().Be("UID");
        message.Parameters.Should().HaveCount(10);
        message.Parameters[0].Should().Be("nick");
        message.Parameters[5].Should().Be("001AAAAAB");
        message.Parameters[9].Should().Be("Real Name");
    }

    [Fact]
    public void TryParseSjoinCommandSucceeds()
    {
        // Arrange
        var input = ":001 SJOIN 1234567890 #channel +nt :@001AAAAAB +001AAAAAC 001AAAAAD";

        // Act
        var result = S2SMessage.TryParse(input, out var message);

        // Assert
        result.Should().BeTrue();
        message.Should().NotBeNull();
        message!.Command.Should().Be("SJOIN");
        message.Parameters.Should().HaveCount(4);
        message.Parameters[0].Should().Be("1234567890");
        message.Parameters[1].Should().Be("#channel");
        message.Parameters[2].Should().Be("+nt");
        message.Parameters[3].Should().Be("@001AAAAAB +001AAAAAC 001AAAAAD");
    }

    [Fact]
    public void TryParseEmptyStringFails()
    {
        // Arrange
        var input = "";

        // Act
        var result = S2SMessage.TryParse(input, out var message);

        // Assert
        result.Should().BeFalse();
        message.Should().BeNull();
    }

    [Fact]
    public void TryParseWhitespaceOnlyFails()
    {
        // Arrange
        var input = "   ";

        // Act
        var result = S2SMessage.TryParse(input, out var message);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CreateSimpleMessageSerializesCorrectly()
    {
        // Arrange & Act
        var message = S2SMessage.Create("PING", "server.test");

        // Assert - trailing colon only added when parameter contains space or starts with colon
        message.ToString().Should().Be("PING server.test");
    }

    [Fact]
    public void CreateWithSourceSerializesCorrectly()
    {
        // Arrange & Act
        var message = S2SMessage.CreateWithSource("001", "SERVER", "irc.hub.com", "1", "Hub Server");

        // Assert - last param has space, so gets colon
        message.ToString().Should().Be(":001 SERVER irc.hub.com 1 :Hub Server");
    }

    [Fact]
    public void CreateFullWithTagsSerializesCorrectly()
    {
        // Arrange
        var tags = new Dictionary<string, string?>
        {
            ["time"] = "2024-01-01T00:00:00Z"
        };

        // Act
        var message = S2SMessage.CreateFull(tags, "001AAAAAB", "PRIVMSG", "#channel", "Hello world");

        // Assert
        message.ToString().Should().Be("@time=2024-01-01T00:00:00Z :001AAAAAB PRIVMSG #channel :Hello world");
    }

    [Fact]
    public void TagValueEscapingWorksCorrectly()
    {
        // Arrange
        var tags = new Dictionary<string, string?>
        {
            ["test"] = "semi;colon space\\backslash"
        };

        // Act
        var message = S2SMessage.CreateFull(tags, "001", "PING", "test");

        // Assert
        var result = message.ToString();
        result.Should().Contain(@"semi\:colon\sspace\\backslash");
    }

    [Fact]
    public void RoundTripParseSerializePreservesMessage()
    {
        // Arrange
        var original = "@time=2024-01-01T00:00:00Z :001AAAAAB PRIVMSG #channel :Hello world";

        // Act
        var parsed = S2SMessage.TryParse(original, out var message);
        parsed.Should().BeTrue();
        var serialized = message!.ToString();

        // Assert
        serialized.Should().Be(original);
    }

    [Fact]
    public void MultipleParametersWithoutTrailingSerializeCorrectly()
    {
        // Arrange & Act
        var message = S2SMessage.Create("MODE", "#channel", "+o", "nick");

        // Assert - no spaces in params, so no trailing colon needed
        message.ToString().Should().Be("MODE #channel +o nick");
    }

    [Fact]
    public void ParameterWithSpaceIsTrailing()
    {
        // Arrange & Act
        var message = S2SMessage.Create("QUIT", "Gone for lunch");

        // Assert
        message.ToString().Should().Be("QUIT :Gone for lunch");
    }
}
