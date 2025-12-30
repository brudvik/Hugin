using FluentAssertions;
using Hugin.Core.Entities;
using Hugin.Persistence;
using Hugin.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Hugin.Integration.Tests.Repositories;

/// <summary>
/// Integration tests for MemoRepository using in-memory database.
/// </summary>
public sealed class MemoRepositoryTests : IDisposable
{
    private readonly HuginDbContext _dbContext;
    private readonly MemoRepository _repository;

    public MemoRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<HuginDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new HuginDbContext(options);
        _repository = new MemoRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task CreateAsyncCreatesNewMemo()
    {
        // Arrange
        var senderId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var text = "Test memo message";

        // Act
        var result = await _repository.CreateAsync(senderId, "SenderNick", recipientId, text);

        // Assert
        result.Should().NotBeNull();
        result.SenderId.Should().Be(senderId);
        result.RecipientId.Should().Be(recipientId);
        result.SenderNickname.Should().Be("SenderNick");
        result.Text.Should().Be(text);
        result.IsRead.Should().BeFalse();
        result.ReadAt.Should().BeNull();
    }

    [Fact]
    public async Task GetByRecipientAsyncReturnsAllMemos()
    {
        // Arrange
        var recipientId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        
        await _repository.CreateAsync(Guid.NewGuid(), "Sender1", recipientId, "Memo 1");
        await _repository.CreateAsync(Guid.NewGuid(), "Sender2", recipientId, "Memo 2");
        await _repository.CreateAsync(Guid.NewGuid(), "Sender3", otherId, "Other memo");

        // Act
        var result = (await _repository.GetByRecipientAsync(recipientId)).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.All(m => m.RecipientId == recipientId).Should().BeTrue();
    }

    [Fact]
    public async Task GetUnreadByRecipientAsyncReturnsOnlyUnread()
    {
        // Arrange
        var recipientId = Guid.NewGuid();
        
        var memo1 = await _repository.CreateAsync(Guid.NewGuid(), "Sender1", recipientId, "Unread memo");
        var memo2 = await _repository.CreateAsync(Guid.NewGuid(), "Sender2", recipientId, "Will be read");
        
        await _repository.MarkAsReadAsync(memo2.Id);

        // Act
        var result = (await _repository.GetUnreadByRecipientAsync(recipientId)).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Id.Should().Be(memo1.Id);
        result[0].IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task GetByIdAsyncReturnsMemo()
    {
        // Arrange
        var memo = await _repository.CreateAsync(Guid.NewGuid(), "Sender", Guid.NewGuid(), "Test");

        // Act
        var result = await _repository.GetByIdAsync(memo.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(memo.Id);
    }

    [Fact]
    public async Task GetByIdAsyncReturnsNullForUnknown()
    {
        // Act
        var result = await _repository.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task MarkAsReadAsyncSetsSetsReadTimestamp()
    {
        // Arrange
        var memo = await _repository.CreateAsync(Guid.NewGuid(), "Sender", Guid.NewGuid(), "Test");
        memo.IsRead.Should().BeFalse();

        // Act
        await _repository.MarkAsReadAsync(memo.Id);
        var updated = await _repository.GetByIdAsync(memo.Id);

        // Assert
        updated.Should().NotBeNull();
        updated!.IsRead.Should().BeTrue();
        updated.ReadAt.Should().NotBeNull();
        updated.ReadAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task MarkAsReadAsyncIgnoresNonExistentMemo()
    {
        // Act & Assert - should not throw
        await _repository.MarkAsReadAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task DeleteAsyncRemovesMemo()
    {
        // Arrange
        var memo = await _repository.CreateAsync(Guid.NewGuid(), "Sender", Guid.NewGuid(), "Test");

        // Act
        await _repository.DeleteAsync(memo.Id);
        var result = await _repository.GetByIdAsync(memo.Id);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsyncIgnoresNonExistentMemo()
    {
        // Act & Assert - should not throw
        await _repository.DeleteAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task DeleteAllByRecipientAsyncRemovesAllMemosForRecipient()
    {
        // Arrange
        var recipientId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        
        await _repository.CreateAsync(Guid.NewGuid(), "Sender1", recipientId, "Memo 1");
        await _repository.CreateAsync(Guid.NewGuid(), "Sender2", recipientId, "Memo 2");
        await _repository.CreateAsync(Guid.NewGuid(), "Sender3", recipientId, "Memo 3");
        await _repository.CreateAsync(Guid.NewGuid(), "Sender4", otherId, "Other memo");

        // Act
        var count = await _repository.DeleteAllByRecipientAsync(recipientId);

        // Assert
        count.Should().Be(3);
        var remaining = await _repository.GetByRecipientAsync(recipientId);
        remaining.Should().BeEmpty();
        
        var otherRemaining = await _repository.GetByRecipientAsync(otherId);
        otherRemaining.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetUnreadCountAsyncReturnsCorrectCount()
    {
        // Arrange
        var recipientId = Guid.NewGuid();
        
        var memo1 = await _repository.CreateAsync(Guid.NewGuid(), "Sender1", recipientId, "Unread 1");
        await _repository.CreateAsync(Guid.NewGuid(), "Sender2", recipientId, "Unread 2");
        var memo3 = await _repository.CreateAsync(Guid.NewGuid(), "Sender3", recipientId, "Will be read");
        
        await _repository.MarkAsReadAsync(memo3.Id);

        // Act
        var count = await _repository.GetUnreadCountAsync(recipientId);

        // Assert
        count.Should().Be(2);
    }

    [Fact]
    public async Task GetUnreadCountAsyncReturnsZeroForNoMemos()
    {
        // Act
        var count = await _repository.GetUnreadCountAsync(Guid.NewGuid());

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public async Task MemosAreOrderedBySentAt()
    {
        // Arrange
        var recipientId = Guid.NewGuid();
        var baseTime = DateTimeOffset.UtcNow;
        
        // Create memos with specific times (simulate different times)
        var memo1 = await _repository.CreateAsync(Guid.NewGuid(), "Sender1", recipientId, "First");
        await Task.Delay(10); // Small delay to ensure different timestamps
        var memo2 = await _repository.CreateAsync(Guid.NewGuid(), "Sender2", recipientId, "Second");
        await Task.Delay(10);
        var memo3 = await _repository.CreateAsync(Guid.NewGuid(), "Sender3", recipientId, "Third");

        // Act
        var memos = (await _repository.GetByRecipientAsync(recipientId)).ToList();

        // Assert
        memos.Should().HaveCount(3);
        memos[0].Id.Should().Be(memo1.Id);
        memos[1].Id.Should().Be(memo2.Id);
        memos[2].Id.Should().Be(memo3.Id);
    }
}
