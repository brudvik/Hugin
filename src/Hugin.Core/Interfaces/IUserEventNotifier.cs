namespace Hugin.Core.Interfaces;

/// <summary>
/// Interface for broadcasting user events to external systems like SignalR.
/// This abstraction allows the Network layer to emit events without 
/// depending on the Server/API layer.
/// </summary>
public interface IUserEventNotifier
{
    /// <summary>
    /// Notifies about a user connection event.
    /// </summary>
    /// <param name="nickname">The user's nickname.</param>
    /// <param name="hostname">The user's (cloaked) hostname.</param>
    /// <param name="userId">Optional user/connection identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask OnUserConnectedAsync(string nickname, string hostname, string? userId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies about a user disconnection event.
    /// </summary>
    /// <param name="nickname">The user's nickname.</param>
    /// <param name="hostname">The user's (cloaked) hostname.</param>
    /// <param name="reason">The quit reason/message.</param>
    /// <param name="userId">Optional user/connection identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask OnUserDisconnectedAsync(string nickname, string hostname, string? reason = null, string? userId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies about a nickname change.
    /// </summary>
    /// <param name="oldNickname">The previous nickname.</param>
    /// <param name="newNickname">The new nickname.</param>
    /// <param name="hostname">The user's (cloaked) hostname.</param>
    /// <param name="userId">Optional user/connection identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask OnNickChangeAsync(string oldNickname, string newNickname, string hostname, string? userId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies about a user joining a channel.
    /// </summary>
    /// <param name="nickname">The user's nickname.</param>
    /// <param name="channel">The channel name.</param>
    /// <param name="hostname">The user's (cloaked) hostname.</param>
    /// <param name="userId">Optional user/connection identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask OnUserJoinAsync(string nickname, string channel, string hostname, string? userId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies about a user leaving a channel.
    /// </summary>
    /// <param name="nickname">The user's nickname.</param>
    /// <param name="channel">The channel name.</param>
    /// <param name="hostname">The user's (cloaked) hostname.</param>
    /// <param name="reason">Optional part message.</param>
    /// <param name="userId">Optional user/connection identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask OnUserPartAsync(string nickname, string channel, string hostname, string? reason = null, string? userId = null, CancellationToken cancellationToken = default);
}
