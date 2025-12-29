namespace Hugin.Protocol.Commands;

/// <summary>
/// Base class for command handlers providing common functionality.
/// </summary>
public abstract class CommandHandlerBase : ICommandHandler
{
    public abstract string Command { get; }
    public virtual int MinimumParameters => 0;
    public virtual bool RequiresRegistration => true;
    public virtual bool RequiresOperator => false;

    public abstract ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an error for not enough parameters.
    /// </summary>
    protected static ValueTask SendNeedMoreParamsAsync(CommandContext ctx, CancellationToken ct = default)
    {
        return ctx.ReplyAsync(
            IrcNumerics.NeedMoreParams(ctx.ServerName, ctx.User.Nickname.Value, ctx.Message.Command),
            ct);
    }

    /// <summary>
    /// Sends a no such nick/channel error.
    /// </summary>
    protected static ValueTask SendNoSuchNickAsync(CommandContext ctx, string target, CancellationToken ct = default)
    {
        return ctx.ReplyAsync(
            IrcNumerics.NoSuchNick(ctx.ServerName, ctx.User.Nickname.Value, target),
            ct);
    }

    /// <summary>
    /// Sends a no such channel error.
    /// </summary>
    protected static ValueTask SendNoSuchChannelAsync(CommandContext ctx, string channel, CancellationToken ct = default)
    {
        return ctx.ReplyAsync(
            IrcNumerics.NoSuchChannel(ctx.ServerName, ctx.User.Nickname.Value, channel),
            ct);
    }

    /// <summary>
    /// Sends a not on channel error.
    /// </summary>
    protected static ValueTask SendNotOnChannelAsync(CommandContext ctx, string channel, CancellationToken ct = default)
    {
        return ctx.ReplyAsync(
            IrcNumerics.NotOnChannel(ctx.ServerName, ctx.User.Nickname.Value, channel),
            ct);
    }

    /// <summary>
    /// Sends a chanop privs needed error.
    /// </summary>
    protected static ValueTask SendChanOpPrivsNeededAsync(CommandContext ctx, string channel, CancellationToken ct = default)
    {
        return ctx.ReplyAsync(
            IrcNumerics.ChanOpPrivsNeeded(ctx.ServerName, ctx.User.Nickname.Value, channel),
            ct);
    }

    /// <summary>
    /// Sends a no privileges error.
    /// </summary>
    protected static ValueTask SendNoPrivilegesAsync(CommandContext ctx, CancellationToken ct = default)
    {
        return ctx.ReplyAsync(
            IrcNumerics.NoPrivileges(ctx.ServerName, ctx.User.Nickname.Value),
            ct);
    }

    /// <summary>
    /// Adds server-time tag to a message if the client supports it.
    /// </summary>
    protected static IrcMessage AddServerTime(IrcMessage message, CapabilityManager caps)
    {
        if (caps.HasServerTime)
        {
            var tags = new Dictionary<string, string?>(message.Tags)
            {
                ["time"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture)
            };
            return IrcMessage.CreateFull(tags, message.Source, message.Command, message.Parameters.ToArray());
        }
        return message;
    }
}
