using System.Net;
using Hugin.Core.Enums;
using Hugin.Core.ValueObjects;

namespace Hugin.Core.Entities;

/// <summary>
/// Represents a connected IRC user/client.
/// </summary>
public sealed class User
{
    /// <summary>
    /// Gets the unique connection identifier.
    /// </summary>
    public Guid ConnectionId { get; }

    /// <summary>
    /// Gets or sets the user's nickname.
    /// </summary>
    public Nickname Nickname { get; set; }

    /// <summary>
    /// Gets the username (ident).
    /// </summary>
    public string Username { get; private set; }

    /// <summary>
    /// Gets the real name (gecos).
    /// </summary>
    public string RealName { get; private set; }

    /// <summary>
    /// Gets the user's real hostname.
    /// </summary>
    public string Hostname { get; private set; }

    /// <summary>
    /// Gets the user's displayed (possibly cloaked) hostname.
    /// </summary>
    public string DisplayedHostname { get; private set; }

    /// <summary>
    /// Gets the user's IP address.
    /// </summary>
    public IPAddress IpAddress { get; private set; }

    /// <summary>
    /// Gets whether WEBIRC has been applied for this connection.
    /// </summary>
    public bool HasWebircApplied { get; private set; }

    /// <summary>
    /// Gets the server the user is connected to.
    /// </summary>
    public ServerId Server { get; }

    /// <summary>
    /// Gets the user's current modes.
    /// </summary>
    public UserMode Modes { get; private set; }

    /// <summary>
    /// Gets the registration state.
    /// </summary>
    public RegistrationState RegistrationState { get; private set; }

    /// <summary>
    /// Gets the connection timestamp.
    /// </summary>
    public DateTimeOffset ConnectedAt { get; }

    /// <summary>
    /// Gets the time of last activity.
    /// </summary>
    public DateTimeOffset LastActivity { get; private set; }

    /// <summary>
    /// Gets or sets the away message (null if not away).
    /// </summary>
    public string? AwayMessage { get; private set; }

    /// <summary>
    /// Gets the account name if authenticated (null if not).
    /// </summary>
    public string? Account { get; private set; }

    /// <summary>
    /// Gets the channels the user is currently in.
    /// </summary>
    public IReadOnlyDictionary<ChannelName, ChannelMemberMode> Channels => _channels;
    private readonly Dictionary<ChannelName, ChannelMemberMode> _channels = new();

    /// <summary>
    /// Gets the nicknames this user is monitoring (MONITOR list).
    /// </summary>
    public IReadOnlySet<string> MonitorList => _monitorList;
    private readonly HashSet<string> _monitorList = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maximum number of entries in the monitor list.
    /// </summary>
    public const int MaxMonitorListSize = 100;

    /// <summary>
    /// Gets whether the user is fully registered.
    /// </summary>
    public bool IsRegistered => RegistrationState == RegistrationState.Registered;

    /// <summary>
    /// Gets whether the user is an IRC operator.
    /// </summary>
    public bool IsOperator => Modes.HasFlag(UserMode.Operator);

    /// <summary>
    /// Gets whether the user is using a secure connection.
    /// </summary>
    public bool IsSecure => Modes.HasFlag(UserMode.Secure);

    /// <summary>
    /// Gets whether the user is away.
    /// </summary>
    public bool IsAway => !string.IsNullOrEmpty(AwayMessage);

    /// <summary>
    /// Gets the user's hostmask.
    /// </summary>
    public Hostmask Hostmask => Hostmask.Create(Nickname.Value, Username, DisplayedHostname);

    /// <summary>
    /// Creates a new User.
    /// </summary>
    public User(
        Guid connectionId,
        IPAddress ipAddress,
        string hostname,
        ServerId server,
        bool isSecure)
    {
        ConnectionId = connectionId;
        IpAddress = ipAddress;
        Hostname = hostname;
        DisplayedHostname = hostname;
        Server = server;
        ConnectedAt = DateTimeOffset.UtcNow;
        LastActivity = DateTimeOffset.UtcNow;
        RegistrationState = RegistrationState.None;
        Nickname = null!; // Set during registration
        Username = string.Empty;
        RealName = string.Empty;

        if (isSecure)
        {
            Modes |= UserMode.Secure;
        }
    }

    /// <summary>
    /// Sets the nickname during registration.
    /// </summary>
    public void SetNickname(Nickname nickname)
    {
        Nickname = nickname;
        UpdateLastActivity();
    }

    /// <summary>
    /// Sets the username and realname during registration.
    /// </summary>
    public void SetUserInfo(string username, string realName)
    {
        Username = username;
        RealName = realName;
        UpdateLastActivity();
    }

    /// <summary>
    /// Updates the registration state.
    /// </summary>
    public void SetRegistrationState(RegistrationState state)
    {
        RegistrationState = state;
    }

    /// <summary>
    /// Sets the user as authenticated with an account.
    /// </summary>
    public void SetAuthenticated(string accountName)
    {
        Account = accountName;
        Modes |= UserMode.Registered;
    }

