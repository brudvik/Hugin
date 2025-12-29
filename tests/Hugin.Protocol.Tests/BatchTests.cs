using FluentAssertions;
using Hugin.Protocol;
using Xunit;

namespace Hugin.Protocol.Tests;

/// <summary>
/// Tests for the Batch class and BatchTypes constants.
/// </summary>
public class BatchTests
{
    private const string ServerName = "irc.test.com";

    [Fact]
    public void ConstructorSetsTypeAndParameters()
    {
        // Act
        var batch = new Batch("chathistory", "#channel");

        // Assert
        batch.Type.Should().Be("chathistory");
        batch.Parameters.Should().Contain("#channel");
        batch.Reference.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ReferenceIsUniqueForEachBatch()
    {
        // Arrange
        var batch1 = new Batch("test");
        var batch2 = new Batch("test");
        var batch3 = new Batch("test");

        // Assert
        batch1.Reference.Should().NotBe(batch2.Reference);
        batch2.Reference.Should().NotBe(batch3.Reference);
        batch1.Reference.Should().NotBe(batch3.Reference);
    }

    [Fact]
    public void ReferenceIsUrlSafe()
    {
        // Arrange
        var batch = new Batch("test");

        // Assert
        batch.Reference.Should().NotContain("+");
        batch.Reference.Should().NotContain("/");
        batch.Reference.Should().NotContain("=");
    }

    [Fact]
    public void AddMessageAddsMessageWithBatchTag()
    {
        // Arrange
        var batch = new Batch("chathistory");
        var message = IrcMessage.Create("PRIVMSG", "#channel", "Hello");

        // Act
        batch.AddMessage(message);

        // Assert
        batch.Messages.Should().HaveCount(1);
        batch.Messages[0].Tags.Should().ContainKey("batch");
        batch.Messages[0].Tags["batch"].Should().Be(batch.Reference);
    }

    [Fact]
    public void CreateStartMessageReturnsCorrectFormat()
    {
        // Arrange
        var batch = new Batch("chathistory", "#channel");

        // Act
        var startMsg = batch.CreateStartMessage(ServerName);

        // Assert
        startMsg.Command.Should().Be("BATCH");
        startMsg.Source.Should().Be(ServerName);
        startMsg.Parameters[0].Should().StartWith("+");
        startMsg.Parameters[0].Should().Be("+" + batch.Reference);
        startMsg.Parameters[1].Should().Be("chathistory");
        startMsg.Parameters[2].Should().Be("#channel");
    }

    [Fact]
    public void CreateEndMessageReturnsCorrectFormat()
    {
        // Arrange
        var batch = new Batch("chathistory");

        // Act
        var endMsg = batch.CreateEndMessage(ServerName);

        // Assert
        endMsg.Command.Should().Be("BATCH");
        endMsg.Source.Should().Be(ServerName);
        endMsg.Parameters[0].Should().StartWith("-");
        endMsg.Parameters[0].Should().Be("-" + batch.Reference);
    }

    [Fact]
    public void GetAllMessagesReturnsStartMessagesEnd()
    {
        // Arrange
        var batch = new Batch("chathistory");
        batch.AddMessage(IrcMessage.Create("PRIVMSG", "#channel", "Message 1"));
        batch.AddMessage(IrcMessage.Create("PRIVMSG", "#channel", "Message 2"));

        // Act
        var allMessages = batch.GetAllMessages(ServerName).ToList();

        // Assert
        allMessages.Should().HaveCount(4);
        allMessages[0].Command.Should().Be("BATCH");
        allMessages[0].Parameters[0].Should().StartWith("+");
        allMessages[1].Command.Should().Be("PRIVMSG");
        allMessages[2].Command.Should().Be("PRIVMSG");
        allMessages[3].Command.Should().Be("BATCH");
        allMessages[3].Parameters[0].Should().StartWith("-");
    }

    [Fact]
    public void MessagesPropertyIsInitiallyEmpty()
    {
        // Arrange
        var batch = new Batch("test");

        // Assert
        batch.Messages.Should().BeEmpty();
    }

    [Fact]
    public void AddMessagePreservesOriginalMessageContent()
    {
        // Arrange
        var batch = new Batch("chathistory");
        var original = IrcMessage.CreateWithSource("nick!user@host", "PRIVMSG", "#channel", "Hello");

        // Act
        batch.AddMessage(original);

        // Assert
        batch.Messages[0].Source.Should().Be("nick!user@host");
        batch.Messages[0].Command.Should().Be("PRIVMSG");
        batch.Messages[0].Parameters.Should().Contain("#channel");
        batch.Messages[0].Parameters.Should().Contain("Hello");
    }
}

/// <summary>
/// Tests for BatchTypes constants.
/// </summary>
public class BatchTypesTests
{
    [Fact]
    public void NetjoinConstantIsCorrect()
    {
        BatchTypes.Netjoin.Should().Be("netjoin");
    }

    [Fact]
    public void NetsplitConstantIsCorrect()
    {
        BatchTypes.Netsplit.Should().Be("netsplit");
    }

    [Fact]
    public void ChathistoryConstantIsCorrect()
    {
        BatchTypes.Chathistory.Should().Be("chathistory");
    }

    [Fact]
    public void LabeledResponseConstantIsCorrect()
    {
        BatchTypes.LabeledResponse.Should().Be("labeled-response");
    }

    [Fact]
    public void DraftMultilineConstantIsCorrect()
    {
        BatchTypes.DraftMultiline.Should().Be("draft/multiline");
    }
}
