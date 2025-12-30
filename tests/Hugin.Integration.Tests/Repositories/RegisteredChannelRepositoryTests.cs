using FluentAssertions;
using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Hugin.Persistence;
using Hugin.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Hugin.Integration.Tests.Repositories;

/// <summary>
/// Integration tests for RegisteredChannelRepository using in-memory database.
/// </summary>
public sealed class RegisteredChannelRepositoryTests : IDisposable
{
    private readonly HuginDbContext _dbContext;
    private readonly RegisteredChannelRepository _repository;

    public RegisteredChannelRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<HuginDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new HuginDbContext(options);
        _repository = new RegisteredChannelRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task CreateAsyncCreatesNewChannel()
    {
        // Arrange
        var founderId = Guid.NewGuid();
        var channelName = "#testchannel";

        // Act
        var result = await _repository.CreateAsync(channelName, founderId);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be(channelName);
        result.FounderId.Should().Be(founderId);
        result.KeepTopic.Should().BeTrue();
        result.Secure.Should().BeFalse();
    }

    [Fact]
    public async Task GetByNameAsyncReturnsChannel()
    {
        // Arrange
        var founderId = Guid.NewGuid();
        var channelName = "#findme";
        await _repository.CreateAsync(channelName, founderId);

        // Act
        var result = await _repository.GetByNameAsync(channelName);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be(channelName);
    }

    [Fact]
    public async Task GetByNameAsyncReturnsNullForUnknown()
    {
        // Act
        var result = await _repository.GetByNameAsync("#unknown");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsyncReturnsChannel()
    {
        // Arrange
        var founderId = Guid.NewGuid();
        var created = await _repository.CreateAsync("#byid", founderId);

        // Act
        var result = await _repository.GetByIdAsync(created.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetByFounderAsyncReturnsAllChannels()
    {
        // Arrange
        var founderId = Guid.NewGuid();
        await _repository.CreateAsync("#channel1", founderId);
        await _repository.CreateAsync("#channel2", founderId);
        await _repository.CreateAsync("#other", Guid.NewGuid());

        // Act
        var result = await _repository.GetByFounderAsync(founderId);

        // Assert
        result.Should().HaveCount(2);
        result.All(c => c.FounderId == founderId).Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsyncReturnsTrueForExisting()
    {
        // Arrange
        await _repository.CreateAsync("#exists", Guid.NewGuid());

        // Act
        var result = await _repository.ExistsAsync("#exists");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsyncReturnsFalseForNonExisting()
    {
        // Act
        var result = await _repository.ExistsAsync("#nonexistent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsyncModifiesChannel()
    {
        // Arrange
        var founderId = Guid.NewGuid();
        var created = await _repository.CreateAsync("#toupdate", founderId);
        
        // Create updated channel using property setters
        var updated = new RegisteredChannel(created.Id, created.Name, created.FounderId)
        {
            Topic = "New Topic",
            Modes = "nt",
            Key = "secret",
            KeepTopic = true,
            Secure = true,
            SuccessorId = Guid.NewGuid()
        };

        // Act
        await _repository.UpdateAsync(updated);
        var result = await _repository.GetByIdAsync(created.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Topic.Should().Be("New Topic");
        result.Modes.Should().Be("nt");
        result.KeepTopic.Should().BeTrue();
        result.Secure.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsyncRemovesChannel()
    {
        // Arrange
        var created = await _repository.CreateAsync("#todelete", Guid.NewGuid());

        // Act
        await _repository.DeleteAsync(created.Id);
        var result = await _repository.GetByIdAsync(created.Id);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsyncDoesNotThrowForUnknown()
    {
        // Act & Assert - should not throw
        await _repository.DeleteAsync(Guid.NewGuid());
    }
}
