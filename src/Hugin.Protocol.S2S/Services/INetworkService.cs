namespace Hugin.Protocol.S2S.Services;

/// <summary>
/// Base interface for IRC network services (NickServ, ChanServ, etc).
/// </summary>
public interface INetworkService
{
    /// <summary>
    /// Gets the service nickname (e.g., "NickServ", "ChanServ").
    /// </summary>
    string Nickname { get; }

    /// <summary>
    /// Gets the service ident/username.
    /// </summary>
    string Ident { get; }

    /// <summary>
    /// Gets the service host (usually "services.network.name").
    /// </summary>
    string Host { get; }

    /// <summary>
    /// Gets the service real name/gecos.
    /// </summary>
    string Realname { get; }

    /// <summary>
    /// Gets the service UID (9 characters: SID + 6 char unique ID).
    /// </summary>
    string Uid { get; }

    /// <summary>
    /// Handles a PRIVMSG directed to this service.
    /// </summary>
    /// <param name="context">The service message context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask HandleMessageAsync(ServiceMessageContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets help text for a command.
    /// </summary>
    /// <param name="command">The command to get help for, or null for general help.</param>
    /// <returns>Help lines to display.</returns>
    IEnumerable<string> GetHelp(string? command = null);
}

/// <summary>
/// Context for a message sent to a network service.
/// </summary>
public sealed class ServiceMessageContext
{
    /// <summary>
    /// Gets the source UID (who sent the message).
    /// </summary>
    public string SourceUid { get; }

    /// <summary>
    /// Gets the source nickname.
    /// </summary>
    public string SourceNick { get; }

    /// <summary>
    /// Gets the source account (if authenticated).
    /// </summary>
    public string? SourceAccount { get; }

    /// <summary>
    /// Gets the raw message text.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the parsed command (first word, uppercased).
    /// </summary>
    public string Command { get; }

    /// <summary>
    /// Gets the command arguments (remaining words).
    /// </summary>
    public string[] Arguments { get; }

    /// <summary>
    /// Gets the services manager for sending responses.
    /// </summary>
    public IServicesManager Services { get; }

    /// <summary>
    /// Gets whether the source user is an IRC operator.
    /// </summary>
    public bool IsOperator { get; }

    /// <summary>
    /// Creates a new service message context.
    /// </summary>
    public ServiceMessageContext(
        string sourceUid,
        string sourceNick,
        string? sourceAccount,
        string message,
        IServicesManager services,
        bool isOperator = false)
    {
        SourceUid = sourceUid;
        SourceNick = sourceNick;
        SourceAccount = sourceAccount;
        Message = message;
        Services = services;
        IsOperator = isOperator;

        var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Command = parts.Length > 0 ? parts[0].ToUpperInvariant() : string.Empty;
        Arguments = parts.Length > 1 ? parts[1..] : Array.Empty<string>();
    }

    /// <summary>
    /// Sends a NOTICE reply to the source user.
    /// </summary>
    public ValueTask ReplyAsync(INetworkService service, string message, CancellationToken cancellationToken = default)
    {
        return Services.SendNoticeAsync(service.Uid, SourceUid, message, cancellationToken);
    }
}
