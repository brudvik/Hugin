namespace Hugin.Core.Enums;

/// <summary>
/// Channel modes as defined in RFC 2812 and Modern IRC.
/// </summary>
[Flags]
public enum ChannelMode
{
    /// <summary>No modes set.</summary>
    None = 0,

    // Type D modes (no parameter)

    /// <summary>Invite only channel (+i).</summary>
    InviteOnly = 1 << 0,

    /// <summary>Moderated channel (+m).</summary>
    Moderated = 1 << 1,

    /// <summary>No external messages (+n).</summary>
    NoExternalMessages = 1 << 2,

    /// <summary>Private channel (+p) - deprecated, use Secret.</summary>
    Private = 1 << 3,

    /// <summary>Secret channel (+s).</summary>
    Secret = 1 << 4,

    /// <summary>Topic settable by channel operators only (+t).</summary>
    TopicProtected = 1 << 5,

    /// <summary>Channel is registered (+r).</summary>
    Registered = 1 << 6,

    /// <summary>Block CTCP messages to channel (+C).</summary>
    NoCTCP = 1 << 7,

    /// <summary>Strip color codes from messages (+S).</summary>
    StripColors = 1 << 8,

    /// <summary>Block messages with color codes (+c).</summary>
    NoColors = 1 << 9,

    /// <summary>Registered users only (+R).</summary>
    RegisteredOnly = 1 << 12,

    /// <summary>Flood protection (+f).</summary>
    FloodProtection = 1 << 13,

    /// <summary>Join throttling (+j).</summary>
    JoinThrottle = 1 << 14,

    /// <summary>Redirect on full (+L).</summary>
    RedirectOnFull = 1 << 15,

    /// <summary>Forward on ban/invite-only (+F).</summary>
    ForwardOnRestriction = 1 << 16,

    /// <summary>Delayed join/auditorium mode (+D).</summary>
    DelayedJoin = 1 << 17,

    // Type C modes (parameter when setting, no parameter when unsetting)

    /// <summary>Channel has a key/password (+k).</summary>
    Key = 1 << 10,

    /// <summary>Channel has a user limit (+l).</summary>
    Limit = 1 << 11
}

/// <summary>
/// Channel membership prefix modes.
/// </summary>
[Flags]
public enum ChannelMemberMode
{
    /// <summary>No special status.</summary>
    None = 0,

    /// <summary>Voice (+v, prefix +).</summary>
    Voice = 1 << 0,

    /// <summary>Half-operator (+h, prefix %).</summary>
    HalfOp = 1 << 1,

    /// <summary>Operator (+o, prefix @).</summary>
    Op = 1 << 2,

    /// <summary>Protected/Admin (+a, prefix &amp;).</summary>
    Admin = 1 << 3,

    /// <summary>Founder/Owner (+q, prefix ~).</summary>
    Owner = 1 << 4
}

/// <summary>
/// Extension methods for channel modes.
/// </summary>
public static class ChannelModeExtensions
{
    /// <summary>
    /// Gets the prefix character for a channel member mode.
    /// </summary>
    public static char GetPrefix(this ChannelMemberMode mode)
    {
        // Return highest prefix
        if (mode.HasFlag(ChannelMemberMode.Owner))
        {
            return '~';
        }

        if (mode.HasFlag(ChannelMemberMode.Admin))
        {
            return '&';
        }

        if (mode.HasFlag(ChannelMemberMode.Op))
        {
            return '@';
        }

        if (mode.HasFlag(ChannelMemberMode.HalfOp))
        {
            return '%';
        }

        if (mode.HasFlag(ChannelMemberMode.Voice))
        {
            return '+';
        }

        return '\0';
    }

    /// <summary>
    /// Gets all prefix characters for a channel member mode (for multi-prefix).
    /// </summary>
    public static string GetAllPrefixes(this ChannelMemberMode mode)
    {
        var prefixes = new List<char>();

        if (mode.HasFlag(ChannelMemberMode.Owner))
        {
            prefixes.Add('~');
        }

        if (mode.HasFlag(ChannelMemberMode.Admin))
        {
            prefixes.Add('&');
        }

        if (mode.HasFlag(ChannelMemberMode.Op))
        {
            prefixes.Add('@');
        }

        if (mode.HasFlag(ChannelMemberMode.HalfOp))
        {
            prefixes.Add('%');
        }

        if (mode.HasFlag(ChannelMemberMode.Voice))
        {
            prefixes.Add('+');
        }

        return new string(prefixes.ToArray());
    }

    /// <summary>
    /// Converts a mode character to ChannelMemberMode.
    /// </summary>
    public static ChannelMemberMode? FromModeChar(char c)
    {
        return c switch
        {
            'v' => ChannelMemberMode.Voice,
            'h' => ChannelMemberMode.HalfOp,
            'o' => ChannelMemberMode.Op,
            'a' => ChannelMemberMode.Admin,
            'q' => ChannelMemberMode.Owner,
            _ => null
        };
    }

    /// <summary>
    /// Converts a prefix character to ChannelMemberMode.
    /// </summary>
    public static ChannelMemberMode? FromPrefixChar(char c)
    {
        return c switch
        {
            '+' => ChannelMemberMode.Voice,
            '%' => ChannelMemberMode.HalfOp,
            '@' => ChannelMemberMode.Op,
            '&' => ChannelMemberMode.Admin,
            '~' => ChannelMemberMode.Owner,
            _ => null
        };
    }
}
