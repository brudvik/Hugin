using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Hugin.Core.ValueObjects;

/// <summary>
/// Represents a validated IRC nickname.
/// </summary>
public sealed partial class Nickname : IEquatable<Nickname>
{
    /// <summary>
    /// Maximum length for a nickname (configurable per server, default 30).
    /// </summary>
    public const int MaxLength = 30;

    /// <summary>
    /// Gets the nickname value.
    /// </summary>
    public string Value { get; }

    private Nickname(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Tries to create a Nickname from a string.
    /// </summary>
    public static bool TryCreate(string? value, [NotNullWhen(true)] out Nickname? nickname, out string? error)
    {
        nickname = null;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Nickname cannot be empty";
            return false;
        }

        if (value.Length > MaxLength)
        {
            error = $"Nickname cannot exceed {MaxLength} characters";
            return false;
        }

        // Must not contain: space, comma, asterisk, question mark, exclamation mark, at sign
        // Must not start with: dollar, colon, hash, ampersand, or prefix characters
        if (!NicknameRegex().IsMatch(value))
        {
            error = "Nickname contains invalid characters";
            return false;
        }

        // Must not start with digit (per RFC)
        if (char.IsDigit(value[0]))
        {
            error = "Nickname cannot start with a digit";
            return false;
        }

        // Should not contain dots (reserved for servers)
        if (value.Contains('.'))
        {
            error = "Nickname should not contain dots";
            return false;
        }

        nickname = new Nickname(value);
        return true;
    }

    /// <summary>
    /// Creates a Nickname from a string, throwing if invalid.
    /// </summary>
    public static Nickname Create(string value)
    {
        if (!TryCreate(value, out var nickname, out var error))
        {
            throw new ArgumentException(error, nameof(value));
        }

        return nickname;
    }

    /// <summary>
    /// Compares nicknames using ASCII casemapping (case-insensitive for a-z).
    /// </summary>
    public bool Equals(Nickname? other)
    {
        if (other is null)
        {
            return false;
        }

        return string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => Equals(obj as Nickname);

    public override int GetHashCode() => Value.ToUpperInvariant().GetHashCode();

    public override string ToString() => Value;

    public static implicit operator string(Nickname nickname) => nickname.Value;

    public static bool operator ==(Nickname? left, Nickname? right)
    {
        if (left is null)
        {
            return right is null;
        }

        return left.Equals(right);
    }

    public static bool operator !=(Nickname? left, Nickname? right) => !(left == right);

    [GeneratedRegex(@"^[A-Za-z_\[\]\\`^{}|][A-Za-z0-9_\[\]\\`^{}\-|]*$")]
    private static partial Regex NicknameRegex();
}
