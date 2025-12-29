using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Hugin.Core.Entities;
using Hugin.Core.Enums;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Hugin.Protocol.S2S;

/// <summary>
/// Implementation of burst synchronization between linked servers.
/// </summary>
public sealed class BurstManager : IBurstManager
{
    private readonly IUserRepository _userRepository;
    private readonly IChannelRepository _channelRepository;
    private readonly IServerLinkManager _linkManager;
    private readonly ServerId _localServerId;
    private readonly ILogger<BurstManager> _logger;

    /// <summary>
    /// Maps UIDs to connection IDs for remote users.
    /// </summary>
    private readonly ConcurrentDictionary<string, Guid> _uidToConnectionId = new();

    /// <summary>
    /// Maps connection IDs back to UIDs for remote users.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, string> _connectionIdToUid = new();

    /// <summary>
    /// Creates a new burst manager.
    /// </summary>
    public BurstManager(
        IUserRepository userRepository,
        IChannelRepository channelRepository,
        IServerLinkManager linkManager,
        ServerId localServerId,
        ILogger<BurstManager> logger)
    {
        _userRepository = userRepository;
        _channelRepository = channelRepository;
        _linkManager = linkManager;
        _localServerId = localServerId;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask SendBurstAsync(LinkedServer targetServer, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending burst to server {ServerName} ({Sid})", 
            targetServer.Id.Name, targetServer.Id.Sid);

        // 1. Send other linked servers (not the target)
        await SendServerBurstAsync(targetServer, cancellationToken);

        // 2. Send all local users
        await SendUserBurstAsync(targetServer, cancellationToken);

        // 3. Send all channels with members
        await SendChannelBurstAsync(targetServer, cancellationToken);

        _logger.LogInformation("Burst complete to server {ServerName}", targetServer.Id.Name);
    }

    private async ValueTask SendServerBurstAsync(LinkedServer targetServer, CancellationToken cancellationToken)
    {
        foreach (var server in _linkManager.AllServers)
        {
            if (server.Id.Sid == targetServer.Id.Sid)
            {
                continue; // Don't send them back to themselves
            }

            var serverMsg = S2SMessage.CreateWithSource(
                _localServerId.Sid,
                "SERVER",
                server.Id.Name,
                (server.HopCount + 1).ToString(CultureInfo.InvariantCulture),
                server.Id.Sid,
                server.Description);

            await _linkManager.SendToServerAsync(targetServer.Id, serverMsg, cancellationToken);
        }
    }

    private async ValueTask SendUserBurstAsync(LinkedServer targetServer, CancellationToken cancellationToken)
    {
        var localUsers = _userRepository.GetByServer(_localServerId);
        var userCount = 0;

        foreach (var user in localUsers)
        {
            if (!user.IsRegistered)
            {
                continue; // Don't send unregistered users
            }

            var uid = GenerateUid(user.ConnectionId);
            var timestamp = user.ConnectedAt.ToUnixTimeSeconds();
            var modes = FormatUserModes(user.Modes);
            var virtualHost = user.DisplayedHostname;

            // UID format: nickname hopcount timestamp username hostname uid servicestamp modes virtualhost :realname
            var uidMsg = S2SMessage.CreateWithSource(
                _localServerId.Sid,
                "UID",
                user.Nickname.Value,
                "1", // Hop count
                timestamp.ToString(CultureInfo.InvariantCulture),
                user.Username,
                user.Hostname,
                uid,
                user.Account ?? "0", // Service stamp (account or 0)
                modes,
                virtualHost,
                user.RealName);

            await _linkManager.SendToServerAsync(targetServer.Id, uidMsg, cancellationToken);
            userCount++;
        }

        _logger.LogDebug("Sent {UserCount} users to {ServerName}", userCount, targetServer.Id.Name);
    }

