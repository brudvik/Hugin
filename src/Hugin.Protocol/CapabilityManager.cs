namespace Hugin.Protocol;

/// <summary>
/// Defines IRCv3 capability information.
/// </summary>
public sealed class Capability
{
    /// <summary>Gets the capability name (e.g., "multi-prefix", "sasl").</summary>
    public string Name { get; }
    
    /// <summary>Gets the capability value, if any (e.g., "PLAIN,EXTERNAL" for sasl).</summary>
    public string? Value { get; }
    
    /// <summary>Gets whether this capability requires client acknowledgment via CAP ACK.</summary>
    public bool RequiresAck { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Capability"/> class.
    /// </summary>
    /// <param name="name">The capability name.</param>
    /// <param name="value">Optional capability value.</param>
    /// <param name="requiresAck">Whether the capability requires acknowledgment.</param>
    public Capability(string name, string? value = null, bool requiresAck = false)
    {
        Name = name;
        Value = value;
        RequiresAck = requiresAck;
    }

    /// <summary>
    /// Returns the capability in CAP LS format (name or name=value).
    /// </summary>
    public override string ToString() =>
        Value is null ? Name : $"{Name}={Value}";
}

/// <summary>
/// Manages IRCv3 capabilities for a client connection.
/// </summary>
public sealed class CapabilityManager
{
    private readonly HashSet<string> _enabledCapabilities = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingAck = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets whether CAP negotiation is in progress.
    /// </summary>
    public bool IsNegotiating { get; set; }

    /// <summary>
    /// Gets the enabled capabilities.
    /// </summary>
    public IReadOnlySet<string> EnabledCapabilities => _enabledCapabilities;

    /// <summary>
    /// Gets capabilities pending acknowledgment.
    /// </summary>
    public IReadOnlySet<string> PendingAck => _pendingAck;

    /// <summary>
    /// Available capabilities and their values.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Capability> SupportedCapabilities = new Dictionary<string, Capability>(StringComparer.OrdinalIgnoreCase)
    {
        // Core IRCv3.1
        ["multi-prefix"] = new("multi-prefix"),
        ["sasl"] = new("sasl", "PLAIN,EXTERNAL,SCRAM-SHA-256"),
        ["away-notify"] = new("away-notify"),
        ["extended-join"] = new("extended-join"),
        ["account-notify"] = new("account-notify"),

        // IRCv3.2
        ["account-tag"] = new("account-tag"),
        ["cap-notify"] = new("cap-notify"),
        ["chghost"] = new("chghost"),
        ["echo-message"] = new("echo-message"),
        ["invite-notify"] = new("invite-notify"),
        ["labeled-response"] = new("labeled-response"),
        ["message-tags"] = new("message-tags"),
        ["msgid"] = new("msgid"), // Draft but widely supported
        ["server-time"] = new("server-time"),
        ["userhost-in-names"] = new("userhost-in-names"),

        // IRCv3.3 / Modern
        ["batch"] = new("batch"),
        ["setname"] = new("setname"),
        ["standard-replies"] = new("standard-replies"),

        // Draft specifications (important for modern clients)
        ["draft/chathistory"] = new("draft/chathistory"),
        ["draft/event-playback"] = new("draft/event-playback"),
        ["draft/read-marker"] = new("draft/read-marker"),

        // Security
        ["sts"] = new("sts", "port=6697,duration=31536000"), // Will be updated from config
        ["tls"] = new("tls"),

        // Bot identification
        ["bot"] = new("bot"),
    };

    /// <summary>
    /// Enables a capability.
    /// </summary>
    public bool Enable(string capName)
    {
        if (!SupportedCapabilities.ContainsKey(capName))
        {
            return false;
        }

        var cap = SupportedCapabilities[capName];
        if (cap.RequiresAck)
        {
            _pendingAck.Add(capName);
        }
        else
        {
            _enabledCapabilities.Add(capName);
        }
        return true;
    }

    /// <summary>
    /// Acknowledges a pending capability.
    /// </summary>
    public bool Acknowledge(string capName)
    {
        if (_pendingAck.Remove(capName))
        {
            _enabledCapabilities.Add(capName);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Disables a capability.
    /// </summary>
    public bool Disable(string capName)
    {
        return _enabledCapabilities.Remove(capName);
    }

    /// <summary>
    /// Checks if a capability is enabled.
    /// </summary>
    public bool IsEnabled(string capName)
    {
        return _enabledCapabilities.Contains(capName);
    }

    /// <summary>
    /// Checks if a capability is supported.
    /// </summary>
    public static bool IsSupported(string capName)
    {
        return SupportedCapabilities.ContainsKey(capName);
    }

    /// <summary>
    /// Gets the capability list string for CAP LS.
    /// </summary>
    public static string GetCapabilityList(bool includeValues = true)
    {
        if (includeValues)
        {
            return string.Join(" ", SupportedCapabilities.Values.Select(c => c.ToString()));
        }
        return string.Join(" ", SupportedCapabilities.Keys);
    }

    /// <summary>
    /// Clears all enabled capabilities.
    /// </summary>
    public void Clear()
    {
        _enabledCapabilities.Clear();
        _pendingAck.Clear();
    }

    // Convenience properties for common capability checks

    /// <summary>Gets whether multi-prefix capability is enabled.</summary>
    public bool HasMultiPrefix => IsEnabled("multi-prefix");
    
    /// <summary>Gets whether away-notify capability is enabled.</summary>
    public bool HasAwayNotify => IsEnabled("away-notify");
    
    /// <summary>Gets whether extended-join capability is enabled.</summary>
    public bool HasExtendedJoin => IsEnabled("extended-join");
    
    /// <summary>Gets whether account-notify capability is enabled.</summary>
    public bool HasAccountNotify => IsEnabled("account-notify");
    
    /// <summary>Gets whether account-tag capability is enabled.</summary>
    public bool HasAccountTag => IsEnabled("account-tag");
    
    /// <summary>Gets whether server-time capability is enabled.</summary>
    public bool HasServerTime => IsEnabled("server-time");
    
    /// <summary>Gets whether message-tags capability is enabled.</summary>
    public bool HasMessageTags => IsEnabled("message-tags");
    
    /// <summary>Gets whether labeled-response capability is enabled.</summary>
    public bool HasLabeledResponse => IsEnabled("labeled-response");
    
    /// <summary>Gets whether batch capability is enabled.</summary>
    public bool HasBatch => IsEnabled("batch");
    
    /// <summary>Gets whether echo-message capability is enabled.</summary>
    public bool HasEchoMessage => IsEnabled("echo-message");
    
    /// <summary>Gets whether userhost-in-names capability is enabled.</summary>
    public bool HasUserhostInNames => IsEnabled("userhost-in-names");
    
    /// <summary>Gets whether draft/chathistory capability is enabled.</summary>
    public bool HasChatHistory => IsEnabled("draft/chathistory");

    /// <summary>Gets whether sasl capability is enabled.</summary>
    public bool HasSasl => IsEnabled("sasl");

    /// <summary>Gets whether invite-notify capability is enabled.</summary>
    public bool HasInviteNotify => IsEnabled("invite-notify");

    /// <summary>Gets whether chghost capability is enabled.</summary>
    public bool HasChghost => IsEnabled("chghost");

    /// <summary>Gets whether setname capability is enabled.</summary>
    public bool HasSetname => IsEnabled("setname");
}
