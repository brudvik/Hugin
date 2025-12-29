using System.Text;
using Hugin.Core.Entities;
using Hugin.Core.Enums;
using Hugin.Core.ValueObjects;

namespace Hugin.Protocol.S2S.Commands;

/// <summary>
/// Handles the SJOIN command for synchronized channel joins.
/// This is how servers share channel membership and modes.
/// </summary>
/// <remarks>
/// Syntax: SJOIN timestamp channel +modes [params] :[@+]uid [@+]uid ...
/// Example: :001 SJOIN 1234567890 #channel +nt :@001AAAAAB +001AAAAAC 001AAAAAD
/// </remarks>
public sealed class SjoinHandler : S2SCommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "SJOIN";

    /// <inheritdoc />
    public override int MinimumParameters => 4;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(S2SContext context, CancellationToken cancellationToken = default)
    {
        var p = context.Message.Parameters;
        
        if (!long.TryParse(p[0], out var timestampUnix))
        {
            return;
        }
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(timestampUnix);
        
        var channelName = p[1];
        if (!ChannelName.TryCreate(channelName, out var channel, out _) || channel == null)
        {
            return;
        }

        var modes = p[2];
        
        // Find the user list (last parameter, prefixed with colons in the raw message)
        var userList = p[^1];
        var users = ParseUserList(userList);

        // Mode parameters are between modes and user list
        var modeParams = new List<string>();
        for (var i = 3; i < p.Count - 1; i++)
        {
            modeParams.Add(p[i]);
        }

        // Process locally
        // This would be handled by the channel manager in the main server

        // Propagate to other servers
        await context.BroadcastAsync(context.Message, cancellationToken);
    }

    /// <summary>
    /// Parses the user list from an SJOIN command.
    /// </summary>
    /// <param name="userList">The space-separated user list with optional prefixes.</param>
    /// <returns>A list of tuples containing (uid, status prefix).</returns>
    private static List<(string Uid, string Prefix)> ParseUserList(string userList)
    {
        var result = new List<(string, string)>();
        var entries = userList.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in entries)
        {
            var prefix = "";
            var uid = entry;

            // Extract status prefixes (@, +, %, etc.)
            var prefixChars = new StringBuilder();
            var i = 0;
            while (i < entry.Length && !char.IsLetterOrDigit(entry[i]))
            {
                prefixChars.Append(entry[i]);
                i++;
            }

            if (i > 0)
            {
                prefix = prefixChars.ToString();
                uid = entry[i..];
            }

            if (uid.Length == 9) // Valid UID length
            {
                result.Add((uid, prefix));
            }
        }

        return result;
    }
}

/// <summary>
/// Handles the PART command propagated from other servers.
/// </summary>
public sealed class S2SPartHandler : S2SCommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "PART";

    /// <inheritdoc />
    public override int MinimumParameters => 1;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(S2SContext context, CancellationToken cancellationToken = default)
    {
        var uid = context.Message.Source;
        if (string.IsNullOrEmpty(uid) || uid.Length != 9)
        {
            return;
        }

        var channelName = context.Message.Parameters[0];
        var reason = context.Message.Parameters.Count > 1 
            ? context.Message.Parameters[1] 
            : null;

        // Propagate to other servers
        await context.BroadcastAsync(context.Message, cancellationToken);

        // Handle locally
        // This would be handled by the channel manager in the main server
    }
}

/// <summary>
/// Handles the KICK command propagated from other servers.
/// </summary>
public sealed class S2SKickHandler : S2SCommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "KICK";

    /// <inheritdoc />
    public override int MinimumParameters => 2;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(S2SContext context, CancellationToken cancellationToken = default)
    {
        var sourceUid = context.Message.Source;
        if (string.IsNullOrEmpty(sourceUid))
        {
            return;
        }

        var channelName = context.Message.Parameters[0];
        var targetUid = context.Message.Parameters[1];
        var reason = context.Message.Parameters.Count > 2 
            ? context.Message.Parameters[2] 
            : sourceUid;

        // Propagate to other servers
        await context.BroadcastAsync(context.Message, cancellationToken);

        // Handle locally
        // This would be handled by the channel manager in the main server
    }
}

/// <summary>
/// Handles the MODE command for channel modes propagated from other servers.
/// </summary>
public sealed class S2SModeHandler : S2SCommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "TMODE";

    /// <inheritdoc />
    public override int MinimumParameters => 3;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(S2SContext context, CancellationToken cancellationToken = default)
    {
        var p = context.Message.Parameters;
        
        if (!long.TryParse(p[0], out var timestampUnix))
        {
            return;
        }
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(timestampUnix);
        
        var channelName = p[1];
        var modes = p[2];
        
        // Mode parameters follow
        var modeParams = new List<string>();
        for (var i = 3; i < p.Count; i++)
        {
            modeParams.Add(p[i]);
        }

        // Propagate to other servers
        await context.BroadcastAsync(context.Message, cancellationToken);

        // Handle locally
        // This would be handled by the channel manager in the main server
    }
}

/// <summary>
/// Handles the TOPIC command propagated from other servers.
/// </summary>
public sealed class S2STopicHandler : S2SCommandHandlerBase
{
    /// <inheritdoc />
    public override string Command => "TOPIC";

    /// <inheritdoc />
    public override int MinimumParameters => 4;

    /// <inheritdoc />
    public override async ValueTask HandleAsync(S2SContext context, CancellationToken cancellationToken = default)
    {
        var p = context.Message.Parameters;
        
        var channelName = p[0];
        var setter = p[1];
        
        if (!long.TryParse(p[2], out var timestampUnix))
        {
            return;
        }
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(timestampUnix);
        
        var topic = p[3];

        // Propagate to other servers
        await context.BroadcastAsync(context.Message, cancellationToken);

        // Handle locally
        // This would be handled by the channel manager in the main server
    }
}
