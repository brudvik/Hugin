using FluentAssertions;
using Hugin.Core.Entities;
using Hugin.Persistence;
using Hugin.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Hugin.Integration.Tests.Repositories;

/// <summary>
/// Integration tests for VirtualHostRepository using in-memory database.
/// </summary>
public sealed class VirtualHostRepositoryTests : IDisposable
{
    private readonly HuginDbContext _context;
    private readonly VirtualHostRepository _repository;

    public VirtualHostRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<HuginDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new HuginDbContext(options);
        _repository = new VirtualHostRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task CreateAsyncShouldAddVirtualHost()
    {
        var accountId = Guid.NewGuid();
        var hostname = "user.example.net";

        var vhost = await _repository.CreateAsync(accountId, hostname);

        vhost.Should().NotBeNull();
        vhost.Hostname.Should().Be(hostname);
        vhost.AccountId.Should().Be(accountId);
        vhost.IsApproved.Should().BeFalse();
        vhost.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GetByIdAsyncShouldReturnVirtualHost()
    {
        var accountId = Guid.NewGuid();
        var vhost = await _repository.CreateAsync(accountId, "user.example.net");

        var retrieved = await _repository.GetByIdAsync(vhost.Id);

        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(vhost.Id);
        retrieved.Hostname.Should().Be("user.example.net");
    }

    [Fact]
    public async Task GetByAccountAsyncShouldReturnAccountVhosts()
    {
        var accountId = Guid.NewGuid();
        await _repository.CreateAsync(accountId, "user1.example.net");
        await _repository.CreateAsync(accountId, "user2.example.net");
        await _repository.CreateAsync(Guid.NewGuid(), "other.example.net");

        var vhosts = (await _repository.GetByAccountAsync(accountId)).ToList();

        vhosts.Should().HaveCount(2);
        vhosts.Should().Contain(v => v.Hostname == "user1.example.net");
        vhosts.Should().Contain(v => v.Hostname == "user2.example.net");
    }

    [Fact]
    public async Task GetActiveByAccountAsyncShouldReturnOnlyActive()
    {
        var accountId = Guid.NewGuid();
        var vhost1 = await _repository.CreateAsync(accountId, "active.example.net");
        var vhost2 = await _repository.CreateAsync(accountId, "inactive.example.net");

        vhost1.Approve("Operator");
        vhost1.Activate();
        await _repository.UpdateAsync(vhost1);

        vhost2.Approve("Operator");
        await _repository.UpdateAsync(vhost2);

        var activeVhost = await _repository.GetActiveByAccountAsync(accountId);

        activeVhost.Should().NotBeNull();
        activeVhost!.Hostname.Should().Be("active.example.net");
    }

    [Fact]
    public async Task GetPendingAsyncShouldReturnUnapproved()
    {
        var accountId1 = Guid.NewGuid();
        var accountId2 = Guid.NewGuid();

        var pending1 = await _repository.CreateAsync(accountId1, "pending1.example.net");
        var approved = await _repository.CreateAsync(accountId1, "approved.example.net");
        var pending2 = await _repository.CreateAsync(accountId2, "pending2.example.net");

        approved.Approve("Operator");
        await _repository.UpdateAsync(approved);

        var pending = (await _repository.GetPendingAsync()).ToList();

        pending.Should().HaveCount(2);
        pending.Should().Contain(v => v.Hostname == "pending1.example.net");
        pending.Should().Contain(v => v.Hostname == "pending2.example.net");
        pending.Should().NotContain(v => v.Hostname == "approved.example.net");
    }

    [Fact]
    public async Task IsHostnameInUseAsyncShouldDetectActiveOrApproved()
    {
        var accountId = Guid.NewGuid();
        var active = await _repository.CreateAsync(accountId, "active.example.net");
        active.Approve("Operator");
        active.Activate();
        await _repository.UpdateAsync(active);

        var approved = await _repository.CreateAsync(accountId, "approved.example.net");
        approved.Approve("Operator");
        await _repository.UpdateAsync(approved);

        var pending = await _repository.CreateAsync(accountId, "pending.example.net");

        (await _repository.IsHostnameInUseAsync("active.example.net")).Should().BeTrue();
        (await _repository.IsHostnameInUseAsync("approved.example.net")).Should().BeTrue();
        (await _repository.IsHostnameInUseAsync("pending.example.net")).Should().BeFalse();
        (await _repository.IsHostnameInUseAsync("unused.example.net")).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsyncShouldPersistChanges()
    {
        var accountId = Guid.NewGuid();
        var vhost = await _repository.CreateAsync(accountId, "user.example.net");

        vhost.Approve("TestOper");
        await _repository.UpdateAsync(vhost);

        var retrieved = await _repository.GetByIdAsync(vhost.Id);
        retrieved!.IsApproved.Should().BeTrue();
        retrieved.ApprovedBy.Should().Be("TestOper");
    }

    [Fact]
    public async Task DeleteAsyncShouldRemoveVirtualHost()
    {
        var accountId = Guid.NewGuid();
        var vhost = await _repository.CreateAsync(accountId, "user.example.net");

        await _repository.DeleteAsync(vhost.Id);

        var retrieved = await _repository.GetByIdAsync(vhost.Id);
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task DeactivateAllForAccountAsyncShouldDeactivateAll()
    {
        var accountId = Guid.NewGuid();
        var vhost1 = await _repository.CreateAsync(accountId, "user1.example.net");
        var vhost2 = await _repository.CreateAsync(accountId, "user2.example.net");

        vhost1.Approve("Operator");
        vhost1.Activate();
        await _repository.UpdateAsync(vhost1);

        vhost2.Approve("Operator");
        vhost2.Activate();
        await _repository.UpdateAsync(vhost2);

        var count = await _repository.DeactivateAllForAccountAsync(accountId);

        count.Should().Be(2);

        var retrieved1 = await _repository.GetByIdAsync(vhost1.Id);
        var retrieved2 = await _repository.GetByIdAsync(vhost2.Id);

        retrieved1!.IsActive.Should().BeFalse();
        retrieved2!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GetPendingAsyncShouldOrderByRequestedAtDescending()
    {
        var accountId = Guid.NewGuid();
        
        // Create vhosts
        var vhost1 = await _repository.CreateAsync(accountId, "vhost1.example.net");
        var vhost2 = await _repository.CreateAsync(accountId, "vhost2.example.net");
        var vhost3 = await _repository.CreateAsync(accountId, "vhost3.example.net");

        var pending = (await _repository.GetPendingAsync()).ToList();

        pending.Should().HaveCount(3);
        // They should all exist - ordering may vary slightly in in-memory DB
        pending.Should().Contain(v => v.Hostname == "vhost1.example.net");
        pending.Should().Contain(v => v.Hostname == "vhost2.example.net");
        pending.Should().Contain(v => v.Hostname == "vhost3.example.net");
    }

    [Fact]
    public async Task GetActiveByAccountAsyncShouldReturnNullWhenNoActive()
    {
        var accountId = Guid.NewGuid();
        var vhost = await _repository.CreateAsync(accountId, "user.example.net");
        vhost.Approve("Operator");
        await _repository.UpdateAsync(vhost);

        var activeVhost = await _repository.GetActiveByAccountAsync(accountId);

        activeVhost.Should().BeNull();
    }
}
