using FluentAssertions;
using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using Hugin.Protocol.S2S;
using Hugin.Protocol.S2S.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Hugin.Protocol.S2S.Tests.Services;

/// <summary>
/// Unit tests for HostServ - Virtual host management service.
/// </summary>
public sealed class HostServTests
{
    private readonly Mock<IVirtualHostRepository> _vhostRepositoryMock;
    private readonly Mock<IAccountRepository> _accountRepositoryMock;
    private readonly Mock<IServicesManager> _servicesManagerMock;
    private readonly HostServ _hostServ;
    private readonly ServerId _localServerId;

    public HostServTests()
    {
        _vhostRepositoryMock = new Mock<IVirtualHostRepository>();
        _accountRepositoryMock = new Mock<IAccountRepository>();
        _servicesManagerMock = new Mock<IServicesManager>();
        _localServerId = ServerId.Create("001", "hub.server.local");

        _hostServ = new HostServ(
            () => _vhostRepositoryMock.Object,
            () => _accountRepositoryMock.Object,
            _localServerId,
            "services.network.org",
            NullLogger<HostServ>.Instance
        );
    }

    [Fact]
    public void HostServShouldHaveCorrectProperties()
    {
        _hostServ.Nickname.Should().Be("HostServ");
        _hostServ.Ident.Should().Be("HostServ");
        _hostServ.Host.Should().Be("services.network.org");
        _hostServ.Realname.Should().Be("Virtual Host Service");
        _hostServ.Uid.Should().Be("001AAAAAH");
    }

