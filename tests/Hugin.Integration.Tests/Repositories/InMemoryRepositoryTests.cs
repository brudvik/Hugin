using System.Net;
using FluentAssertions;
using Hugin.Core.Entities;
using Hugin.Core.Enums;
using Hugin.Core.ValueObjects;
using Hugin.Persistence.Repositories;
using Xunit;

namespace Hugin.Integration.Tests.Repositories;

/// <summary>
/// Tests for the InMemoryUserRepository.
/// </summary>
public class InMemoryUserRepositoryTests
{
    private readonly InMemoryUserRepository _repository = new();

    [Fact]
    public void AddUserStoresUser()
    {
        // Arrange
        var user = CreateTestUser(Guid.NewGuid(), "TestUser");

        // Act
        _repository.Add(user);

        // Assert
        _repository.GetByConnectionId(user.ConnectionId).Should().NotBeNull();
    }

    [Fact]
    public void GetByConnectionIdReturnsNullForUnknown()
    {
        _repository.GetByConnectionId(Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public void GetByNicknameFindsRegisteredUser()
    {
        // Arrange
        var user = CreateTestUser(Guid.NewGuid(), "TestUser");
        _repository.Add(user);
        _repository.RegisterNickname(user.ConnectionId, null!, user.Nickname!);

        // Act
        var found = _repository.GetByNickname(user.Nickname!);

        // Assert
        found.Should().NotBeNull();
        found!.ConnectionId.Should().Be(user.ConnectionId);
    }

    [Fact]
    public void GetByNicknameReturnsNullForUnknown()
    {
        Nickname.TryCreate("Unknown", out var nick, out _);
        _repository.GetByNickname(nick!).Should().BeNull();
    }

    [Fact]
    public void IsNicknameInUseReturnsTrueForRegistered()
    {
        // Arrange
        var user = CreateTestUser(Guid.NewGuid(), "TakenNick");
        _repository.Add(user);
        _repository.RegisterNickname(user.ConnectionId, null!, user.Nickname!);

        // Act & Assert
        _repository.IsNicknameInUse(user.Nickname!).Should().BeTrue();
    }

    [Fact]
    public void IsNicknameInUseReturnsFalseForFree()
    {
        Nickname.TryCreate("FreeNick", out var nick, out _);
        _repository.IsNicknameInUse(nick!).Should().BeFalse();
    }

    [Fact]
    public void RemoveDeletesUser()
    {
        // Arrange
        var user = CreateTestUser(Guid.NewGuid(), "ToRemove");
        _repository.Add(user);
        _repository.RegisterNickname(user.ConnectionId, null!, user.Nickname!);

        // Act
        _repository.Remove(user.ConnectionId);

        // Assert
        _repository.GetByConnectionId(user.ConnectionId).Should().BeNull();
        _repository.IsNicknameInUse(user.Nickname!).Should().BeFalse();
    }

    [Fact]
    public void GetAllReturnsAllUsers()
    {
        // Arrange
        var user1 = CreateTestUser(Guid.NewGuid(), "User1");
        var user2 = CreateTestUser(Guid.NewGuid(), "User2");
        _repository.Add(user1);
        _repository.Add(user2);

        // Act
        var all = _repository.GetAll().ToList();

        // Assert
        all.Should().HaveCount(2);
    }

    [Fact]
    public void GetCountReturnsCorrectNumber()
    {
        // Arrange
        _repository.Add(CreateTestUser(Guid.NewGuid(), "User1"));
        _repository.Add(CreateTestUser(Guid.NewGuid(), "User2"));

        // Assert
        _repository.GetCount().Should().Be(2);
    }

    [Fact]
    public void GetMaxUserCountTracksHighWaterMark()
    {
        // Arrange
        var user1 = CreateTestUser(Guid.NewGuid(), "User1");
        var user2 = CreateTestUser(Guid.NewGuid(), "User2");
        _repository.Add(user1);
        _repository.Add(user2);

        // Act
        _repository.Remove(user1.ConnectionId);

        // Assert - Max should still be 2
        _repository.GetMaxUserCount().Should().Be(2);
        _repository.GetCount().Should().Be(1);
    }

    [Fact]
    public void RegisterNicknameUpdatesIndex()
    {
        // Arrange
        var user = CreateTestUser(Guid.NewGuid(), "OldNick");
        _repository.Add(user);
        _repository.RegisterNickname(user.ConnectionId, null!, user.Nickname!);

        // Act
        Nickname.TryCreate("NewNick", out var newNick, out _);
        _repository.RegisterNickname(user.ConnectionId, user.Nickname!, newNick!);

        // Assert
        _repository.IsNicknameInUse(user.Nickname!).Should().BeFalse();
        _repository.GetByNickname(newNick!).Should().NotBeNull();
    }

    [Fact]
    public void AddDuplicateConnectionIdThrows()
    {
        // Arrange
        var connectionId = Guid.NewGuid();
        var user1 = CreateTestUser(connectionId, "User1");
        var user2 = CreateTestUser(connectionId, "User2");
        _repository.Add(user1);

        // Act & Assert
        var act = () => _repository.Add(user2);
        act.Should().Throw<InvalidOperationException>();
    }

    private static User CreateTestUser(Guid connectionId, string nickname)
    {
        Nickname.TryCreate(nickname, out var nick, out _);
        var user = new User(
            connectionId,
            IPAddress.Loopback,
            "localhost",
            ServerId.Create("001", "test.server.com"),
            isSecure: true);
        user.SetNickname(nick!);
        return user;
    }
}

/// <summary>
/// Tests for the InMemoryChannelRepository.
/// </summary>
public class InMemoryChannelRepositoryTests
{
    private readonly InMemoryChannelRepository _repository = new();

    [Fact]
    public void CreateChannelAddsToRepository()
    {
        // Arrange
        ChannelName.TryCreate("#test", out var name, out _);

        // Act
        var channel = _repository.Create(name!);

        // Assert
        channel.Should().NotBeNull();
        _repository.Exists(name!).Should().BeTrue();
    }

    [Fact]
    public void GetByNameReturnsChannel()
    {
        // Arrange
        ChannelName.TryCreate("#test", out var name, out _);
        _repository.Create(name!);

        // Act
        var found = _repository.GetByName(name!);

        // Assert
        found.Should().NotBeNull();
        found!.Name.Should().Be(name);
    }

    [Fact]
    public void GetByNameReturnsNullForUnknown()
    {
        ChannelName.TryCreate("#unknown", out var name, out _);
        _repository.GetByName(name!).Should().BeNull();
    }

    [Fact]
    public void ExistsReturnsFalseForUnknown()
    {
        ChannelName.TryCreate("#unknown", out var name, out _);
        _repository.Exists(name!).Should().BeFalse();
    }

    [Fact]
    public void RemoveDeletesChannel()
    {
        // Arrange
        ChannelName.TryCreate("#test", out var name, out _);
        _repository.Create(name!);

        // Act
        _repository.Remove(name!);

        // Assert
        _repository.Exists(name!).Should().BeFalse();
    }

    [Fact]
    public void GetAllReturnsAllChannels()
    {
        // Arrange
        ChannelName.TryCreate("#test1", out var name1, out _);
        ChannelName.TryCreate("#test2", out var name2, out _);
        _repository.Create(name1!);
        _repository.Create(name2!);

        // Act
        var all = _repository.GetAll().ToList();

        // Assert
        all.Should().HaveCount(2);
    }

    [Fact]
    public void GetCountReturnsCorrectNumber()
    {
        // Arrange
        ChannelName.TryCreate("#test1", out var name1, out _);
        ChannelName.TryCreate("#test2", out var name2, out _);
        _repository.Create(name1!);
        _repository.Create(name2!);

        // Assert
        _repository.GetCount().Should().Be(2);
    }

    [Fact]
    public void SearchWithWildcardMatchesAll()
    {
        // Arrange
        ChannelName.TryCreate("#test", out var name, out _);
        _repository.Create(name!);

        // Act
        var results = _repository.Search("*").ToList();

        // Assert
        results.Should().HaveCount(1);
    }

    [Fact]
    public void SearchWithPatternFiltersChannels()
    {
        // Arrange
        ChannelName.TryCreate("#foo", out var name1, out _);
        ChannelName.TryCreate("#bar", out var name2, out _);
        _repository.Create(name1!);
        _repository.Create(name2!);

        // Act
        var results = _repository.Search("#f*").ToList();

        // Assert
        results.Should().HaveCount(1);
        results[0].Name.Value.Should().Be("#foo");
    }

    [Fact]
    public void SearchWithExactNameReturnsOneChannel()
    {
        // Arrange
        ChannelName.TryCreate("#exact", out var name, out _);
        _repository.Create(name!);

        // Act
        var results = _repository.Search("#exact").ToList();

        // Assert
        results.Should().HaveCount(1);
    }

    [Fact]
    public void CreateDuplicateChannelThrows()
    {
        // Arrange
        ChannelName.TryCreate("#duplicate", out var name, out _);
        _repository.Create(name!);

        // Act & Assert
        var act = () => _repository.Create(name!);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddChannelStoresIt()
    {
        // Arrange
        ChannelName.TryCreate("#added", out var name, out _);
        var channel = new Channel(name!);

        // Act
        _repository.Add(channel);

        // Assert
        _repository.Exists(name!).Should().BeTrue();
    }
}
