using Hugin.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Hugin.Protocol.S2S;

/// <summary>
/// Manages the server-to-server handshake process.
/// </summary>
/// <remarks>
/// The S2S handshake follows this sequence:
/// 1. Both sides send PASS (password) with TS version and flags
/// 2. Both sides send CAPAB (capabilities)
/// 3. Both sides send SERVER (server introduction)
/// 4. After successful handshake, BURST begins to sync state
/// </remarks>
public interface IS2SHandshakeManager
{
    /// <summary>
    /// Initiates an outgoing connection handshake.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="password">The link password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask InitiateHandshakeAsync(Guid connectionId, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes an incoming handshake message.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="message">The S2S message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if handshake is complete, false if more messages expected.</returns>
    ValueTask<HandshakeResult> ProcessHandshakeMessageAsync(Guid connectionId, S2SMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a connection is in handshake state.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <returns>True if handshake is in progress.</returns>
    bool IsHandshaking(Guid connectionId);

    /// <summary>
    /// Gets the handshake state for a connection.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <returns>The handshake state or null if not found.</returns>
    HandshakeState? GetState(Guid connectionId);

    /// <summary>
    /// Removes handshake state for a connection.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    void RemoveState(Guid connectionId);
}

/// <summary>
/// Result of processing a handshake message.
/// </summary>
public enum HandshakeResult
{
    /// <summary>
    /// Handshake is still in progress.
    /// </summary>
    InProgress,

    /// <summary>
    /// Handshake completed successfully.
    /// </summary>
    Complete,

    /// <summary>
    /// Handshake failed due to an error.
    /// </summary>
    Failed
}

/// <summary>
/// Tracks the state of an ongoing handshake.
/// </summary>
public sealed class HandshakeState
{
    /// <summary>
    /// Gets the connection identifier.
    /// </summary>
    public Guid ConnectionId { get; }

    /// <summary>
    /// Gets whether this is an outgoing connection.
    /// </summary>
    public bool IsOutgoing { get; }

    /// <summary>
    /// Gets or sets whether PASS has been received.
    /// </summary>
    public bool PassReceived { get; set; }

    /// <summary>
    /// Gets or sets whether CAPAB has been received.
    /// </summary>
    public bool CapabReceived { get; set; }

    /// <summary>
    /// Gets or sets whether SERVER has been received.
    /// </summary>
    public bool ServerReceived { get; set; }

    /// <summary>
    /// Gets or sets whether we have sent our PASS.
    /// </summary>
    public bool PassSent { get; set; }

    /// <summary>
    /// Gets or sets whether we have sent our CAPAB.
    /// </summary>
    public bool CapabSent { get; set; }

    /// <summary>
    /// Gets or sets whether we have sent our SERVER.
    /// </summary>
    public bool ServerSent { get; set; }

    /// <summary>
    /// Gets or sets the received password.
    /// </summary>
    public string? ReceivedPassword { get; set; }

    /// <summary>
    /// Gets or sets the expected password.
    /// </summary>
    public string? ExpectedPassword { get; set; }

    /// <summary>
    /// Gets the received capabilities.
    /// </summary>
    public HashSet<string> ReceivedCapabilities { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the remote server SID.
    /// </summary>
    public string? RemoteSid { get; set; }

    /// <summary>
    /// Gets or sets the remote server name.
    /// </summary>
    public string? RemoteServerName { get; set; }

    /// <summary>
    /// Gets or sets the remote server description.
    /// </summary>
    public string? RemoteDescription { get; set; }

    /// <summary>
    /// Gets or sets the error message if handshake failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Creates a new handshake state.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="isOutgoing">Whether this is an outgoing connection.</param>
    public HandshakeState(Guid connectionId, bool isOutgoing)
    {
        ConnectionId = connectionId;
        IsOutgoing = isOutgoing;
    }

    /// <summary>
    /// Checks if the handshake is complete from our perspective.
    /// </summary>
    public bool IsComplete => PassReceived && CapabReceived && ServerReceived && 
                               PassSent && CapabSent && ServerSent;
}

/// <summary>
/// Capabilities supported by the S2S protocol.
/// </summary>
public static class S2SCapabilities
{
    /// <summary>
    /// Encapsulated messages.
    /// </summary>
    public const string Encap = "ENCAP";

    /// <summary>
    /// Server-time message tags.
    /// </summary>
    public const string ServerTime = "SVS-TIME";

    /// <summary>
    /// Extended bans.
    /// </summary>
    public const string ExtendedBans = "EBAN";

    /// <summary>
    /// Channel history.
    /// </summary>
    public const string ChannelHistory = "CHW";

    /// <summary>
    /// TLS/SSL support.
    /// </summary>
    public const string Tls = "TLS";

    /// <summary>
    /// Knock support.
    /// </summary>
    public const string Knock = "KNOCK";

    /// <summary>
    /// Services support.
    /// </summary>
    public const string Services = "SERVICES";

    /// <summary>
    /// Gets all default capabilities.
    /// </summary>
    public static IReadOnlyList<string> Defaults { get; } = new[]
    {
        Encap,
        ServerTime,
        Tls
    };
}
