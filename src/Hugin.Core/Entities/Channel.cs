using Hugin.Core.Enums;
using Hugin.Core.ValueObjects;

namespace Hugin.Core.Entities;

/// <summary>
/// Represents an IRC channel.
/// </summary>
public sealed class Channel
{
    /// <summary>
    /// Gets the channel name.
    /// </summary>
    public ChannelName Name { get; }

    /// <summary>
    /// Gets or sets the channel topic.
    /// </summary>
    public string? Topic { get; private set; }

    /// <summary>
    /// Gets who set the topic.
    /// </summary>
    public string? TopicSetBy { get; private set; }

    /// <summary>
    /// Gets when the topic was set.
    /// </summary>
    public DateTimeOffset? TopicSetAt { get; private set; }

    /// <summary>
    /// Gets the channel modes.
    /// </summary>
    public ChannelMode Modes { get; private set; }

    /// <summary>
    /// Gets the channel key (password), if set.
    /// </summary>
    public string? Key { get; private set; }

    /// <summary>
    /// Gets the user limit, if set.
    /// </summary>
    public int? UserLimit { get; private set; }

    /// <summary>
    /// Gets the channel creation time.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Gets the channel members.
    /// </summary>
    public IReadOnlyDictionary<Guid, ChannelMember> Members => _members;
    private readonly Dictionary<Guid, ChannelMember> _members = new();

    /// <summary>
    /// Gets the ban list.
    /// </summary>
    public IReadOnlyList<BanEntry> Bans => _bans;
    private readonly List<BanEntry> _bans = new();

    /// <summary>
    /// Gets the ban exception list.
    /// </summary>
    public IReadOnlyList<BanEntry> BanExceptions => _banExceptions;
    private readonly List<BanEntry> _banExceptions = new();

    /// <summary>
    /// Gets the invite exception list.
    /// </summary>
    public IReadOnlyList<BanEntry> InviteExceptions => _inviteExceptions;
    private readonly List<BanEntry> _inviteExceptions = new();

    /// <summary>
    /// Gets the pending invitations.
    /// </summary>
    public IReadOnlySet<Guid> Invitations => _invitations;
    private readonly HashSet<Guid> _invitations = new();

    /// <summary>
    /// Gets whether the channel is empty.
    /// </summary>
    public bool IsEmpty => _members.Count == 0;

    /// <summary>
    /// Gets the member count.
    /// </summary>
    public int MemberCount => _members.Count;

    /// <summary>
    /// Creates a new channel.
    /// </summary>
    public Channel(ChannelName name)
    {
        Name = name;
        CreatedAt = DateTimeOffset.UtcNow;
        // Default modes for new channels
        Modes = ChannelMode.NoExternalMessages | ChannelMode.TopicProtected;
    }

