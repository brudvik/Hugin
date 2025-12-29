namespace Hugin.Core.Entities;

/// <summary>
/// Represents a server link configuration stored in the database.
/// </summary>
public sealed class ServerLinkEntity
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the name of the linked server.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the server SID (3 characters).
    /// </summary>
    public string Sid { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the host to connect to.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the port to connect to.
    /// </summary>
    public int Port { get; set; } = 6697;

    /// <summary>
    /// Gets or sets the send password (we send this to them).
    /// </summary>
    public string SendPassword { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the receive password (we expect this from them).
    /// </summary>
    public string ReceivePassword { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this link should auto-connect on startup.
    /// </summary>
    public bool AutoConnect { get; set; } = true;

    /// <summary>
    /// Gets or sets whether TLS should be used for this connection.
    /// </summary>
    public bool UseTls { get; set; } = true;

    /// <summary>
    /// Gets or sets the expected certificate fingerprint (for verification).
    /// </summary>
    public string? CertificateFingerprint { get; set; }

    /// <summary>
    /// Gets or sets when this link was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets when this link was last modified.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets when this link was last successfully connected.
    /// </summary>
    public DateTimeOffset? LastConnectedAt { get; set; }

    /// <summary>
    /// Gets or sets whether this link is currently enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a description or comment for this link.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the server class (hub, leaf, services).
    /// </summary>
    public ServerLinkClass LinkClass { get; set; } = ServerLinkClass.Leaf;
}

/// <summary>
/// Server link class types.
/// </summary>
public enum ServerLinkClass
{
    /// <summary>
    /// A leaf server (end node in the network).
    /// </summary>
    Leaf = 0,

    /// <summary>
    /// A hub server (can have multiple connections).
    /// </summary>
    Hub = 1,

    /// <summary>
    /// A services server (NickServ, ChanServ, etc).
    /// </summary>
    Services = 2
}
