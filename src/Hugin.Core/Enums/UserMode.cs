namespace Hugin.Core.Enums;

/// <summary>
/// User modes as defined in RFC 2812 and Modern IRC.
/// </summary>
[Flags]
public enum UserMode
{
    /// <summary>No modes set.</summary>
    None = 0,

    /// <summary>User is invisible (+i).</summary>
    Invisible = 1 << 0,

    /// <summary>User receives server notices (+s).</summary>
    ServerNotices = 1 << 1,

    /// <summary>User receives wallops (+w).</summary>
    Wallops = 1 << 2,

    /// <summary>User is an IRC operator (+o).</summary>
    Operator = 1 << 3,

    /// <summary>User is away (+a).</summary>
    Away = 1 << 4,

    /// <summary>User has registered/identified with services (+r).</summary>
    Registered = 1 << 5,

    /// <summary>User is using a secure connection (+Z).</summary>
    Secure = 1 << 6,

    /// <summary>User is a bot (+B).</summary>
    Bot = 1 << 7
}

/// <summary>
/// Extension methods for UserMode.
/// </summary>
public static class UserModeExtensions
{
    /// <summary>
    /// Converts a mode character to a UserMode flag.
    /// </summary>
    public static UserMode? FromChar(char c)
    {
        return c switch
        {
            'i' => UserMode.Invisible,
            's' => UserMode.ServerNotices,
            'w' => UserMode.Wallops,
            'o' => UserMode.Operator,
            'a' => UserMode.Away,
            'r' => UserMode.Registered,
            'Z' => UserMode.Secure,
            'B' => UserMode.Bot,
            _ => null
        };
    }

    /// <summary>
    /// Converts a UserMode flag to its character representation.
    /// </summary>
    public static char ToChar(this UserMode mode)
    {
        return mode switch
        {
            UserMode.Invisible => 'i',
            UserMode.ServerNotices => 's',
            UserMode.Wallops => 'w',
            UserMode.Operator => 'o',
            UserMode.Away => 'a',
            UserMode.Registered => 'r',
            UserMode.Secure => 'Z',
            UserMode.Bot => 'B',
            _ => ' '
        };
    }

    /// <summary>
    /// Converts UserMode flags to a mode string (e.g., "+iw").
    /// </summary>
    public static string ToModeString(this UserMode modes)
    {
        if (modes == UserMode.None)
        {
            return "+";
        }

        var chars = new List<char>();
        foreach (UserMode mode in Enum.GetValues<UserMode>())
        {
            if (mode != UserMode.None && modes.HasFlag(mode))
            {
                char c = mode.ToChar();
                if (c != ' ')
                {
                    chars.Add(c);
                }
            }
        }

        return chars.Count > 0 ? "+" + new string(chars.ToArray()) : "+";
    }
}
