using System.Globalization;
using Hugin.Core.Entities;
using Hugin.Core.Enums;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Hugin.Protocol.S2S.Commands;

/// <summary>
/// Handles the UID command for user introduction.
/// This is how servers introduce users to each other.
/// </summary>
/// <remarks>
/// Syntax: UID nickname hopcount timestamp username hostname uid servicestamp usermodes virtualhost :realname
/// Example: :001 UID nick 1 1234567890 user host 001AAAAAB 0 +i vhost :Real Name
/// </remarks>
public sealed class UidHandler : S2SCommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "UID";

    /// <inheritdoc />
    public override int MinimumParameters => 9;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(S2SContext context, CancellationToken cancellationToken = default)
    {
        var p = context.Message.Parameters;
        
        var nickname = p[0];
        if (!int.TryParse(p[1], out var hopCount))
        {
            return;
        }
        
        if (!long.TryParse(p[2], out var timestampUnix))
        {
            return;
        }
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(timestampUnix);
        
        var username = p[3];
        var hostname = p[4];
        var uid = p[5];
        var serviceStamp = p[6]; // Account name or 0
        var userModes = p[7];
        var virtualHost = p[8];
        var realName = p.Count > 9 ? p[9] : nickname;

        // Validate UID format (9 characters: 3 char SID + 6 char user ID)
        if (uid.Length != 9)
        {
            return;
        }

        var serverSid = uid[..3];
        var server = context.Links.GetBySid(serverSid);
        if (server == null)
        {
            // Unknown server - this shouldn't happen
            return;
        }

        // Get burst manager
        var burstManager = GetService<IBurstManager>(context);
        var userRepo = GetService<IUserRepository>(context);
        if (burstManager == null || userRepo == null)
        {
            return;
        }

        // Check for nickname collision
        if (Nickname.TryCreate(nickname, out var nick, out _) && nick != null)
        {
            var existingUser = userRepo.GetByNickname(nick);
            if (existingUser != null)
            {
                // Nickname collision - resolve based on timestamp
                // Older timestamp wins; if equal, lower UID (lexicographically) wins
                var resolution = ResolveNickCollision(existingUser, timestamp, uid, context);
                
                if (resolution == NickCollisionResolution.KillRemote)
                {
                    // Our user was first, kill the remote user
                    var killMsg = S2SMessage.CreateWithSource(
                        context.LocalServerId.Sid,
                        "KILL",
                        uid,
                        $"Nickname collision with older client (ours: {existingUser.ConnectedAt.ToUnixTimeSeconds()}, theirs: {timestampUnix})");
                    await context.ReplyAsync(killMsg, cancellationToken);
                    await context.BroadcastAsync(killMsg, cancellationToken);
                    return;
                }
                else if (resolution == NickCollisionResolution.KillLocal)
                {
                    // Remote user was first, kill our local user
                    var killMsg = S2SMessage.CreateWithSource(
                        context.LocalServerId.Sid,
                        "KILL",
                        GetLocalUid(existingUser, burstManager),
                        $"Nickname collision with older client");
                    
                    // Remove local user
                    userRepo.Remove(existingUser.ConnectionId);
                    
                    // Broadcast kill
                    await context.BroadcastAsync(killMsg, cancellationToken);
                }
                else // KillBoth
                {
                    // Same timestamp and can't determine winner - kill both
                    var killRemoteMsg = S2SMessage.CreateWithSource(
                        context.LocalServerId.Sid,
                        "KILL",
                        uid,
                        "Nickname collision (identical timestamp)");
                    await context.ReplyAsync(killRemoteMsg, cancellationToken);
                    await context.BroadcastAsync(killRemoteMsg, cancellationToken);
                    
                    var killLocalMsg = S2SMessage.CreateWithSource(
                        context.LocalServerId.Sid,
                        "KILL",
                        GetLocalUid(existingUser, burstManager),
                        "Nickname collision (identical timestamp)");
                    userRepo.Remove(existingUser.ConnectionId);
                    await context.BroadcastAsync(killLocalMsg, cancellationToken);
                    return;
                }
            }
        }

        // Parse account from service stamp
        var account = serviceStamp != "0" ? serviceStamp : null;

        // Introduce the remote user via BurstManager
        var user = burstManager.IntroduceRemoteUser(
            uid,
            nickname,
            username,
            hostname,
            virtualHost,
            realName,
            timestamp,
            userModes,
            account,
            server);

        if (user == null)
        {
            // User creation failed (shouldn't happen if collision was handled)
            return;
        }

        // Propagate UID to other linked servers
        var uidMsg = S2SMessage.CreateWithSource(
            context.Message.Source ?? serverSid,
            "UID",
            p.ToArray());

        await context.BroadcastAsync(uidMsg, cancellationToken);
    }

    /// <summary>
    /// Resolves a nickname collision between a local user and a remote user.
    /// </summary>
    private static NickCollisionResolution ResolveNickCollision(
        User localUser,
        DateTimeOffset remoteTimestamp,
        string remoteUid,
        S2SContext context)
    {
        // Older timestamp wins
        if (localUser.ConnectedAt < remoteTimestamp)
        {
            return NickCollisionResolution.KillRemote;
        }
        else if (remoteTimestamp < localUser.ConnectedAt)
        {
            return NickCollisionResolution.KillLocal;
        }

        // Same timestamp - use UID comparison (lower UID wins)
        // This requires knowing the local user's UID
        // If we can't determine, kill both to be safe
        return NickCollisionResolution.KillBoth;
    }

    /// <summary>
    /// Gets the UID for a local user.
    /// </summary>
    private static string GetLocalUid(User user, IBurstManager burstManager)
    {
        // Try to get existing UID from burst manager
        // This is a bit of a hack - in production we'd have a proper mapping
        var existingUser = burstManager.ResolveUser(user.Nickname.Value);
        if (existingUser != null && existingUser.ConnectionId == user.ConnectionId)
        {
            // We need to generate/retrieve the UID - this would be tracked by BurstManager
        }
        
        // Fallback: generate UID from connection ID
        return GenerateUidFromConnectionId(user.ConnectionId, "000"); // SID would come from config
    }

    /// <summary>
    /// Generates a UID from a connection ID.
    /// </summary>
    private static string GenerateUidFromConnectionId(Guid connectionId, string sid)
    {
        var bytes = connectionId.ToByteArray();
        var value = BitConverter.ToInt64(bytes, 0) & 0x7FFFFFFFFFFFFFFF;

        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var result = new char[6];

        for (int i = 5; i >= 0; i--)
        {
            result[i] = chars[(int)(value % 36)];
            value /= 36;
        }

        return sid + new string(result);
    }

    private enum NickCollisionResolution
    {
        KillRemote,
        KillLocal,
        KillBoth
    }
}

