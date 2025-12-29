using Hugin.Core.ValueObjects;

namespace Hugin.Core.Entities;

/// <summary>
/// Represents a linked IRC server (for S2S).
/// </summary>
public sealed class LinkedServer
{
    /// <summary>
    /// Gets the server identifier.
    /// </summary>
    public ServerId Id { get; }

    /// <summary>
    /// Gets the server description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the server version string.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Gets when the server was linked.
    /// </summary>
    public DateTimeOffset LinkedAt { get; }

    /// <summary>
    /// Gets the hop count (distance from this server).
    /// </summary>
    public int HopCount { get; }

    /// <summary>
    /// Gets the server we learned about this server from (null if directly connected).
    /// </summary>
    public ServerId? LearnedFrom { get; }

    /// <summary>
    /// Gets whether this is a directly connected server.
    /// </summary>
    public bool IsDirect => LearnedFrom is null;

    /// <summary>
    /// Gets the users on this server.
    /// </summary>
    public IReadOnlySet<Guid> Users => _users;
    private readonly HashSet<Guid> _users = new();

    /// <summary>
    /// Creates a new linked server instance.
    /// </summary>
    /// <param name="id">The server identifier.</param>
    /// <param name="description">The server description.</param>
    /// <param name="version">The server version string.</param>
    /// <param name="hopCount">The hop count (distance from this server).</param>
    /// <param name="learnedFrom">The server we learned about this server from.</param>
    public LinkedServer(
        ServerId id,
        string description,
        string version,
        int hopCount,
        ServerId? learnedFrom = null)
    {
        Id = id;
        Description = description;
        Version = version;
        HopCount = hopCount;
        LearnedFrom = learnedFrom;
        LinkedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Adds a user to this server.
    /// </summary>
    public void AddUser(Guid connectionId)
    {
        _users.Add(connectionId);
    }

    /// <summary>
    /// Removes a user from this server.
    /// </summary>
    public void RemoveUser(Guid connectionId)
    {
        _users.Remove(connectionId);
    }
}