    [Fact]
    public async Task RequestWithoutAuthenticationShouldFail()
    {
        var context = CreateContext("REQUEST user.example.net", sourceAccount: null);

        await _hostServ.HandleMessageAsync(context, CancellationToken.None);

        _servicesManagerMock.Verify(
            s => s.SendNoticeAsync(
                _hostServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("must be identified")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RequestWithoutHostnameShouldShowSyntax()
    {
        var context = CreateContext("REQUEST", sourceAccount: "TestAccount");

        await _hostServ.HandleMessageAsync(context, CancellationToken.None);

        _servicesManagerMock.Verify(
            s => s.SendNoticeAsync(
                _hostServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("Syntax: REQUEST")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RequestWithInvalidHostnameShouldFail()
    {
        var context = CreateContext("REQUEST invalid", sourceAccount: "TestAccount");

        var account = new Account(Guid.NewGuid(), "TestAccount", "hashed_password");
        _accountRepositoryMock.Setup(r => r.GetByNameAsync("TestAccount", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        await _hostServ.HandleMessageAsync(context, CancellationToken.None);

        _servicesManagerMock.Verify(
            s => s.SendNoticeAsync(
                _hostServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("Invalid hostname")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RequestWithHostnameInUseShouldFail()
    {
        var context = CreateContext("REQUEST user.example.net", sourceAccount: "TestAccount");

        var account = new Account(Guid.NewGuid(), "TestAccount", "hashed_password");
        _accountRepositoryMock.Setup(r => r.GetByNameAsync("TestAccount", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        _vhostRepositoryMock.Setup(r => r.IsHostnameInUseAsync("user.example.net", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _hostServ.HandleMessageAsync(context, CancellationToken.None);

        _servicesManagerMock.Verify(
            s => s.SendNoticeAsync(
                _hostServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("already in use")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RequestWithValidHostnameShouldSucceed()
    {
        var context = CreateContext("REQUEST user.example.net", sourceAccount: "TestAccount");

        var account = new Account(Guid.NewGuid(), "TestAccount", "hashed_password");
        _accountRepositoryMock.Setup(r => r.GetByNameAsync("TestAccount", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        _vhostRepositoryMock.Setup(r => r.IsHostnameInUseAsync("user.example.net", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _vhostRepositoryMock.Setup(r => r.CreateAsync(account.Id, "user.example.net", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VirtualHost(Guid.NewGuid(), account.Id, "user.example.net"));

        await _hostServ.HandleMessageAsync(context, CancellationToken.None);

        _servicesManagerMock.Verify(
            s => s.SendNoticeAsync(
                _hostServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("submitted")),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        
        _vhostRepositoryMock.Verify(r => r.CreateAsync(account.Id, "user.example.net", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ActivateWithoutApprovedVhostShouldFail()
    {
        var context = CreateContext("ACTIVATE", sourceAccount: "TestAccount");

        var account = new Account(Guid.NewGuid(), "TestAccount", "hashed_password");
        _accountRepositoryMock.Setup(r => r.GetByNameAsync("TestAccount", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        _vhostRepositoryMock.Setup(r => r.GetByAccountAsync(account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VirtualHost>());

        await _hostServ.HandleMessageAsync(context, CancellationToken.None);

        _servicesManagerMock.Verify(
            s => s.SendNoticeAsync(
                _hostServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("don't have any approved vhosts")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ActivateShouldActivateApprovedVhost()
    {
        var context = CreateContext("ACTIVATE", sourceAccount: "TestAccount");

        var account = new Account(Guid.NewGuid(), "TestAccount", "hashed_password");
        _accountRepositoryMock.Setup(r => r.GetByNameAsync("TestAccount", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var vhost = new VirtualHost(Guid.NewGuid(), account.Id, "user.example.net");
        vhost.Approve("Operator");

        _vhostRepositoryMock.Setup(r => r.GetByAccountAsync(account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { vhost });

        await _hostServ.HandleMessageAsync(context, CancellationToken.None);

        _servicesManagerMock.Verify(
            s => s.SendNoticeAsync(
                _hostServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("is now active")),
                It.IsAny<CancellationToken>()),
            Times.Once);
        
        _vhostRepositoryMock.Verify(r => r.DeactivateAllForAccountAsync(account.Id, It.IsAny<CancellationToken>()), Times.Once);
        _vhostRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<VirtualHost>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeactivateShouldDeactivateActiveVhost()
    {
        var context = CreateContext("OFF", sourceAccount: "TestAccount");

        var account = new Account(Guid.NewGuid(), "TestAccount", "hashed_password");
        _accountRepositoryMock.Setup(r => r.GetByNameAsync("TestAccount", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        _vhostRepositoryMock.Setup(r => r.DeactivateAllForAccountAsync(account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _hostServ.HandleMessageAsync(context, CancellationToken.None);

        _servicesManagerMock.Verify(
            s => s.SendNoticeAsync(
                _hostServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("deactivated")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteShouldRemoveVhost()
    {
        var context = CreateContext("DELETE user.example.net", sourceAccount: "TestAccount");

        var account = new Account(Guid.NewGuid(), "TestAccount", "hashed_password");
        _accountRepositoryMock.Setup(r => r.GetByNameAsync("TestAccount", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var vhost = new VirtualHost(Guid.NewGuid(), account.Id, "user.example.net");
        _vhostRepositoryMock.Setup(r => r.GetByAccountAsync(account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { vhost });

        await _hostServ.HandleMessageAsync(context, CancellationToken.None);

        _servicesManagerMock.Verify(
            s => s.SendNoticeAsync(
                _hostServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("deleted")),
                It.IsAny<CancellationToken>()),
            Times.Once);
        
        _vhostRepositoryMock.Verify(r => r.DeleteAsync(vhost.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListShouldShowUserVhosts()
    {
        var context = CreateContext("LIST", sourceAccount: "TestAccount");

        var account = new Account(Guid.NewGuid(), "TestAccount", "hashed_password");
        _accountRepositoryMock.Setup(r => r.GetByNameAsync("TestAccount", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var vhost1 = new VirtualHost(Guid.NewGuid(), account.Id, "user1.example.net");
        vhost1.Approve("Operator");
        vhost1.Activate();

        var vhost2 = new VirtualHost(Guid.NewGuid(), account.Id, "user2.example.net");

        _vhostRepositoryMock.Setup(r => r.GetByAccountAsync(account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { vhost1, vhost2 });

        await _hostServ.HandleMessageAsync(context, CancellationToken.None);

        _servicesManagerMock.Verify(
            s => s.SendNoticeAsync(
                _hostServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("[ACTIVE]")),
                It.IsAny<CancellationToken>()),
            Times.Once);
        
        _servicesManagerMock.Verify(
            s => s.SendNoticeAsync(
                _hostServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("[PENDING]")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ApproveWithoutOperatorShouldFail()
    {
        var context = CreateContext("APPROVE user.example.net", sourceAccount: "TestAccount", isOperator: false);

        await _hostServ.HandleMessageAsync(context, CancellationToken.None);

        _servicesManagerMock.Verify(
            s => s.SendNoticeAsync(
                _hostServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("Access denied")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ApproveShouldApprovePendingVhost()
    {
        var context = CreateContext("APPROVE user.example.net", sourceAccount: "TestOper", isOperator: true);

        var account = new Account(Guid.NewGuid(), "TestAccount", "hashed_password");
        var vhost = new VirtualHost(Guid.NewGuid(), account.Id, "user.example.net");

        _vhostRepositoryMock.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { vhost });

        await _hostServ.HandleMessageAsync(context, CancellationToken.None);

        _servicesManagerMock.Verify(
            s => s.SendNoticeAsync(
                _hostServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("approved")),
                It.IsAny<CancellationToken>()),
            Times.Once);
        
        _vhostRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<VirtualHost>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectShouldDeletePendingVhost()
    {
        var context = CreateContext("REJECT user.example.net Invalid format", sourceAccount: "TestOper", isOperator: true);

        var account = new Account(Guid.NewGuid(), "TestAccount", "hashed_password");
        var vhost = new VirtualHost(Guid.NewGuid(), account.Id, "user.example.net");

        _vhostRepositoryMock.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { vhost });

        await _hostServ.HandleMessageAsync(context, CancellationToken.None);

        _servicesManagerMock.Verify(
            s => s.SendNoticeAsync(
                _hostServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("rejected")),
                It.IsAny<CancellationToken>()),
            Times.Once);
        
        _vhostRepositoryMock.Verify(r => r.DeleteAsync(vhost.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WaitingShouldListPendingRequests()
    {
        var context = CreateContext("WAITING", sourceAccount: "TestOper", isOperator: true);

        var account = new Account(Guid.NewGuid(), "TestAccount", "hashed_password");
        var vhost1 = new VirtualHost(Guid.NewGuid(), account.Id, "user1.example.net");
        var vhost2 = new VirtualHost(Guid.NewGuid(), account.Id, "user2.example.net");

        _vhostRepositoryMock.Setup(r => r.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { vhost1, vhost2 });

        _accountRepositoryMock.Setup(r => r.GetByIdAsync(account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        await _hostServ.HandleMessageAsync(context, CancellationToken.None);

        _servicesManagerMock.Verify(
            s => s.SendNoticeAsync(
                _hostServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("Pending vhost requests")),
                It.IsAny<CancellationToken>()),
            Times.Once);
        
        _servicesManagerMock.Verify(
            s => s.SendNoticeAsync(
                _hostServ.Uid,
                "001AAAAAB",
                It.Is<string>(msg => msg.Contains("user1.example.net")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void GetHelpShouldReturnGeneralHelp()
    {
        var help = _hostServ.GetHelp();

        help.Should().Contain(h => h.Contains("HostServ Help"));
        help.Should().Contain(h => h.Contains("REQUEST"));
        help.Should().Contain(h => h.Contains("ACTIVATE"));
    }

    [Fact]
    public void GetHelpForRequestShouldReturnRequestHelp()
    {
        var help = _hostServ.GetHelp("REQUEST");

        help.Should().Contain(h => h.Contains("REQUEST"));
        help.Should().Contain(h => h.Contains("Syntax"));
    }

    private ServiceMessageContext CreateContext(string message, string? sourceAccount = null, bool isOperator = false)
    {
        return new ServiceMessageContext(
            "001AAAAAB",
            "TestNick",
            sourceAccount,
            message,
            _servicesManagerMock.Object,
            isOperator);
    }
}


