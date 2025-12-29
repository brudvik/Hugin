using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;

namespace Hugin.Protocol.Commands;

/// <summary>
/// Represents an ongoing SASL authentication session.
/// </summary>
public sealed class SaslSession
{
    /// <summary>Gets the selected mechanism name.</summary>
    public string Mechanism { get; }

    /// <summary>Gets any accumulated data from chunked AUTHENTICATE messages.</summary>
    public System.Text.StringBuilder AccumulatedData { get; } = new();

    /// <summary>Gets whether authentication has completed.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Gets the authenticated account name if successful.</summary>
    public string? AccountName { get; private set; }

    /// <summary>Creates a new SASL session.</summary>
    /// <param name="mechanism">The SASL mechanism name.</param>
    public SaslSession(string mechanism)
    {
        Mechanism = mechanism;
    }

    /// <summary>Marks the session as successfully authenticated.</summary>
    /// <param name="accountName">The authenticated account name.</param>
    public void SetSuccess(string accountName)
    {
        AccountName = accountName;
        IsComplete = true;
    }

    /// <summary>Marks the session as failed.</summary>
    public void SetFailed()
    {
        IsComplete = true;
    }
}

/// <summary>
/// Context for command execution.
/// </summary>
public sealed class CommandContext
{
    /// <summary>
    /// Gets the parsed IRC message.
    /// </summary>
    public IrcMessage Message { get; }

    /// <summary>
    /// Gets the user executing the command.
    /// </summary>
    public User User { get; }

    /// <summary>
    /// Gets the client connection.
    /// </summary>
    public IClientConnection Connection { get; }

    /// <summary>
    /// Gets the user repository.
    /// </summary>
    public IUserRepository Users { get; }

    /// <summary>
    /// Gets the channel repository.
    /// </summary>
    public IChannelRepository Channels { get; }

    /// <summary>
    /// Gets the message broker.
    /// </summary>
    public IMessageBroker Broker { get; }

    /// <summary>
    /// Gets the capability manager for this client.
    /// </summary>
    public CapabilityManager Capabilities { get; }

    /// <summary>
    /// Gets the server name.
    /// </summary>
    public string ServerName { get; }

    /// <summary>
    /// Gets the server's unique ID.
    /// </summary>
    public ServerId ServerId { get; }

    /// <summary>
    /// Gets a service from the DI container.
    /// </summary>
    public Func<Type, object?> ServiceProvider { get; }

    /// <summary>
    /// Gets or sets the current SASL session.
    /// </summary>
    public SaslSession? SaslSession { get; set; }

    /// <summary>
    /// Creates a new command context.
    /// </summary>
    /// <param name="message">The parsed IRC message.</param>
    /// <param name="user">The user executing the command.</param>
    /// <param name="connection">The client connection.</param>
    /// <param name="users">The user repository.</param>
    /// <param name="channels">The channel repository.</param>
    /// <param name="broker">The message broker.</param>
    /// <param name="capabilities">The capability manager for this client.</param>
    /// <param name="serverName">The server name.</param>
    /// <param name="serverId">The server's unique ID.</param>
    /// <param name="serviceProvider">The service provider for dependency resolution.</param>
    public CommandContext(
        IrcMessage message,
        User user,
        IClientConnection connection,
        IUserRepository users,
        IChannelRepository channels,
        IMessageBroker broker,
        CapabilityManager capabilities,
        string serverName,
        ServerId serverId,
        Func<Type, object?> serviceProvider)
    {
        Message = message;
        User = user;
        Connection = connection;
        Users = users;
        Channels = channels;
        Broker = broker;
        Capabilities = capabilities;
        ServerName = serverName;
        ServerId = serverId;
        ServiceProvider = serviceProvider;
    }

    /// <summary>
    /// Gets the target (first parameter, commonly nick/channel).
    /// </summary>
    public string? Target => Message.Parameters.Count > 0 ? Message.Parameters[0] : null;

    /// <summary>
    /// Gets a typed service from the DI container.
    /// </summary>
    public T? GetService<T>() where T : class
    {
        return ServiceProvider(typeof(T)) as T;
    }

    /// <summary>
    /// Gets a required typed service from the DI container.
    /// </summary>
    public T GetRequiredService<T>() where T : class
    {
        return GetService<T>() ?? throw new InvalidOperationException($"Service {typeof(T).Name} not found");
    }

    /// <summary>
    /// Sends a message to the user.
    /// </summary>
    public ValueTask ReplyAsync(IrcMessage message, CancellationToken cancellationToken = default)
    {
        return Broker.SendToConnectionAsync(Connection.ConnectionId, message.ToString(), cancellationToken);
    }

    /// <summary>
    /// Sends a numeric reply to the user.
    /// </summary>
    public ValueTask ReplyNumericAsync(IrcMessage numeric, CancellationToken cancellationToken = default)
    {
        return ReplyAsync(numeric, cancellationToken);
    }

    /// <summary>
    /// Gets the label tag from the message (for labeled-response).
    /// </summary>
    public string? GetLabel()
    {
        return Message.Tags.TryGetValue("label", out var label) ? label : null;
    }

    /// <summary>
    /// Creates a response with the label tag if present.
    /// </summary>
    public IrcMessage CreateLabeledResponse(IrcMessage response)
    {
        var label = GetLabel();
        if (label is not null && Capabilities.HasLabeledResponse)
        {
            return response.WithTags(new Dictionary<string, string?> { ["label"] = label });
        }
        return response;
    }
}
