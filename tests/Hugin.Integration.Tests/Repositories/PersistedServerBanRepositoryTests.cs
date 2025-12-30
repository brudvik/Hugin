using FluentAssertions;
using Hugin.Core.Entities;
using Hugin.Persistence;
using Hugin.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hugin.Integration.Tests.Repositories;

/// <summary>
/// Integration tests for PersistedServerBanRepository.
/// </summary>
public sealed class PersistedServerBanRepositoryTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly PersistedServerBanRepository _repository;

    public PersistedServerBanRepositoryTests()
    {
        var services = new ServiceCollection();
        
        services.AddDbContext<HuginDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));

        services.AddLogging();
        
        _serviceProvider = services.BuildServiceProvider();

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var logger = new Mock<ILogger<PersistedServerBanRepository>>();

        _repository = new PersistedServerBanRepository(scopeFactory, logger.Object);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    [Fact]
    public void AddPersistsBanToDatabase()
    {
        // Arrange
        var ban = new ServerBan(
            Guid.NewGuid(),
            BanType.KLine,
            "*@badhost.com",
            "Spam",
            "admin",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1));

        // Act
        _repository.Add(ban);

        // Assert
        var allBans = _repository.GetAllActive();
        allBans.Should().ContainSingle(b => b.Pattern == "*@badhost.com");
    }

    [Fact]
    public async Task AddAsyncPersistsBanToDatabase()
    {
        // Arrange
        var ban = new ServerBan(
            Guid.NewGuid(),
            BanType.GLine,
            "*@globalban.com",
            "Network ban",
            "ircop",
            DateTimeOffset.UtcNow,
            null);

        // Act
        await _repository.AddAsync(ban);

        // Assert
        var allBans = _repository.GetAllActive();
        allBans.Should().ContainSingle(b => b.Pattern == "*@globalban.com");
    }

    [Fact]
    public void RemoveByTypeAndPatternDeletesFromDatabase()
    {
        // Arrange
        var ban = new ServerBan(
            Guid.NewGuid(),
            BanType.KLine,
            "*@removeme.com",
            "Test",
            "admin",
            DateTimeOffset.UtcNow,
            null);
        _repository.Add(ban);

        // Act
        var result = _repository.Remove(BanType.KLine, "*@removeme.com");

        // Assert
        result.Should().BeTrue();
        _repository.GetAllActive().Should().NotContain(b => b.Pattern == "*@removeme.com");
    }

    [Fact]
    public void RemoveByIdDeletesFromDatabase()
    {
        // Arrange
        var ban = new ServerBan(
            Guid.NewGuid(),
            BanType.ZLine,
            "192.168.1.0/24",
            "Bad subnet",
            "admin",
            DateTimeOffset.UtcNow,
            null);
        _repository.Add(ban);

        // Act
        var result = _repository.Remove(ban.Id);

        // Assert
        result.Should().BeTrue();
        _repository.GetAllActive().Should().NotContain(b => b.Id == ban.Id);
    }

    [Fact]
    public void GetByTypeReturnsOnlyMatchingBans()
    {
        // Arrange
        _repository.Add(CreateBan(BanType.KLine, "kline@test.com"));
        _repository.Add(CreateBan(BanType.GLine, "gline@test.com"));
        _repository.Add(CreateBan(BanType.ZLine, "1.2.3.4"));

        // Act
        var klines = _repository.GetByType(BanType.KLine);
        var glines = _repository.GetByType(BanType.GLine);
        var zlines = _repository.GetByType(BanType.ZLine);

        // Assert
        klines.Should().HaveCount(1);
        glines.Should().HaveCount(1);
        zlines.Should().HaveCount(1);
    }

    [Fact]
    public void FindMatchingBanReturnsMatchingKLineOrGLine()
    {
        // Arrange
        _repository.Add(CreateBan(BanType.KLine, "*@*.badhost.com"));

        // Act
        var result = _repository.FindMatchingBan("user@test.badhost.com");

        // Assert
        result.Should().NotBeNull();
        result!.Pattern.Should().Be("*@*.badhost.com");
    }

    [Fact]
    public void FindMatchingZLineReturnsMatchingIpBan()
    {
        // Arrange
        _repository.Add(CreateBan(BanType.ZLine, "10.0.0.*"));

        // Act
        var result = _repository.FindMatchingZLine("10.0.0.5");

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void PurgeExpiredRemovesExpiredBans()
    {
        // Arrange - add an expired ban
        var expiredBan = new ServerBan(
            Guid.NewGuid(),
            BanType.KLine,
            "*@expired.com",
            "Should be removed",
            "admin",
            DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddHours(-1));  // Expired 1 hour ago
        _repository.Add(expiredBan);

        var activeBan = CreateBan(BanType.KLine, "*@active.com");
        _repository.Add(activeBan);

        // Act
        var count = _repository.PurgeExpired();

        // Assert
        count.Should().Be(1);
        _repository.GetAllActive().Should().NotContain(b => b.Pattern == "*@expired.com");
        _repository.GetAllActive().Should().Contain(b => b.Pattern == "*@active.com");
    }

    [Fact]
    public async Task GetActiveGlinesAsyncReturnsOnlyGLines()
    {
        // Arrange
        _repository.Add(CreateBan(BanType.KLine, "kline@test.com"));
        _repository.Add(CreateBan(BanType.GLine, "gline1@test.com"));
        _repository.Add(CreateBan(BanType.GLine, "gline2@test.com"));

        // Act
        var result = await _repository.GetActiveGlinesAsync();

        // Assert
        result.Should().HaveCount(2);
        result.All(b => b.Type == BanType.GLine).Should().BeTrue();
    }

    private static ServerBan CreateBan(BanType type, string pattern)
    {
        return new ServerBan(
            Guid.NewGuid(),
            type,
            pattern,
            "Test reason",
            "admin",
            DateTimeOffset.UtcNow,
            null);
    }
}
