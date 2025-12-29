using Hugin.Core.Entities;
using Hugin.Core.ValueObjects;

namespace Hugin.Protocol.Commands.Handlers;

/// <summary>
/// Handles the MONITOR command for tracking user online status.
/// IRCv3 specification: https://ircv3.net/specs/extensions/monitor
/// </summary>
public sealed class MonitorHandler : CommandHandlerBase
{
    public override string Command => "MONITOR";
    public override int MinimumParameters => 1;

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var subcommand = context.Message.Parameters[0].ToUpperInvariant();

        switch (subcommand)
        {
            case "+":
                await HandleAddAsync(context, cancellationToken);
                break;
            case "-":
                await HandleRemoveAsync(context, cancellationToken);
                break;
            case "C":
                HandleClear(context);
                break;
            case "L":
                await HandleListAsync(context, cancellationToken);
                break;
            case "S":
                await HandleStatusAsync(context, cancellationToken);
                break;
            default:
                // Unknown subcommand - silently ignore per spec
                break;
        }
    }

    /// <summary>
    /// MONITOR + target[,target2,...] - Add targets to monitor list.
    /// </summary>
    private static async ValueTask HandleAddAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Message.Parameters.Count < 2)
        {
            await SendNeedMoreParamsAsync(context, cancellationToken);
            return;
        }

        var targets = context.Message.Parameters[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
        var nick = context.User.Nickname.Value;
        var server = context.ServerName;

        // Add to monitor list
        var overflow = context.User.AddToMonitorList(targets);

        // Report overflow if any
        if (overflow.Count > 0)
        {
            await context.ReplyAsync(
                IrcNumerics.MonListFull(server, nick, User.MaxMonitorListSize, string.Join(",", overflow)),
                cancellationToken);
        }

        // Check status of newly added targets
        var online = new List<string>();
        var offline = new List<string>();

        foreach (var target in targets.Except(overflow))
        {
            if (Nickname.TryCreate(target, out var targetNickname, out _))
            {
                var targetUser = context.Users.GetByNickname(targetNickname);
                if (targetUser is not null)
                {
                    online.Add($"{targetUser.Nickname.Value}!{targetUser.Username}@{targetUser.DisplayedHostname}");
                }
                else
                {
                    offline.Add(target);
                }
            }
            else
            {
                offline.Add(target);
            }
        }

        // Send online notifications
        if (online.Count > 0)
        {
            await context.ReplyAsync(
                IrcNumerics.MonOnline(server, nick, string.Join(",", online)),
                cancellationToken);
        }

        // Send offline notifications
        if (offline.Count > 0)
        {
            await context.ReplyAsync(
                IrcNumerics.MonOffline(server, nick, string.Join(",", offline)),
                cancellationToken);
        }
    }

    /// <summary>
    /// MONITOR - target[,target2,...] - Remove targets from monitor list.
    /// </summary>
    private static async ValueTask HandleRemoveAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Message.Parameters.Count < 2)
        {
            await SendNeedMoreParamsAsync(context, cancellationToken);
            return;
        }

        var targets = context.Message.Parameters[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
        context.User.RemoveFromMonitorList(targets);
    }

    /// <summary>
    /// MONITOR C - Clear entire monitor list.
    /// </summary>
    private static void HandleClear(CommandContext context)
    {
        context.User.ClearMonitorList();
    }

    /// <summary>
    /// MONITOR L - List all monitored nicknames.
    /// </summary>
    private static async ValueTask HandleListAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var nick = context.User.Nickname.Value;
        var server = context.ServerName;
        var monitorList = context.User.MonitorList;

        if (monitorList.Count > 0)
        {
            // Send in batches to avoid overly long lines
            const int batchSize = 20;
            var items = monitorList.ToList();

            for (var i = 0; i < items.Count; i += batchSize)
            {
                var batch = items.Skip(i).Take(batchSize);
                await context.ReplyAsync(
                    IrcNumerics.MonList(server, nick, string.Join(",", batch)),
                    cancellationToken);
            }
        }

        await context.ReplyAsync(
            IrcNumerics.EndOfMonList(server, nick),
            cancellationToken);
    }

    /// <summary>
    /// MONITOR S - Get current status of all monitored nicknames.
    /// </summary>
    private static async ValueTask HandleStatusAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var nick = context.User.Nickname.Value;
        var server = context.ServerName;
        var monitorList = context.User.MonitorList;

        var online = new List<string>();
        var offline = new List<string>();

        foreach (var target in monitorList)
        {
            if (Nickname.TryCreate(target, out var targetNickname, out _))
            {
                var targetUser = context.Users.GetByNickname(targetNickname);
                if (targetUser is not null)
                {
                    online.Add($"{targetUser.Nickname.Value}!{targetUser.Username}@{targetUser.DisplayedHostname}");
                }
                else
                {
                    offline.Add(target);
                }
            }
            else
            {
                offline.Add(target);
            }
        }

        // Send online notifications
        if (online.Count > 0)
        {
            await context.ReplyAsync(
                IrcNumerics.MonOnline(server, nick, string.Join(",", online)),
                cancellationToken);
        }

        // Send offline notifications
        if (offline.Count > 0)
        {
            await context.ReplyAsync(
                IrcNumerics.MonOffline(server, nick, string.Join(",", offline)),
                cancellationToken);
        }
    }
}