    /// <summary>
    /// Sets the channel topic.
    /// </summary>
    public void SetTopic(string? topic, string setBy)
    {
        Topic = topic;
        TopicSetBy = setBy;
        TopicSetAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Adds a member to the channel.
    /// </summary>
    public ChannelMember AddMember(User user, ChannelMemberMode mode = ChannelMemberMode.None)
    {
        var member = new ChannelMember(user.ConnectionId, user.Nickname, mode);
        _members[user.ConnectionId] = member;
        _invitations.Remove(user.ConnectionId);
        return member;
    }

    /// <summary>
    /// Removes a member from the channel.
    /// </summary>
    public bool RemoveMember(Guid connectionId)
    {
        return _members.Remove(connectionId);
    }

    /// <summary>
    /// Gets a member by connection ID.
    /// </summary>
    public ChannelMember? GetMember(Guid connectionId)
    {
        return _members.GetValueOrDefault(connectionId);
    }

    /// <summary>
    /// Checks if a user is a member.
    /// </summary>
    public bool HasMember(Guid connectionId)
    {
        return _members.ContainsKey(connectionId);
    }

    /// <summary>
    /// Updates a member's nickname (after NICK change).
    /// </summary>
    public void UpdateMemberNickname(Guid connectionId, Nickname newNickname)
    {
        if (_members.TryGetValue(connectionId, out var member))
        {
            member.UpdateNickname(newNickname);
        }
    }

    /// <summary>
    /// Sets a member's mode.
    /// </summary>
    public void SetMemberMode(Guid connectionId, ChannelMemberMode mode)
    {
        if (_members.TryGetValue(connectionId, out var member))
        {
            member.SetMode(mode);
        }
    }

    /// <summary>
    /// Adds a mode to a member.
    /// </summary>
    public void AddMemberMode(Guid connectionId, ChannelMemberMode mode)
    {
        if (_members.TryGetValue(connectionId, out var member))
        {
            member.AddMode(mode);
        }
    }

    /// <summary>
    /// Removes a mode from a member.
    /// </summary>
    public void RemoveMemberMode(Guid connectionId, ChannelMemberMode mode)
    {
        if (_members.TryGetValue(connectionId, out var member))
        {
            member.RemoveMode(mode);
        }
    }

    /// <summary>
    /// Adds a channel mode.
    /// </summary>
    public void AddMode(ChannelMode mode)
    {
        Modes |= mode;
    }

    /// <summary>
    /// Removes a channel mode.
    /// </summary>
    public void RemoveMode(ChannelMode mode)
    {
        Modes &= ~mode;
    }

    /// <summary>
    /// Sets the channel key.
    /// </summary>
    public void SetKey(string key)
    {
        Key = key;
        Modes |= ChannelMode.Key;
    }

    /// <summary>
    /// Removes the channel key.
    /// </summary>
    public void RemoveKey()
    {
        Key = null;
        Modes &= ~ChannelMode.Key;
    }

    /// <summary>
    /// Sets the user limit.
    /// </summary>
    public void SetLimit(int limit)
    {
        UserLimit = limit;
        Modes |= ChannelMode.Limit;
    }

    /// <summary>
    /// Removes the user limit.
    /// </summary>
    public void RemoveLimit()
    {
        UserLimit = null;
        Modes &= ~ChannelMode.Limit;
    }

    /// <summary>
    /// Adds a ban.
    /// </summary>
    public void AddBan(string mask, string setBy)
    {
        if (!_bans.Any(b => b.Mask.Equals(mask, StringComparison.OrdinalIgnoreCase)))
        {
            _bans.Add(new BanEntry(mask, setBy, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>
    /// Removes a ban.
    /// </summary>
    public bool RemoveBan(string mask)
    {
        var ban = _bans.FirstOrDefault(b => b.Mask.Equals(mask, StringComparison.OrdinalIgnoreCase));
        if (ban is not null)
        {
            _bans.Remove(ban);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if a hostmask is banned.
    /// </summary>
    public bool IsBanned(Hostmask hostmask)
    {
        bool isBanned = _bans.Any(b => hostmask.Matches(b.Mask));
        if (!isBanned)
        {
            return false;
        }

        // Check for ban exception
        bool isExcepted = _banExceptions.Any(e => hostmask.Matches(e.Mask));
        return !isExcepted;
    }

    /// <summary>
    /// Adds an invitation.
    /// </summary>
    public void AddInvitation(Guid connectionId)
    {
        _invitations.Add(connectionId);
    }

    /// <summary>
    /// Checks if a user is invited.
    /// </summary>
    public bool IsInvited(Guid connectionId)
    {
        return _invitations.Contains(connectionId);
    }

    /// <summary>
    /// Checks if a hostmask has an invite exception.
    /// </summary>
    public bool HasInviteException(Hostmask hostmask)
    {
        return _inviteExceptions.Any(e => hostmask.Matches(e.Mask));
    }

    /// <summary>
    /// Gets the mode string for the channel.
    /// </summary>
    public string GetModeString()
    {
        var modes = new List<char>();
        var args = new List<string>();

        if (Modes.HasFlag(ChannelMode.InviteOnly))
        {
            modes.Add('i');
        }

        if (Modes.HasFlag(ChannelMode.Moderated))
        {
            modes.Add('m');
        }

        if (Modes.HasFlag(ChannelMode.NoExternalMessages))
        {
            modes.Add('n');
        }

        if (Modes.HasFlag(ChannelMode.Secret))
        {
            modes.Add('s');
        }

        if (Modes.HasFlag(ChannelMode.TopicProtected))
        {
            modes.Add('t');
        }

        if (Modes.HasFlag(ChannelMode.Key) && !string.IsNullOrEmpty(Key))
        {
            modes.Add('k');
            args.Add(Key);
        }

        if (Modes.HasFlag(ChannelMode.Limit) && UserLimit.HasValue)
        {
            modes.Add('l');
            args.Add(UserLimit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        string modeString = modes.Count > 0 ? "+" + new string(modes.ToArray()) : "+";
        if (args.Count > 0)
        {
            modeString += " " + string.Join(" ", args);
        }

        return modeString;
    }
}

/// <summary>
/// Represents a channel member.
/// </summary>
public sealed class ChannelMember
{
    /// <summary>
    /// Gets the user's connection ID.
    /// </summary>
    public Guid ConnectionId { get; }

    /// <summary>
    /// Gets the user's current nickname.
    /// </summary>
    public Nickname Nickname { get; private set; }

    /// <summary>
    /// Gets the member's modes in this channel.
    /// </summary>
    public ChannelMemberMode Modes { get; private set; }

    /// <summary>
    /// Gets when the user joined the channel.
    /// </summary>
    public DateTimeOffset JoinedAt { get; }

    /// <summary>
    /// Gets whether the user can speak in moderated channels.
    /// </summary>
    public bool CanSpeak => Modes != ChannelMemberMode.None;

    /// <summary>
    /// Gets whether the user is at least a half-operator.
    /// </summary>
    public bool IsHalfOpOrHigher => Modes >= ChannelMemberMode.HalfOp;

    /// <summary>
    /// Gets whether the user is at least an operator.
    /// </summary>
    public bool IsOpOrHigher => Modes >= ChannelMemberMode.Op;

    /// <summary>
    /// Creates a new channel member.
    /// </summary>
    /// <param name="connectionId">The user's connection ID.</param>
    /// <param name="nickname">The user's nickname.</param>
    /// <param name="modes">Initial channel member modes.</param>
    public ChannelMember(Guid connectionId, Nickname nickname, ChannelMemberMode modes)
    {
        ConnectionId = connectionId;
        Nickname = nickname;
        Modes = modes;
        JoinedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Updates the member's nickname after a NICK change.
    /// </summary>
    /// <param name="newNickname">The new nickname.</param>
    public void UpdateNickname(Nickname newNickname)
    {
        Nickname = newNickname;
    }

    /// <summary>
    /// Sets the member's channel modes.
    /// </summary>
    /// <param name="mode">The mode to set.</param>
    public void SetMode(ChannelMemberMode mode)
    {
        Modes = mode;
    }

    /// <summary>
    /// Adds a channel mode to the member.
    /// </summary>
    /// <param name="mode">The mode to add.</param>
    public void AddMode(ChannelMemberMode mode)
    {
        Modes |= mode;
    }

    /// <summary>
    /// Removes a channel mode from the member.
    /// </summary>
    /// <param name="mode">The mode to remove.</param>
    public void RemoveMode(ChannelMemberMode mode)
    {
        Modes &= ~mode;
    }
}

/// <summary>
/// Represents a ban, exception, or invite exception entry in a channel.
/// </summary>
/// <param name="Mask">The hostmask pattern (e.g., *!*@*.example.com).</param>
/// <param name="SetBy">The nickname or hostmask of the user who set the entry.</param>
/// <param name="SetAt">The timestamp when the entry was created.</param>
public sealed record BanEntry(string Mask, string SetBy, DateTimeOffset SetAt);