    /// <summary>
    /// Clears authentication.
    /// </summary>
    public void ClearAuthentication()
    {
        Account = null;
        Modes &= ~UserMode.Registered;
    }

    /// <summary>
    /// Adds or updates a mode.
    /// </summary>
    public void AddMode(UserMode mode)
    {
        Modes |= mode;
    }

    /// <summary>
    /// Removes a mode.
    /// </summary>
    public void RemoveMode(UserMode mode)
    {
        Modes &= ~mode;
    }

    /// <summary>
    /// Sets the away message.
    /// </summary>
    public void SetAway(string message)
    {
        AwayMessage = message;
        Modes |= UserMode.Away;
    }

    /// <summary>
    /// Clears the away status.
    /// </summary>
    public void SetBack()
    {
        AwayMessage = null;
        Modes &= ~UserMode.Away;
    }

    /// <summary>
    /// Sets a cloaked hostname.
    /// </summary>
    public void SetCloakedHostname(string cloakedHostname)
    {
        DisplayedHostname = cloakedHostname;
    }

    /// <summary>
    /// Joins a channel.
    /// </summary>
    public void JoinChannel(ChannelName channelName, ChannelMemberMode mode = ChannelMemberMode.None)
    {
        _channels[channelName] = mode;
        UpdateLastActivity();
    }

    /// <summary>
    /// Parts a channel.
    /// </summary>
    public void PartChannel(ChannelName channelName)
    {
        _channels.Remove(channelName);
        UpdateLastActivity();
    }

    /// <summary>
    /// Updates the user's mode in a channel.
    /// </summary>
    public void SetChannelMode(ChannelName channelName, ChannelMemberMode mode)
    {
        if (_channels.ContainsKey(channelName))
        {
            _channels[channelName] = mode;
        }
    }

    /// <summary>
    /// Adds a mode in a channel.
    /// </summary>
    public void AddChannelMode(ChannelName channelName, ChannelMemberMode mode)
    {
        if (_channels.TryGetValue(channelName, out var currentMode))
        {
            _channels[channelName] = currentMode | mode;
        }
    }

    /// <summary>
    /// Removes a mode in a channel.
    /// </summary>
    public void RemoveChannelMode(ChannelName channelName, ChannelMemberMode mode)
    {
        if (_channels.TryGetValue(channelName, out var currentMode))
        {
            _channels[channelName] = currentMode & ~mode;
        }
    }

    /// <summary>
    /// Gets the user's mode in a specific channel.
    /// </summary>
    public ChannelMemberMode GetChannelMode(ChannelName channelName)
    {
        return _channels.GetValueOrDefault(channelName, ChannelMemberMode.None);
    }

    /// <summary>
    /// Updates the last activity timestamp.
    /// </summary>
    public void UpdateLastActivity()
    {
        LastActivity = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets idle time in seconds.
    /// </summary>
    public long GetIdleSeconds()
    {
        return (long)(DateTimeOffset.UtcNow - LastActivity).TotalSeconds;
    }

    /// <summary>
    /// Adds nicknames to the monitor list.
    /// </summary>
    /// <param name="nicknames">The nicknames to monitor.</param>
    /// <returns>List of nicknames that couldn't be added due to limit.</returns>
    public IReadOnlyList<string> AddToMonitorList(IEnumerable<string> nicknames)
    {
        var overflow = new List<string>();

        foreach (var nick in nicknames)
        {
            if (_monitorList.Count >= MaxMonitorListSize)
            {
                overflow.Add(nick);
            }
            else
            {
                _monitorList.Add(nick);
            }
        }

        return overflow;
    }

    /// <summary>
    /// Removes nicknames from the monitor list.
    /// </summary>
    /// <param name="nicknames">The nicknames to stop monitoring.</param>
    public void RemoveFromMonitorList(IEnumerable<string> nicknames)
    {
        foreach (var nick in nicknames)
        {
            _monitorList.Remove(nick);
        }
    }

    /// <summary>
    /// Clears the entire monitor list.
    /// </summary>
    public void ClearMonitorList()
    {
        _monitorList.Clear();
    }

    /// <summary>
    /// Checks if a nickname is in the monitor list.
    /// </summary>
    public bool IsMonitoring(string nickname)
    {
        return _monitorList.Contains(nickname);
    }

    /// <summary>
    /// Applies WEBIRC information to update the user's real IP address and hostname.
    /// This should only be called before registration completes.
    /// </summary>
    /// <param name="realIpAddress">The real IP address of the end user.</param>
    /// <param name="realHostname">The real hostname of the end user.</param>
    /// <returns>True if WEBIRC was successfully applied; false if already applied or user is registered.</returns>
    public bool ApplyWebirc(IPAddress realIpAddress, string realHostname)
    {
        // WEBIRC can only be applied once, and only before registration
        if (HasWebircApplied || IsRegistered)
        {
            return false;
        }

        IpAddress = realIpAddress;
        Hostname = realHostname;
        DisplayedHostname = realHostname;
        HasWebircApplied = true;
        return true;
    }
}
