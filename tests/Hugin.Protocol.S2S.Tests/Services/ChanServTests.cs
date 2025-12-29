using FluentAssertions;
using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using Hugin.Protocol.S2S.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Hugin.Protocol.S2S.Tests.Services;

/// <summary>
/// Unit tests for ChanServ service.
/// </summary>
public sealed class ChanServTests
{
    private readonly Mock<IChannelRepository> _channelRepository;
    private readonly Mock<IAccountRepository> _accountRepository;
    private readonly Mock<IServicesManager> _servicesManager;
    private readonly ChanServ _chanServ;
    private readonly ServerId _serverId;

    public ChanServTests()
    {
        _channelRepository = new Mock<IChannelRepository>();
        _accountRepository = new Mock<IAccountRepository>();
        _servicesManager = new Mock<IServicesManager>();
        _serverId = ServerId.Create("001", "irc.test.net");
        _chanServ = new ChanServ(
            _channelRepository.Object,
            _accountRepository.Object,
            _serverId,
            "services.test.net",
            NullLogger<ChanServ>.Instance);
    }

    [Fact]
    public void NicknameIsChanServ()
    {
        _chanServ.Nickname.Should().Be("ChanServ");
    }

    [Fact]
    public void IdentIsChanServ()
    {
        _chanServ.Ident.Should().Be("ChanServ");
    }

    [Fact]
    public void HostIsServiceHost()
    {
        _chanServ.Host.Should().Be("services.test.net");
    }

    [Fact]
    public void RealnameIsDescriptive()
    {
        _chanServ.Realname.Should().Be("Channel Registration Service");
    }

    [Fact]
    public void UidStartsWithServerId()
    {
        _chanServ.Uid.Should().StartWith("001");
        _chanServ.Uid.Should().HaveLength(9);
    }

