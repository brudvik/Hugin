using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Hugin.Core.ValueObjects;

/// <summary>
/// Represents a validated IRC channel name.
/// </summary>
public sealed partial class ChannelName : IEquatable<ChannelName>
{
    /// <summary>
    /// Maximum length for a channel name.
    /// </summary>
    public const int MaxLength = 50;

    /// <summary>
    /// Valid channel prefixes.
    /// </summary>
    public static readonly char[] ValidPrefixes = ['#', '&'];

    /// <summary>
    /// Gets the channel name value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets the channel prefix character.
    /// </summary>
    public char Prefix => Value[0];

    /// <summary>
    /// Gets whether this is a local channel (&amp;).
    /// </summary>
    public bool IsLocal => Prefix == '&';

    /// <summary>
    /// Gets the channel name without the prefix.
    /// </summary>
    public string NameWithoutPrefix => Value[1..];

    private ChannelName(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Tries to create a ChannelName from a string.
    /// </summary>
    public static bool TryCreate(string? value, [NotNullWhen(true)] out ChannelName? channelName, out string? error)
    {
        channelName = null;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Channel name cannot be empty";
            return false;
        }

        if (value.Length > MaxLength)
        {
            error = $"Channel name cannot exceed {MaxLength} characters";
            return false;
        }

        if (value.Length < 2)
        {
            error = "Channel name must be at least 2 characters (prefix + name)";
            return false;
        }

        if (!ValidPrefixes.Contains(value[0]))
        {
            error = $"Channel name must start with one of: {string.Join(", ", ValidPrefixes)}";
            return false;
        }

        // Must not contain: space, bell (^G), comma
        if (!ChannelNameRegex().IsMatch(value))
        {
            error = "Channel name contains invalid characters (space, bell, or comma)";
            return false;
        }

        channelName = new ChannelName(value);
        return true;
    }

    /// <summary>
    /// Creates a ChannelName from a string, throwing if invalid.
    /// </summary>
    public static ChannelName Create(string value)
    {
        if (!TryCreate(value, out var channelName, out var error))
        {
            throw new ArgumentException(error, nameof(value));
        }

        return channelName;
    }

    /// <summary>
    /// Checks if a string looks like a channel name.
    /// </summary>
    public static bool IsValidPrefix(string? value)
    {
        return !string.IsNullOrEmpty(value) && ValidPrefixes.Contains(value[0]);
    }

    public bool Equals(ChannelName? other)
    {
        if (other is null)
        {
            return false;
        }

        // Channel names are case-insensitive
        return string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => Equals(obj as ChannelName);

    public override int GetHashCode() => Value.ToUpperInvariant().GetHashCode();

    public override string ToString() => Value;

    public static implicit operator string(ChannelName channelName) => channelName.Value;

    public static bool operator ==(ChannelName? left, ChannelName? right)
    {
        if (left is null)
        {
            return right is null;
        }

        return left.Equals(right);
    }

    public static bool operator !=(ChannelName? left, ChannelName? right) => !(left == right);

    // Matches channel names that don't contain space (0x20), bell (0x07), or comma (0x2C)
    [GeneratedRegex(@"^[#&][^\x00\x07\x20,]+$")]
    private static partial Regex ChannelNameRegex();
}