    private async ValueTask SendChannelBurstAsync(LinkedServer targetServer, CancellationToken cancellationToken)
    {
        var channelCount = 0;

        foreach (var channel in _channelRepository.GetAll())
        {
            if (channel.IsEmpty)
            {
                continue;
            }

            // Build member list with prefixes
            var memberList = new List<string>();
            foreach (var (connectionId, member) in channel.Members)
            {
                // Get UID for local users
                var uid = GetUidForConnection(connectionId);
                if (uid == null)
                {
                    continue;
                }

                var prefix = FormatMemberPrefix(member.Modes);
                memberList.Add($"{prefix}{uid}");
            }

            if (memberList.Count == 0)
            {
                continue;
            }

            var timestamp = channel.CreatedAt.ToUnixTimeSeconds();
            var modes = FormatChannelModes(channel);
            var members = string.Join(" ", memberList);

            // SJOIN format: :SID SJOIN timestamp #channel +modes :members
            var sjoinMsg = S2SMessage.CreateWithSource(
                _localServerId.Sid,
                "SJOIN",
                timestamp.ToString(CultureInfo.InvariantCulture),
                channel.Name.Value,
                modes,
                members);

            await _linkManager.SendToServerAsync(targetServer.Id, sjoinMsg, cancellationToken);
            channelCount++;

            // Send TOPIC if set
            if (!string.IsNullOrEmpty(channel.Topic) && channel.TopicSetAt.HasValue)
            {
                var topicTs = channel.TopicSetAt.Value.ToUnixTimeSeconds();
                var topicMsg = S2SMessage.CreateWithSource(
                    _localServerId.Sid,
                    "TOPIC",
                    channel.Name.Value,
                    channel.TopicSetBy ?? _localServerId.Name,
                    topicTs.ToString(CultureInfo.InvariantCulture),
                    channel.Topic);

                await _linkManager.SendToServerAsync(targetServer.Id, topicMsg, cancellationToken);
            }
        }

        _logger.LogDebug("Sent {ChannelCount} channels to {ServerName}", channelCount, targetServer.Id.Name);
    }

    /// <inheritdoc />
    public User? IntroduceRemoteUser(
        string uid,
        string nickname,
        string username,
        string hostname,
        string virtualHost,
        string realName,
        DateTimeOffset timestamp,
        string userModes,
        string? account,
        LinkedServer sourceServer)
    {
        if (uid.Length != 9)
        {
            _logger.LogWarning("Invalid UID format: {Uid}", uid);
            return null;
        }

        // Generate a deterministic connection ID from the UID
        var connectionId = GenerateConnectionIdFromUid(uid);

        // Check for duplicate
        if (_uidToConnectionId.ContainsKey(uid))
        {
            _logger.LogWarning("Duplicate UID received: {Uid}", uid);
            return null;
        }

        // Validate and create nickname
        if (!Nickname.TryCreate(nickname, out var nick, out var error) || nick == null)
        {
            _logger.LogWarning("Invalid nickname from remote server: {Nickname}, error: {Error}", nickname, error);
            return null;
        }

        // Check for nickname collision
        var existingUser = _userRepository.GetByNickname(nick);
        if (existingUser != null)
        {
            // Nickname collision - will be handled by caller (nick collision resolution)
            _logger.LogDebug("Nickname collision detected for {Nickname}", nickname);
            return null; // Collision handling should be done by UidHandler
        }

        // Create remote user
        var user = new User(
            connectionId,
            IPAddress.None, // Remote users don't have a local IP
            hostname,
            sourceServer.Id,
            userModes.Contains('Z'));

        // Set user info
        user.SetNickname(nick);
        user.SetUserInfo(username, realName);
        user.SetCloakedHostname(virtualHost);
        user.SetRegistrationState(RegistrationState.Registered);

        // Parse and apply user modes
        ApplyUserModes(user, userModes);

        // Set account if authenticated
        if (!string.IsNullOrEmpty(account) && account != "0")
        {
            user.SetAuthenticated(account);
        }

        // Add to repository
        _userRepository.Add(user);

        // Track UID mapping
        _uidToConnectionId[uid] = connectionId;
        _connectionIdToUid[connectionId] = uid;

        // Track on server
        sourceServer.AddUser(connectionId);

        _logger.LogDebug("Introduced remote user {Nickname} ({Uid}) from {Server}",
            nickname, uid, sourceServer.Id.Name);

        return user;
    }

    /// <inheritdoc />
    public void RemoveRemoteUser(string uid)
    {
        if (!_uidToConnectionId.TryRemove(uid, out var connectionId))
        {
            return;
        }

        _connectionIdToUid.TryRemove(connectionId, out _);
        _userRepository.Remove(connectionId);

        // Also remove from all channels
        foreach (var channel in _channelRepository.GetChannelsForUser(connectionId))
        {
            channel.RemoveMember(connectionId);
        }

        _logger.LogDebug("Removed remote user with UID {Uid}", uid);
    }

    /// <inheritdoc />
    public IEnumerable<User> RemoveUsersOnServer(LinkedServer server)
    {
        var removedUsers = new List<User>();
        var usersToRemove = _userRepository.GetByServer(server.Id).ToList();

        foreach (var user in usersToRemove)
        {
            var uid = GetUidForConnection(user.ConnectionId);
            if (uid != null)
            {
                _uidToConnectionId.TryRemove(uid, out _);
            }

            _connectionIdToUid.TryRemove(user.ConnectionId, out _);
            _userRepository.Remove(user.ConnectionId);

            // Remove from all channels
            foreach (var channel in _channelRepository.GetChannelsForUser(user.ConnectionId))
            {
                channel.RemoveMember(user.ConnectionId);
            }

            removedUsers.Add(user);
        }

        _logger.LogInformation("Removed {Count} users from server {ServerName}",
            removedUsers.Count, server.Id.Name);

        return removedUsers;
    }

