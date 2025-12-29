namespace Hugin.Core.ValueObjects;

/// <summary>
/// Represents a unique server identifier for S2S communication.
/// </summary>
public sealed class ServerId : IEquatable<ServerId>
{
    /// <summary>
    /// Gets the server's unique SID (Server ID) - typically 3 characters.
    /// </summary>
    public string Sid { get; }

    /// <summary>
    /// Gets the server's name (e.g., "irc.example.com").
    /// </summary>
    public string Name { get; }

    private ServerId(string sid, string name)
    {
        Sid = sid;
        Name = name;
    }

    /// <summary>
    /// Creates a new ServerId.
    /// </summary>
    public static ServerId Create(string sid, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sid);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (sid.Length != 3)
        {
            throw new ArgumentException("SID must be exactly 3 characters", nameof(sid));
        }

        if (!name.Contains('.'))
        {
            throw new ArgumentException("Server name should contain a dot", nameof(name));
        }

        return new ServerId(sid.ToUpperInvariant(), name.ToLowerInvariant());
    }

    public bool Equals(ServerId? other)
    {
        if (other is null)
        {
            return false;
        }

        return string.Equals(Sid, other.Sid, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj) => Equals(obj as ServerId);

    public override int GetHashCode() => Sid.GetHashCode();

    public override string ToString() => $"{Sid} ({Name})";

    public static bool operator ==(ServerId? left, ServerId? right)
    {
        if (left is null)
        {
            return right is null;
        }

        return left.Equals(right);
    }

    public static bool operator !=(ServerId? left, ServerId? right) => !(left == right);
}