    [Fact]
    public void GetHelpWithNoCommandReturnsGeneralHelp()
    {
        var help = _chanServ.GetHelp(null).ToList();

        help.Should().NotBeEmpty();
        help.Should().Contain(l => l.Contains("ChanServ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetHelpWithRegisterCommandReturnsRegisterHelp()
    {
        var help = _chanServ.GetHelp("REGISTER").ToList();

        help.Should().NotBeEmpty();
        help.Should().Contain(l => l.Contains("REGISTER", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HandleHelpCommandReturnsHelpText()
    {
        var context = CreateContext("HELP");

        await _chanServ.HandleMessageAsync(context);

        _servicesManager.Verify(
            s => s.SendNoticeAsync(_chanServ.Uid, "001AAAAAB", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task HandleRegisterWithNoChannelReturnsError()
    {
        var context = CreateContext("REGISTER", account: "TestUser");

        await _chanServ.HandleMessageAsync(context);

        VerifyErrorReply("Syntax:");
    }

    [Fact]
    public async Task HandleRegisterWithoutIdentificationReturnsError()
    {
        var context = CreateContext("REGISTER #test", account: null);

        await _chanServ.HandleMessageAsync(context);

        VerifyErrorReply("must be identified");
    }

    [Fact]
    public async Task HandleRegisterWithInvalidChannelNameReturnsError()
    {
        var context = CreateContext("REGISTER notachannel", account: "TestUser");

        await _chanServ.HandleMessageAsync(context);

        VerifyErrorReply("must start with");
    }

    [Fact]
    public async Task HandleInfoWithNoChannelReturnsError()
    {
        var context = CreateContext("INFO");

        await _chanServ.HandleMessageAsync(context);

        VerifyErrorReply("Syntax:");
    }

    [Fact]
    public async Task HandleInfoWithNonExistentChannelReturnsError()
    {
        // GetByName returns null for non-existent channel
        _channelRepository.Setup(r => r.GetByName(It.IsAny<ChannelName>()))
            .Returns((Channel?)null);

        var context = CreateContext("INFO #nonexistent");

        await _chanServ.HandleMessageAsync(context);

        VerifyErrorReply("not registered");
    }

    [Fact]
    public async Task HandleOpWithNoChannelReturnsError()
    {
        var context = CreateContext("OP", account: "TestUser");

        await _chanServ.HandleMessageAsync(context);

        VerifyErrorReply("Syntax:");
    }

    [Fact]
    public async Task HandleOpWithNoNicknameReturnsError()
    {
        var context = CreateContext("OP #test", account: "TestUser");

        await _chanServ.HandleMessageAsync(context);

        VerifyErrorReply("Syntax:");
    }

    [Fact]
    public async Task HandleDeopWithNoChannelReturnsError()
    {
        var context = CreateContext("DEOP", account: "TestUser");

        await _chanServ.HandleMessageAsync(context);

        VerifyErrorReply("Syntax:");
    }

    [Fact]
    public async Task HandleVoiceWithNoChannelReturnsError()
    {
        var context = CreateContext("VOICE", account: "TestUser");

        await _chanServ.HandleMessageAsync(context);

        VerifyErrorReply("Syntax:");
    }

    [Fact]
    public async Task HandleDevoiceWithNoChannelReturnsError()
    {
        var context = CreateContext("DEVOICE", account: "TestUser");

        await _chanServ.HandleMessageAsync(context);

        VerifyErrorReply("Syntax:");
    }

    [Fact]
    public async Task HandleKickWithNoChannelReturnsError()
    {
        var context = CreateContext("KICK", account: "TestUser");

        await _chanServ.HandleMessageAsync(context);

        VerifyErrorReply("Syntax:");
    }

    [Fact]
    public async Task HandleBanWithNoChannelReturnsError()
    {
        var context = CreateContext("BAN", account: "TestUser");

        await _chanServ.HandleMessageAsync(context);

        VerifyErrorReply("Syntax:");
    }

    [Fact]
    public async Task HandleUnbanWithNoChannelReturnsError()
    {
        var context = CreateContext("UNBAN", account: "TestUser");

        await _chanServ.HandleMessageAsync(context);

        VerifyErrorReply("Syntax:");
    }

    [Fact]
    public async Task HandleTopicWithNoChannelReturnsError()
    {
        var context = CreateContext("TOPIC", account: "TestUser");

        await _chanServ.HandleMessageAsync(context);

        VerifyErrorReply("Syntax:");
    }

    [Fact]
    public async Task HandleSetWithNoChannelReturnsError()
    {
        var context = CreateContext("SET", account: "TestUser");

        await _chanServ.HandleMessageAsync(context);

        VerifyErrorReply("Syntax:");
    }

    [Fact]
    public async Task HandleSetWithoutIdentificationReturnsError()
    {
        var context = CreateContext("SET #test DESCRIPTION A nice channel", account: null);

        await _chanServ.HandleMessageAsync(context);

        VerifyErrorReply("must be identified");
    }

    [Fact]
    public async Task HandleDropWithNoChannelReturnsError()
    {
        var context = CreateContext("DROP", account: "TestUser");

        await _chanServ.HandleMessageAsync(context);

        VerifyErrorReply("Syntax:");
    }

    [Fact]
    public async Task HandleDropWithoutIdentificationReturnsError()
    {
        var context = CreateContext("DROP #test", account: null);

        await _chanServ.HandleMessageAsync(context);

        VerifyErrorReply("must be identified");
    }

    [Fact]
    public async Task HandleUnknownCommandReturnsError()
    {
        var context = CreateContext("INVALIDCMD");

        await _chanServ.HandleMessageAsync(context);

        VerifyErrorReply("Unknown command");
    }

    private ServiceMessageContext CreateContext(string message, string? account = null)
    {
        return new ServiceMessageContext(
            sourceUid: "001AAAAAB",
            sourceNick: "TestUser",
            sourceAccount: account,
            message: message,
            services: _servicesManager.Object);
    }

    private void VerifyErrorReply(string containsText)
    {
        _servicesManager.Verify(
            s => s.SendNoticeAsync(
                _chanServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains(containsText, StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }
}