    /// <inheritdoc />
    public User? GetByUid(string uid)
    {
        if (_uidToConnectionId.TryGetValue(uid, out var connectionId))
        {
            return _userRepository.GetByConnectionId(connectionId);
        }

        return null;
    }

    /// <inheritdoc />
    public User? ResolveUser(string target)
    {
        // Check if it's a UID (9 characters, starts with valid SID format)
        if (target.Length == 9 && char.IsLetterOrDigit(target[0]))
        {
            return GetByUid(target);
        }

        // Otherwise treat as nickname
        if (Nickname.TryCreate(target, out var nick, out _) && nick != null)
        {
            return _userRepository.GetByNickname(nick);
        }

        return null;
    }

    /// <inheritdoc />
    public void ProcessSjoin(
        ChannelName channelName,
        DateTimeOffset channelTimestamp,
        string modes,
        IReadOnlyList<(string Uid, string Prefixes)> members)
    {
        var channel = _channelRepository.GetByName(channelName);

        if (channel == null)
        {
            // Create new channel
            channel = _channelRepository.Create(channelName);
        }
        else
        {
            // Channel exists - compare timestamps for mode/topic resolution
            if (channelTimestamp < channel.CreatedAt)
            {
                // Remote channel is older, accept their modes
                // (Mode application would be more complex in production)
            }
        }

        // Add members
        foreach (var (uid, prefixes) in members)
        {
            var user = GetByUid(uid);
            if (user == null)
            {
                _logger.LogWarning("SJOIN references unknown UID {Uid}", uid);
                continue;
            }

            var memberMode = ParsePrefixes(prefixes);
            channel.AddMember(user, memberMode);
            user.JoinChannel(channelName, memberMode);
        }

        _logger.LogDebug("Processed SJOIN for {Channel} with {Count} members",
            channelName.Value, members.Count);
    }

    /// <inheritdoc />
    public bool UpdateNickname(string uid, string newNickname, DateTimeOffset timestamp)
    {
        var user = GetByUid(uid);
        if (user == null)
        {
            _logger.LogWarning("NICK change for unknown UID {Uid}", uid);
            return false;
        }

        if (!Nickname.TryCreate(newNickname, out var newNick, out var error) || newNick == null)
        {
            _logger.LogWarning("Invalid nickname in NICK change: {Nick}, error: {Error}", newNickname, error);
            return false;
        }

        // Check for collision
        var existingUser = _userRepository.GetByNickname(newNick);
        if (existingUser != null && existingUser.ConnectionId != user.ConnectionId)
        {
            // Nick collision
            return false;
        }

        var oldNick = user.Nickname;
        user.SetNickname(newNick);

        // Update nickname index
        if (_userRepository is IUserRepository repo)
        {
            // Note: InMemoryUserRepository has RegisterNickname method
            // This is a workaround - ideally the interface would support this
        }

        _logger.LogDebug("Remote user {OldNick} changed nick to {NewNick}", oldNick.Value, newNickname);
        return true;
    }

    /// <summary>
    /// Generates a UID for a local user.
    /// </summary>
    private string GenerateUid(Guid connectionId)
    {
        // Check if we already have a UID for this connection
        if (_connectionIdToUid.TryGetValue(connectionId, out var existingUid))
        {
            return existingUid;
        }

        // Generate new UID: SID (3 chars) + 6 alphanumeric chars
        var userPart = GenerateUserIdPart(connectionId);
        var uid = $"{_localServerId.Sid}{userPart}";

        _uidToConnectionId[uid] = connectionId;
        _connectionIdToUid[connectionId] = uid;

        return uid;
    }

    /// <summary>
    /// Generates the 6-character user ID part from a connection ID.
    /// </summary>
    private static string GenerateUserIdPart(Guid connectionId)
    {
        // Use base36 encoding of part of the GUID
        var bytes = connectionId.ToByteArray();
        var value = BitConverter.ToInt64(bytes, 0) & 0x7FFFFFFFFFFFFFFF; // Ensure positive

        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var result = new char[6];

        for (int i = 5; i >= 0; i--)
        {
            result[i] = chars[(int)(value % 36)];
            value /= 36;
        }

        return new string(result);
    }

