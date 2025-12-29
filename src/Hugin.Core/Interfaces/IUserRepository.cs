using Hugin.Core.Entities;
using Hugin.Core.ValueObjects;

namespace Hugin.Core.Interfaces;

/// <summary>
/// Repository for user operations.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Gets a user by connection ID.
    /// </summary>
    User? GetByConnectionId(Guid connectionId);

    /// <summary>
    /// Gets a user by nickname.
    /// </summary>
    User? GetByNickname(Nickname nickname);

    /// <summary>
    /// Gets all connected users.
    /// </summary>
    IEnumerable<User> GetAll();

    /// <summary>
    /// Gets users matching a hostmask pattern.
    /// </summary>
    IEnumerable<User> FindByHostmask(Hostmask pattern);

    /// <summary>
    /// Gets all users in a channel.
    /// </summary>
    IEnumerable<User> GetUsersInChannel(ChannelName channelName);

    /// <summary>
    /// Gets users by account name.
    /// </summary>
    IEnumerable<User> GetByAccount(string accountName);

    /// <summary>
    /// Checks if a nickname is in use.
    /// </summary>
    bool IsNicknameInUse(Nickname nickname);

    /// <summary>
    /// Adds a user.
    /// </summary>
    void Add(User user);

    /// <summary>
    /// Removes a user.
    /// </summary>
    void Remove(Guid connectionId);

    /// <summary>
    /// Gets the total user count.
    /// </summary>
    int GetCount();

    /// <summary>
    /// Gets the invisible user count.
    /// </summary>
    int GetInvisibleCount();

    /// <summary>
    /// Gets the operator count.
    /// </summary>
    int GetOperatorCount();

    /// <summary>
    /// Gets users on a specific server.
    /// </summary>
    IEnumerable<User> GetByServer(ServerId serverId);

    /// <summary>
    /// Gets maximum user count ever seen.
    /// </summary>
    int GetMaxUserCount();
}
