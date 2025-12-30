using FluentAssertions;
using Hugin.Persistence;
using Hugin.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Hugin.Integration.Tests.Repositories;

/// <summary>
/// Integration tests for BotRepository using in-memory database.
/// </summary>
public sealed class BotRepositoryTests : IDisposable
{
    private readonly HuginDbContext _dbContext;
    private readonly BotRepository _repository;

    public BotRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<HuginDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new HuginDbContext(options);
        _repository = new BotRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task CreateAsyncCreatesNewBot()
    {
        // Arrange & Act
        var result = await _repository.CreateAsync("TestBot", "bot", "A test bot", "001AAAAA1");

        // Assert
        result.Should().NotBeNull();
        result.Nickname.Should().Be("TestBot");
        result.Ident.Should().Be("bot");
        result.Realname.Should().Be("A test bot");
        result.Uid.Should().Be("001AAAAA1");
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetByIdAsyncReturnsBot()
    {
        // Arrange
        var created = await _repository.CreateAsync("TestBot", "bot", "A test bot", "001AAAAA1");

        // Act
        var result = await _repository.GetByIdAsync(created.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetByNicknameAsyncReturnsBot()
    {
        // Arrange
        await _repository.CreateAsync("TestBot", "bot", "A test bot", "001AAAAA1");

        // Act
        var result = await _repository.GetByNicknameAsync("TestBot");

        // Assert
        result.Should().NotBeNull();
        result!.Nickname.Should().Be("TestBot");
    }

    [Fact]
    public async Task GetAllActiveAsyncReturnsOnlyActiveBots()
    {
        // Arrange
        var bot1 = await _repository.CreateAsync("ActiveBot", "bot", "Active", "001AAAAA1");
        await _repository.CreateAsync("ActiveBot2", "bot", "Active", "001AAAAA2");
        var bot3 = await _repository.CreateAsync("InactiveBot", "bot", "Inactive", "001AAAAA3");
        
        bot3.IsActive = false;
        await _repository.UpdateAsync(bot3);

        // Act
        var result = (await _repository.GetAllActiveAsync()).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().OnlyContain(b => b.IsActive);
    }

    [Fact]
    public async Task UpdateAsyncModifiesBot()
    {
        // Arrange
        var bot = await _repository.CreateAsync("TestBot", "bot", "Original", "001AAAAA1");
        bot.Realname = "Updated";
        bot.IsActive = false;

        // Act
        await _repository.UpdateAsync(bot);
        var updated = await _repository.GetByIdAsync(bot.Id);

        // Assert
        updated.Should().NotBeNull();
        updated!.Realname.Should().Be("Updated");
        updated.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsyncRemovesBot()
    {
        // Arrange
        var bot = await _repository.CreateAsync("TestBot", "bot", "Test", "001AAAAA1");

        // Act
        await _repository.DeleteAsync(bot.Id);
        var result = await _repository.GetByIdAsync(bot.Id);

        // Assert
        result.Should().BeNull();
    }
}

/// <summary>
/// Integration tests for ChannelBotRepository using in-memory database.
/// </summary>
public sealed class ChannelBotRepositoryTests : IDisposable
{
    private readonly HuginDbContext _dbContext;
    private readonly ChannelBotRepository _repository;
    private readonly BotRepository _botRepository;

    public ChannelBotRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<HuginDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new HuginDbContext(options);
        _repository = new ChannelBotRepository(_dbContext);
        _botRepository = new BotRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task AssignAsyncCreatesAssignment()
    {
        // Arrange
        var bot = await _botRepository.CreateAsync("TestBot", "bot", "Test", "001AAAAA1");
        var assignedBy = Guid.NewGuid();

        // Act
        var result = await _repository.AssignAsync(bot.Id, "#test", assignedBy);

        // Assert
        result.Should().NotBeNull();
        result.BotId.Should().Be(bot.Id);
        result.ChannelName.Should().Be("#test");
        result.AssignedBy.Should().Be(assignedBy);
    }

    [Fact]
    public async Task GetByChannelAsyncReturnsAllAssignments()
    {
        // Arrange
        var bot1 = await _botRepository.CreateAsync("Bot1", "bot", "Test", "001AAAAA1");
        var bot2 = await _botRepository.CreateAsync("Bot2", "bot", "Test", "001AAAAA2");
        var assignedBy = Guid.NewGuid();

        await _repository.AssignAsync(bot1.Id, "#test", assignedBy);
        await _repository.AssignAsync(bot2.Id, "#test", assignedBy);
        await _repository.AssignAsync(bot1.Id, "#other", assignedBy);

        // Act
        var result = (await _repository.GetByChannelAsync("#test")).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().OnlyContain(a => a.ChannelName == "#test");
    }

    [Fact]
    public async Task GetAssignmentAsyncReturnsSpecificAssignment()
    {
        // Arrange
        var bot = await _botRepository.CreateAsync("TestBot", "bot", "Test", "001AAAAA1");
        var assignedBy = Guid.NewGuid();
        await _repository.AssignAsync(bot.Id, "#test", assignedBy);

        // Act
        var result = await _repository.GetAssignmentAsync(bot.Id, "#test");

        // Assert
        result.Should().NotBeNull();
        result!.BotId.Should().Be(bot.Id);
        result.ChannelName.Should().Be("#test");
    }

    [Fact]
    public async Task GetByBotAsyncReturnsAllChannelsForBot()
    {
        // Arrange
        var bot = await _botRepository.CreateAsync("TestBot", "bot", "Test", "001AAAAA1");
        var assignedBy = Guid.NewGuid();

        await _repository.AssignAsync(bot.Id, "#channel1", assignedBy);
        await _repository.AssignAsync(bot.Id, "#channel2", assignedBy);

        // Act
        var result = (await _repository.GetByBotAsync(bot.Id)).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().OnlyContain(a => a.BotId == bot.Id);
    }

    [Fact]
    public async Task UnassignAsyncRemovesAssignment()
    {
        // Arrange
        var bot = await _botRepository.CreateAsync("TestBot", "bot", "Test", "001AAAAA1");
        var assignedBy = Guid.NewGuid();
        await _repository.AssignAsync(bot.Id, "#test", assignedBy);

        // Act
        var result = await _repository.UnassignAsync(bot.Id, "#test");
        var assignment = await _repository.GetAssignmentAsync(bot.Id, "#test");

        // Assert
        result.Should().BeTrue();
        assignment.Should().BeNull();
    }

    [Fact]
    public async Task UnassignAsyncReturnsFalseForNonExistent()
    {
        // Arrange
        var bot = await _botRepository.CreateAsync("TestBot", "bot", "Test", "001AAAAA1");

        // Act
        var result = await _repository.UnassignAsync(bot.Id, "#nonexistent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsyncModifiesAssignment()
    {
        // Arrange
        var bot = await _botRepository.CreateAsync("TestBot", "bot", "Test", "001AAAAA1");
        var assignedBy = Guid.NewGuid();
        var assignment = await _repository.AssignAsync(bot.Id, "#test", assignedBy);

        assignment.AutoGreet = true;
        assignment.GreetMessage = "Welcome!";

        // Act
        await _repository.UpdateAsync(assignment);
        var updated = await _repository.GetAssignmentAsync(bot.Id, "#test");

        // Assert
        updated.Should().NotBeNull();
        updated!.AutoGreet.Should().BeTrue();
        updated.GreetMessage.Should().Be("Welcome!");
    }
}