    /// <summary>
    /// Generates a deterministic connection ID from a UID.
    /// </summary>
    private static Guid GenerateConnectionIdFromUid(string uid)
    {
        // Create a deterministic GUID from the UID
        var bytes = new byte[16];
        var uidBytes = System.Text.Encoding.ASCII.GetBytes(uid.PadRight(16));
        Array.Copy(uidBytes, bytes, Math.Min(16, uidBytes.Length));
        return new Guid(bytes);
    }

    /// <summary>
    /// Gets the UID for a local connection ID.
    /// </summary>
    private string? GetUidForConnection(Guid connectionId)
    {
        if (_connectionIdToUid.TryGetValue(connectionId, out var uid))
        {
            return uid;
        }

        // Check if this is a local user (connected to this server)
        var user = _userRepository.GetByConnectionId(connectionId);
        if (user != null && user.Server.Equals(_localServerId))
        {
            return GenerateUid(connectionId);
        }

        return null;
    }

    /// <summary>
    /// Formats user modes as a mode string.
    /// </summary>
    private static string FormatUserModes(UserMode modes)
    {
        if (modes == UserMode.None)
        {
            return "+";
        }

        var modeChars = new List<char> { '+' };

        if (modes.HasFlag(UserMode.Invisible))
        {
            modeChars.Add('i');
        }

        if (modes.HasFlag(UserMode.Wallops))
        {
            modeChars.Add('w');
        }

        if (modes.HasFlag(UserMode.Operator))
        {
            modeChars.Add('o');
        }

        if (modes.HasFlag(UserMode.Registered))
        {
            modeChars.Add('r');
        }

        if (modes.HasFlag(UserMode.Secure))
        {
            modeChars.Add('Z');
        }

        if (modes.HasFlag(UserMode.Bot))
        {
            modeChars.Add('B');
        }

        return new string(modeChars.ToArray());
    }

    /// <summary>
    /// Applies user modes from a mode string.
    /// </summary>
    private static void ApplyUserModes(User user, string modes)
    {
        foreach (var c in modes)
        {
            if (c == '+')
            {
                continue;
            }

            var mode = UserModeExtensions.FromChar(c);
            if (mode.HasValue)
            {
                user.AddMode(mode.Value);
            }
        }
    }

    /// <summary>
    /// Formats channel modes for SJOIN.
    /// </summary>
    private static string FormatChannelModes(Channel channel)
    {
        var modes = new List<char> { '+' };
        var parameters = new List<string>();

        if (channel.Modes.HasFlag(ChannelMode.NoExternalMessages))
        {
            modes.Add('n');
        }

        if (channel.Modes.HasFlag(ChannelMode.TopicProtected))
        {
            modes.Add('t');
        }

        if (channel.Modes.HasFlag(ChannelMode.InviteOnly))
        {
            modes.Add('i');
        }

        if (channel.Modes.HasFlag(ChannelMode.Moderated))
        {
            modes.Add('m');
        }

        if (channel.Modes.HasFlag(ChannelMode.Secret))
        {
            modes.Add('s');
        }

        if (channel.Key != null)
        {
            modes.Add('k');
            parameters.Add(channel.Key);
        }

        if (channel.UserLimit.HasValue)
        {
            modes.Add('l');
            parameters.Add(channel.UserLimit.Value.ToString(CultureInfo.InvariantCulture));
        }

        var result = new string(modes.ToArray());
        if (parameters.Count > 0)
        {
            result += " " + string.Join(" ", parameters);
        }

        return result;
    }

    /// <summary>
    /// Formats member prefix for SJOIN.
    /// </summary>
    private static string FormatMemberPrefix(ChannelMemberMode mode)
    {
        var prefixes = "";

        if (mode.HasFlag(ChannelMemberMode.Owner))
        {
            prefixes += "~";
        }

        if (mode.HasFlag(ChannelMemberMode.Admin))
        {
            prefixes += "&";
        }

        if (mode.HasFlag(ChannelMemberMode.Op))
        {
            prefixes += "@";
        }

        if (mode.HasFlag(ChannelMemberMode.HalfOp))
        {
            prefixes += "%";
        }

        if (mode.HasFlag(ChannelMemberMode.Voice))
        {
            prefixes += "+";
        }

        return prefixes;
    }

    /// <summary>
    /// Parses member prefixes from SJOIN.
    /// </summary>
    private static ChannelMemberMode ParsePrefixes(string prefixes)
    {
        var mode = ChannelMemberMode.None;

        foreach (var c in prefixes)
        {
            mode |= c switch
            {
                '~' => ChannelMemberMode.Owner,
                '&' => ChannelMemberMode.Admin,
                '@' => ChannelMemberMode.Op,
                '%' => ChannelMemberMode.HalfOp,
                '+' => ChannelMemberMode.Voice,
                _ => ChannelMemberMode.None
            };
        }

        return mode;
    }
}
