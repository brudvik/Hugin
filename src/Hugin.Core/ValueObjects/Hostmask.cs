using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.RegularExpressions;

namespace Hugin.Core.ValueObjects;

/// <summary>
/// Represents a user's hostmask (nick!user@host).
/// </summary>
public sealed partial class Hostmask : IEquatable<Hostmask>
{
    /// <summary>
    /// Gets the nickname part.
    /// </summary>
    public string Nick { get; }

    /// <summary>
    /// Gets the username (ident) part.
    /// </summary>
    public string User { get; }

    /// <summary>
    /// Gets the hostname part.
    /// </summary>
    public string Host { get; }

    private Hostmask(string nick, string user, string host)
    {
        Nick = nick;
        User = user;
        Host = host;
    }

    /// <summary>
    /// Creates a hostmask from components.
    /// </summary>
    public static Hostmask Create(string nick, string user, string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nick);
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        return new Hostmask(nick, user, host);
    }

    /// <summary>
    /// Tries to parse a hostmask string.
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out Hostmask? hostmask)
    {
        hostmask = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = HostmaskRegex().Match(value);
        if (!match.Success)
        {
            return false;
        }

        hostmask = new Hostmask(
            match.Groups["nick"].Value,
            match.Groups["user"].Value,
            match.Groups["host"].Value
        );
        return true;
    }

    /// <summary>
    /// Parses a hostmask string, throwing if invalid.
    /// </summary>
    public static Hostmask Parse(string value)
    {
        if (!TryParse(value, out var hostmask))
        {
            throw new FormatException($"Invalid hostmask format: {value}");
        }

        return hostmask;
    }

    /// <summary>
    /// Checks if this hostmask matches a mask pattern (with wildcards).
    /// </summary>
    public bool Matches(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        string fullMask = ToString();
        return WildcardMatch(fullMask, pattern);
    }

    /// <summary>
    /// Creates a cloaked/masked hostname for privacy.
    /// </summary>
    public Hostmask WithCloakedHost(string cloakedHost)
    {
        return new Hostmask(Nick, User, cloakedHost);
    }

    /// <summary>
    /// Creates a new hostmask with updated nickname.
    /// </summary>
    public Hostmask WithNick(string newNick)
    {
        return new Hostmask(newNick, User, Host);
    }

    public bool Equals(Hostmask? other)
    {
        if (other is null)
        {
            return false;
        }

        return string.Equals(Nick, other.Nick, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(User, other.User, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => Equals(obj as Hostmask);

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Nick.ToUpperInvariant(),
            User.ToUpperInvariant(),
            Host.ToUpperInvariant()
        );
    }

    public override string ToString() => $"{Nick}!{User}@{Host}";

    /// <summary>
    /// Performs wildcard matching (* and ?).
    /// Uses NonBacktracking to prevent ReDoS attacks.
    /// </summary>
    /// <param name="text">The text to match against.</param>
    /// <param name="pattern">The wildcard pattern (* and ? supported).</param>
    /// <returns>True if the text matches the pattern; otherwise false.</returns>
    public static bool WildcardMatch(string text, string pattern)
    {
        // Convert IRC wildcard pattern to regex
        string regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        // Use NonBacktracking to prevent catastrophic backtracking (ReDoS)
        return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
    }

    [GeneratedRegex(@"^(?<nick>[^!]+)!(?<user>[^@]+)@(?<host>.+)$")]
    private static partial Regex HostmaskRegex();
}
