using FluentAssertions;
using Hugin.Core.Entities;
using Hugin.Core.Enums;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using Hugin.Protocol.S2S;
using Hugin.Protocol.S2S.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Hugin.Protocol.S2S.Tests.Services;

/// <summary>
/// Unit tests for OperServ service.
/// </summary>
public sealed class OperServTests
{
    private readonly Mock<IServerBanRepository> _banRepository;
    private readonly Mock<IServerLinkManager> _linkManager;
    private readonly Mock<IServicesManager> _servicesManager;
    private readonly OperServ _operServ;
    private readonly ServerId _serverId;

    public OperServTests()
    {
        _banRepository = new Mock<IServerBanRepository>();
        _linkManager = new Mock<IServerLinkManager>();
        _servicesManager = new Mock<IServicesManager>();
        _serverId = ServerId.Create("001", "irc.test.net");

        _operServ = new OperServ(
            _banRepository.Object,
            _linkManager.Object,
            _serverId,
            "services.test.net",
            NullLogger<OperServ>.Instance);
    }

    [Fact]
    public void PropertiesAreSet()
    {
        _operServ.Nickname.Should().Be("OperServ");
        _operServ.Ident.Should().Be("OperServ");
        _operServ.Realname.Should().Be("Network Administration Service");
        _operServ.Host.Should().Be("services.test.net");
        _operServ.Uid.Should().Be("001AAAAAO");
    }

    [Fact]
    public async Task NonOperatorIsDenied()
    {
        var context = CreateContext("HELP", isOperator: false);

        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("Access denied")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AkillAddRequiresDurationFormat()
    {
        var context = CreateContext("AKILL ADD user@host bad reason", isOperator: true);

        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("Invalid duration")),
            It.IsAny<CancellationToken>()), Times.Once);
        _banRepository.Verify(r => r.AddAsync(It.IsAny<ServerBan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AkillAddSucceeds()
    {
        var context = CreateContext("AKILL ADD user@host 1h abuse", isOperator: true);

        await _operServ.HandleMessageAsync(context);

        _banRepository.Verify(r => r.AddAsync(It.Is<ServerBan>(b => b.Type == BanType.GLine && b.Mask == "user@host"), It.IsAny<CancellationToken>()), Times.Once);
        _linkManager.Verify(l => l.BroadcastAsync(It.IsAny<S2SMessage>(), null, It.IsAny<CancellationToken>()), Times.Once);
        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("has been added")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AkillDelShowsSyntaxWhenMissingMask()
    {
        var context = CreateContext("AKILL DEL", isOperator: true);

        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("Syntax: AKILL DEL")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AkillDelNotFoundReplies()
    {
        _banRepository.Setup(r => r.RemoveAsync("user@host", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var context = CreateContext("AKILL DEL user@host", isOperator: true);
        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("No AKILL found")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AkillDelSuccessBroadcasts()
    {
        _banRepository.Setup(r => r.RemoveAsync("user@host", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var context = CreateContext("AKILL DEL user@host", isOperator: true);
        await _operServ.HandleMessageAsync(context);

        _linkManager.Verify(l => l.BroadcastAsync(It.IsAny<S2SMessage>(), null, It.IsAny<CancellationToken>()), Times.Once);
        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("has been removed")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AkillListEmpty()
    {
        _banRepository.Setup(r => r.GetActiveGlinesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ServerBan>());

        var context = CreateContext("AKILL LIST", isOperator: true);
        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("AKILL list is empty")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AkillListShowsEntries()
    {
        var ban = new ServerBan(BanType.GLine, "user@host", "reason", "oper", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
        _banRepository.Setup(r => r.GetActiveGlinesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ban });

        var context = CreateContext("AKILL LIST", isOperator: true);
        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("user@host")),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task JupeRequiresArguments()
    {
        var context = CreateContext("JUPE", isOperator: true);

        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("Syntax: JUPE")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task JupeFailsWhenServerLinked()
    {
        _linkManager.Setup(l => l.GetByName("irc.example.org"))
            .Returns(new LinkedServer(ServerId.Create("005", "irc.example.org"), "desc", "ver", 1));

        var context = CreateContext("JUPE irc.example.org reason", isOperator: true);
        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("currently linked")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task JupeSucceeds()
    {
        var context = CreateContext("JUPE irc.example.org maintenance", isOperator: true);

        await _operServ.HandleMessageAsync(context);

        _banRepository.Verify(r => r.AddAsync(It.Is<ServerBan>(b => b.Type == BanType.Jupe && b.Mask == "irc.example.org"), It.IsAny<CancellationToken>()), Times.Once);
        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("has been JUPEd")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StatsOutputsCounts()
    {
        var remote = new LinkedServer(ServerId.Create("005", "irc.remote.net"), "desc", "ver", 2, _serverId);
        _linkManager.SetupGet(l => l.AllServers).Returns(new[] { remote });
        _linkManager.SetupGet(l => l.DirectLinks).Returns(Array.Empty<LinkedServer>());

        var context = CreateContext("STATS", isOperator: true);
        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("Network Statistics")),
            It.IsAny<CancellationToken>()), Times.Once);
        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("Total servers")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ModeSyntax()
    {
        var context = CreateContext("MODE", isOperator: true);
        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("Syntax: MODE")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ModeSuccess()
    {
        var context = CreateContext("MODE #chan +o user", isOperator: true);
        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("Mode change")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task KickSyntax()
    {
        var context = CreateContext("KICK", isOperator: true);
        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("Syntax: KICK")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task KickSuccess()
    {
        var context = CreateContext("KICK #chan user reason", isOperator: true);
        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("Kicked")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task KillSyntax()
    {
        var context = CreateContext("KILL", isOperator: true);
        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("Syntax: KILL")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task KillSuccessBroadcasts()
    {
        var context = CreateContext("KILL baduser being bad", isOperator: true);
        await _operServ.HandleMessageAsync(context);

        _linkManager.Verify(l => l.BroadcastAsync(It.IsAny<S2SMessage>(), null, It.IsAny<CancellationToken>()), Times.Once);
        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("Killed baduser")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RawSyntax()
    {
        var context = CreateContext("RAW", isOperator: true);
        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("Syntax: RAW")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RawSuccess()
    {
        var context = CreateContext("RAW SQUIT test", isOperator: true);
        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("RAW command sent")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestartRequiresConfirm()
    {
        var context = CreateContext("RESTART", isOperator: true);
        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("Warning: This will restart")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestartWithConfirm()
    {
        var context = CreateContext("RESTART CONFIRM", isOperator: true);
        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("restart initiated")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DieRequiresConfirm()
    {
        var context = CreateContext("DIE", isOperator: true);
        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("Warning: This will shut down")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DieWithConfirm()
    {
        var context = CreateContext("DIE CONFIRM", isOperator: true);
        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("shutdown initiated")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GlobalSyntax()
    {
        var context = CreateContext("GLOBAL", isOperator: true);
        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("Syntax: GLOBAL")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GlobalSuccess()
    {
        var context = CreateContext("GLOBAL maintenance window", isOperator: true);
        await _operServ.HandleMessageAsync(context);

        _servicesManager.Verify(s => s.SendNoticeAsync(
            _operServ.Uid,
            "001AAAAAB",
            It.Is<string>(m => m.Contains("Global notice sent")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private ServiceMessageContext CreateContext(string message, bool isOperator)
    {
        return new ServiceMessageContext(
            "001AAAAAB",
            "OperUser",
            "operaccount",
            message,
            _servicesManager.Object,
            isOperator);
    }
}
