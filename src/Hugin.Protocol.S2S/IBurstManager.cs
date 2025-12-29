using Hugin.Core.Entities;
using Hugin.Core.ValueObjects;

namespace Hugin.Protocol.S2S;

/// <summary>
/// Manages the burst synchronization process between linked servers.
/// </summary>
/// <remarks>
/// When two servers link, they exchange a "burst" of all current state:
/// 1. SERVER commands for any other linked servers
/// 2. UID commands for all connected users
/// 3. SJOIN commands for all channels
/// 4. MODE/TOPIC commands for channel state
/// </remarks>
public interface IBurstManager
{
    /// <summary>
    /// Sends our complete state to a newly linked server.
    /// </summary>
    /// <param name="targetServer">The server to send burst to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SendBurstAsync(LinkedServer targetServer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a user introduction from a remote server.
    /// </summary>
    /// <param name="uid">The user's UID (9 characters: SID + user ID).</param>
    /// <param name="nickname">The user's nickname.</param>
    /// <param name="username">The user's ident/username.</param>
    /// <param name="hostname">The user's hostname.</param>
    /// <param name="virtualHost">The user's displayed/cloaked hostname.</param>
    /// <param name="realName">The user's real name (gecos).</param>
    /// <param name="timestamp">When the user connected.</param>
    /// <param name="userModes">The user's modes.</param>
    /// <param name="account">The user's account name (null if not logged in).</param>
    /// <param name="sourceServer">The server the user is on.</param>
    /// <returns>The created remote user, or null if creation failed.</returns>
    User? IntroduceRemoteUser(
        string uid,
        string nickname,
        string username,
        string hostname,
        string virtualHost,
        string realName,
        DateTimeOffset timestamp,
        string userModes,
        string? account,
        LinkedServer sourceServer);

    /// <summary>
    /// Removes a remote user (on QUIT/KILL).
    /// </summary>
    /// <param name="uid">The user's UID.</param>
    void RemoveRemoteUser(string uid);

    /// <summary>
    /// Removes all users from a server (on netsplit/SQUIT).
    /// </summary>
    /// <param name="server">The server that disconnected.</param>
    /// <returns>The users that were removed.</returns>
    IEnumerable<User> RemoveUsersOnServer(LinkedServer server);

    /// <summary>
    /// Gets a remote user by UID.
    /// </summary>
    /// <param name="uid">The user's UID.</param>
    /// <returns>The user, or null if not found.</returns>
    User? GetByUid(string uid);

    /// <summary>
    /// Resolves a target (nick or UID) to a user.
    /// </summary>
    /// <param name="target">A nickname or UID.</param>
    /// <returns>The user, or null if not found.</returns>
    User? ResolveUser(string target);

    /// <summary>
    /// Processes a synchronized join (SJOIN) from a remote server.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <param name="channelTimestamp">The channel creation timestamp.</param>
    /// <param name="modes">The channel modes.</param>
    /// <param name="members">List of (UID, ChannelMemberMode) tuples.</param>
    void ProcessSjoin(
        ChannelName channelName,
        DateTimeOffset channelTimestamp,
        string modes,
        IReadOnlyList<(string Uid, string Prefixes)> members);

    /// <summary>
    /// Updates a remote user's nickname.
    /// </summary>
    /// <param name="uid">The user's UID.</param>
    /// <param name="newNickname">The new nickname.</param>
    /// <param name="timestamp">The timestamp of the nick change.</param>
    /// <returns>True if successful, false if nick collision occurred.</returns>
    bool UpdateNickname(string uid, string newNickname, DateTimeOffset timestamp);
}
