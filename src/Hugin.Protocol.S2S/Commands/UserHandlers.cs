using System.Globalization;
using Hugin.Core.Entities;
using Hugin.Core.Enums;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;

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
        // p[6] is servicestamp (unused for now)
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

        // Get user repository
        var userRepo = GetService<IUserRepository>(context);
        if (userRepo == null)
        {
            return;
        }

        // Check for nickname collision
        if (Nickname.TryCreate(nickname, out var nick, out _) && nick != null)
        {
            var existingUser = userRepo.GetByNickname(nick);
            if (existingUser != null)
            {
                // Nickname collision - need to handle based on timestamp
                // Older timestamp wins; if equal, we lose (we're the receiving end)
                if (existingUser.ConnectedAt <= timestamp)
                {
                    // Our user was first, kill the remote user
                    var killMsg = S2SMessage.CreateWithSource(
                        context.LocalServerId.Sid,
                        "KILL",
                        uid,
                        $"Nickname collision with older client");
                    await context.ReplyAsync(killMsg, cancellationToken);
                    return;
                }
                else
                {
                    // Remote user was first, kill our local user
                    // This would be handled by the main server code
                }
            }
        }

        // Create a remote user representation
        // Remote users have a special connection ID format
        var remoteConnectionId = GenerateRemoteConnectionId(uid);
        var serverId = server.Id;

        // Note: Remote users would be stored differently than local users
        // For now, we just broadcast the UID to other links
        var uidMsg = S2SMessage.CreateWithSource(
            context.Message.Source ?? serverSid,
            "UID",
            p.ToArray());

        await context.BroadcastAsync(uidMsg, cancellationToken);
    }

    private static Guid GenerateRemoteConnectionId(string uid)
    {
        // Generate a deterministic GUID from the UID
        var bytes = new byte[16];
        var uidBytes = System.Text.Encoding.ASCII.GetBytes(uid.PadRight(16));
        Array.Copy(uidBytes, bytes, Math.Min(16, uidBytes.Length));
        return new Guid(bytes);
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

        // Propagate to other servers
        await context.BroadcastAsync(context.Message, cancellationToken);

        // Remove user from local state
        // This would be handled by the main server code
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

        // Propagate to other servers
        await context.BroadcastAsync(context.Message, cancellationToken);

        // Handle locally if the user is on this server
        // This would be handled by the main server code
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
        if (string.IsNullOrEmpty(uid))
        {
            return;
        }

        var newNick = context.Message.Parameters[0];
        var timestamp = context.Message.Parameters.Count > 1 
            ? context.Message.Parameters[1] 
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        // Propagate to other servers
        await context.BroadcastAsync(context.Message, cancellationToken);

        // Update local user state
        // This would be handled by the main server code
    }
}