/// <summary>
/// Handles the QUIT command propagated from other servers.
/// </summary>
public sealed class S2SQuitHandler : S2SCommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "QUIT";

    /// <inheritdoc />
    public override int MinimumParameters => 0;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(S2SContext context, CancellationToken cancellationToken = default)
    {
        var uid = context.Message.Source;
        if (string.IsNullOrEmpty(uid) || uid.Length != 9)
        {
            return;
        }

        var reason = context.Message.Parameters.Count > 0 
            ? context.Message.Parameters[0] 
            : "Client quit";

        // Remove user from local state via BurstManager
        var burstManager = GetService<IBurstManager>(context);
        burstManager?.RemoveRemoteUser(uid);

        // Propagate to other servers
        await context.BroadcastAsync(context.Message, cancellationToken);
    }
}

/// <summary>
/// Handles the KILL command between servers.
/// </summary>
public sealed class S2SKillHandler : S2SCommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "KILL";

    /// <inheritdoc />
    public override int MinimumParameters => 2;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(S2SContext context, CancellationToken cancellationToken = default)
    {
        var targetUid = context.Message.Parameters[0];
        var reason = context.Message.Parameters[1];

        // Remove user from local state via BurstManager
        var burstManager = GetService<IBurstManager>(context);
        burstManager?.RemoveRemoteUser(targetUid);

        // Propagate to other servers
        await context.BroadcastAsync(context.Message, cancellationToken);
    }
}

/// <summary>
/// Handles the NICK command for nickname changes.
/// </summary>
public sealed class S2SNickHandler : S2SCommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "NICK";

    /// <inheritdoc />
    public override int MinimumParameters => 2;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(S2SContext context, CancellationToken cancellationToken = default)
    {
        var uid = context.Message.Source;
        if (string.IsNullOrEmpty(uid) || uid.Length != 9)
        {
            return;
        }

        var newNick = context.Message.Parameters[0];
        if (!long.TryParse(context.Message.Parameters[1], out var timestampUnix))
        {
            timestampUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(timestampUnix);

        // Update nickname via BurstManager
        var burstManager = GetService<IBurstManager>(context);
        if (burstManager != null)
        {
            var success = burstManager.UpdateNickname(uid, newNick, timestamp);
            if (!success)
            {
                // Nick collision during change - KILL the user
                var killMsg = S2SMessage.CreateWithSource(
                    context.LocalServerId.Sid,
                    "KILL",
                    uid,
                    "Nickname collision during nick change");
                await context.ReplyAsync(killMsg, cancellationToken);
                await context.BroadcastAsync(killMsg, cancellationToken);
                return;
            }
        }

        // Propagate to other servers
        await context.BroadcastAsync(context.Message, cancellationToken);
    }
}
