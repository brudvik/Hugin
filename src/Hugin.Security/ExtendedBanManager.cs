using System.Text.RegularExpressions;
using Hugin.Core.Entities;
using Microsoft.Extensions.Logging;

namespace Hugin.Security;

/// <summary>
/// Manages extended (extban) channel bans.
/// Extended bans allow banning based on account, realname, channel membership, etc.
/// Format: ~type:value or $type:value (depending on IRCd convention)
/// </summary>
public sealed class ExtendedBanManager : IExtendedBanManager
{
    private readonly ILogger<ExtendedBanManager> _logger;
    private readonly Dictionary<char, IExtendedBanType> _banTypes = new();

    /// <summary>
    /// Creates a new extended ban manager with default ban types.
    /// </summary>
    /// <param name="logger">Logger for extended ban operations.</param>
    public ExtendedBanManager(ILogger<ExtendedBanManager> logger)
    {
        _logger = logger;
        RegisterDefaultTypes();
    }

    /// <summary>
    /// Registers default extended ban types.
    /// </summary>
    private void RegisterDefaultTypes()
    {
        // Account-based ban (~a:accountname)
        RegisterBanType('a', new AccountBanType());

        // Realname/GECOS ban (~r:pattern)
        RegisterBanType('r', new RealnameBanType());

        // Channel ban (~c:#channel) - must be on specified channel
        RegisterBanType('c', new ChannelBanType());

        // Registered users only ban (~R:) - bans unregistered users
        RegisterBanType('R', new RegisteredBanType());

        // Server ban (~s:server.name)
        RegisterBanType('s', new ServerBanType());

        // Secure/TLS ban (~z:) - bans non-TLS connections
        RegisterBanType('z', new SecureBanType());

        // Oper-only (~o:) - allows only opers
        RegisterBanType('o', new OperBanType());

        // Quiet/mute (~q:mask) - mutes matching users
        RegisterBanType('q', new QuietBanType());

        // Certificate fingerprint (~f:fingerprint)
        RegisterBanType('f', new CertFingerprintBanType());

        // Text/message pattern ban (~T:pattern) - blocks matching messages
        RegisterBanType('T', new TextPatternBanType());
    }

    /// <summary>
    /// Registers a custom extended ban type.
    /// </summary>
    /// <param name="typeChar">The type character (e.g., 'a' for account).</param>
    /// <param name="handler">The ban type handler.</param>
    public void RegisterBanType(char typeChar, IExtendedBanType handler)
    {
        _banTypes[typeChar] = handler;
        _logger.LogDebug("Registered extended ban type: ~{Type}", typeChar);
    }

    /// <inheritdoc />
    public bool IsExtendedBan(string banMask)
    {
        // Extended bans start with ~ or $
        return banMask.Length >= 3 &&
               (banMask[0] == '~' || banMask[0] == '$') &&
               banMask[2] == ':';
    }

    /// <inheritdoc />
    public ExtendedBanParseResult Parse(string banMask)
    {
        if (!IsExtendedBan(banMask))
        {
            return ExtendedBanParseResult.NotExtended;
        }

        var typeChar = banMask[1];
        var value = banMask.Length > 3 ? banMask[3..] : string.Empty;

        if (!_banTypes.TryGetValue(typeChar, out var banType))
        {
            return new ExtendedBanParseResult
            {
                IsExtended = true,
                IsValid = false,
                Error = $"Unknown extended ban type: ~{typeChar}"
            };
        }

        return new ExtendedBanParseResult
        {
            IsExtended = true,
            IsValid = true,
            TypeChar = typeChar,
            TypeName = banType.Name,
            Value = value,
            Description = banType.GetDescription(value)
        };
    }

    /// <inheritdoc />
    public ExtendedBanMatchResult Matches(string banMask, ExtendedBanContext context)
    {
        if (!IsExtendedBan(banMask))
        {
            return ExtendedBanMatchResult.NotApplicable;
        }

        var typeChar = banMask[1];
        var value = banMask.Length > 3 ? banMask[3..] : string.Empty;

        if (!_banTypes.TryGetValue(typeChar, out var banType))
        {
            return ExtendedBanMatchResult.NotApplicable;
        }

        try
        {
            var matches = banType.Matches(value, context);
            return new ExtendedBanMatchResult
            {
                IsApplicable = true,
                Matches = matches,
                BanType = banType.Name,
                IsQuiet = banType.IsQuietType
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking extended ban ~{Type}:{Value}", typeChar, value);
            return ExtendedBanMatchResult.NotApplicable;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ExtendedBanTypeInfo> GetSupportedTypes()
    {
        return _banTypes.Select(kv => new ExtendedBanTypeInfo
        {
            TypeChar = kv.Key,
            Name = kv.Value.Name,
            Description = kv.Value.Description,
            RequiresValue = kv.Value.RequiresValue,
            IsQuietType = kv.Value.IsQuietType
        }).OrderBy(t => t.TypeChar).ToList();
    }
}

/// <summary>
/// Interface for extended ban management.
/// </summary>
public interface IExtendedBanManager
{
    /// <summary>
    /// Checks if a ban mask is an extended ban.
    /// </summary>
    bool IsExtendedBan(string banMask);

    /// <summary>
    /// Parses an extended ban mask.
    /// </summary>
    ExtendedBanParseResult Parse(string banMask);

    /// <summary>
    /// Checks if an extended ban matches a user context.
    /// </summary>
    ExtendedBanMatchResult Matches(string banMask, ExtendedBanContext context);

    /// <summary>
    /// Gets all supported extended ban types.
    /// </summary>
    IReadOnlyList<ExtendedBanTypeInfo> GetSupportedTypes();
}

/// <summary>
/// Interface for an extended ban type.
/// </summary>
public interface IExtendedBanType
{
    /// <summary>Human-readable name.</summary>
    string Name { get; }

    /// <summary>Description of what this ban type does.</summary>
    string Description { get; }

    /// <summary>Whether this ban type requires a value.</summary>
    bool RequiresValue { get; }

    /// <summary>Whether this is a "quiet" type (mute instead of ban).</summary>
    bool IsQuietType { get; }

    /// <summary>Checks if the ban matches a user context.</summary>
    bool Matches(string value, ExtendedBanContext context);

    /// <summary>Gets a human-readable description of a specific ban.</summary>
    string GetDescription(string value);
}

/// <summary>
/// Context for extended ban matching.
/// </summary>
public sealed class ExtendedBanContext
{
    /// <summary>User's nickname.</summary>
    public string? Nickname { get; init; }

    /// <summary>User's username.</summary>
    public string? Username { get; init; }

    /// <summary>User's hostname.</summary>
    public string? Hostname { get; init; }

    /// <summary>User's IP address.</summary>
    public string? IpAddress { get; init; }

    /// <summary>User's account name (if registered).</summary>
    public string? Account { get; init; }

    /// <summary>User's realname/GECOS.</summary>
    public string? Realname { get; init; }

    /// <summary>User's server name.</summary>
    public string? Server { get; init; }

    /// <summary>Whether user is connected via TLS.</summary>
    public bool IsSecure { get; init; }

    /// <summary>Whether user is an IRC operator.</summary>
    public bool IsOper { get; init; }

    /// <summary>User's TLS certificate fingerprint.</summary>
    public string? CertFingerprint { get; init; }

    /// <summary>Channels the user is on.</summary>
    public IReadOnlySet<string>? Channels { get; init; }

    /// <summary>Message being sent (for text pattern bans).</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Result of parsing an extended ban.
/// </summary>
public readonly struct ExtendedBanParseResult
{
    /// <summary>Result for non-extended bans.</summary>
    public static readonly ExtendedBanParseResult NotExtended = new() { IsExtended = false };

    /// <summary>Whether this is an extended ban.</summary>
    public bool IsExtended { get; init; }

    /// <summary>Whether the extended ban is valid.</summary>
    public bool IsValid { get; init; }

    /// <summary>The type character.</summary>
    public char TypeChar { get; init; }

    /// <summary>The type name.</summary>
    public string? TypeName { get; init; }

    /// <summary>The ban value.</summary>
    public string? Value { get; init; }

    /// <summary>Human-readable description.</summary>
    public string? Description { get; init; }

    /// <summary>Error message if invalid.</summary>
    public string? Error { get; init; }
}

/// <summary>
/// Result of matching an extended ban.
/// </summary>
public readonly struct ExtendedBanMatchResult
{
    /// <summary>Not applicable result.</summary>
    public static readonly ExtendedBanMatchResult NotApplicable = new() { IsApplicable = false };

    /// <summary>Whether this ban type was applicable.</summary>
    public bool IsApplicable { get; init; }

    /// <summary>Whether the ban matches.</summary>
    public bool Matches { get; init; }

    /// <summary>The ban type name.</summary>
    public string? BanType { get; init; }

    /// <summary>Whether this is a quiet/mute ban.</summary>
    public bool IsQuiet { get; init; }
}

/// <summary>
/// Information about a supported extended ban type.
/// </summary>
public sealed class ExtendedBanTypeInfo
{
    /// <summary>The type character.</summary>
    public char TypeChar { get; init; }

    /// <summary>Human-readable name.</summary>
    public required string Name { get; init; }

    /// <summary>Description.</summary>
    public required string Description { get; init; }

    /// <summary>Whether this type requires a value.</summary>
    public bool RequiresValue { get; init; }

    /// <summary>Whether this is a quiet type.</summary>
    public bool IsQuietType { get; init; }
}

#region Extended Ban Type Implementations

/// <summary>Account-based extended ban (~a:accountname).</summary>
internal sealed class AccountBanType : IExtendedBanType
{
    public string Name => "account";
    public string Description => "Matches users logged into a specific account";
    public bool RequiresValue => true;
    public bool IsQuietType => false;

    public bool Matches(string value, ExtendedBanContext context)
    {
        if (string.IsNullOrEmpty(context.Account))
        {
            return false;
        }

        return WildcardHelper.WildcardMatch(context.Account, value);
    }

    public string GetDescription(string value) => $"Users with account matching '{value}'";
}

/// <summary>Realname/GECOS extended ban (~r:pattern).</summary>
internal sealed class RealnameBanType : IExtendedBanType
{
    public string Name => "realname";
    public string Description => "Matches users by realname/GECOS";
    public bool RequiresValue => true;
    public bool IsQuietType => false;

    public bool Matches(string value, ExtendedBanContext context)
    {
        if (string.IsNullOrEmpty(context.Realname))
        {
            return false;
        }

        return WildcardHelper.WildcardMatch(context.Realname, value);
    }

    public string GetDescription(string value) => $"Users with realname matching '{value}'";
}

/// <summary>Channel membership extended ban (~c:#channel).</summary>
internal sealed class ChannelBanType : IExtendedBanType
{
    public string Name => "channel";
    public string Description => "Matches users who are on a specific channel";
    public bool RequiresValue => true;
    public bool IsQuietType => false;

    public bool Matches(string value, ExtendedBanContext context)
    {
        if (context.Channels is null)
        {
            return false;
        }

        return context.Channels.Contains(value);
    }

    public string GetDescription(string value) => $"Users who are on {value}";
}

/// <summary>Registered users extended ban (~R:).</summary>
internal sealed class RegisteredBanType : IExtendedBanType
{
    public string Name => "registered";
    public string Description => "Matches unregistered users";
    public bool RequiresValue => false;
    public bool IsQuietType => false;

    public bool Matches(string value, ExtendedBanContext context)
    {
        // Matches if user is NOT registered
        return string.IsNullOrEmpty(context.Account);
    }

    public string GetDescription(string value) => "Unregistered users";
}

/// <summary>Server extended ban (~s:server.name).</summary>
internal sealed class ServerBanType : IExtendedBanType
{
    public string Name => "server";
    public string Description => "Matches users from a specific server";
    public bool RequiresValue => true;
    public bool IsQuietType => false;

    public bool Matches(string value, ExtendedBanContext context)
    {
        if (string.IsNullOrEmpty(context.Server))
        {
            return false;
        }

        return WildcardHelper.WildcardMatch(context.Server, value);
    }

    public string GetDescription(string value) => $"Users from server matching '{value}'";
}

/// <summary>Secure/TLS extended ban (~z:).</summary>
internal sealed class SecureBanType : IExtendedBanType
{
    public string Name => "secure";
    public string Description => "Matches users without TLS/SSL";
    public bool RequiresValue => false;
    public bool IsQuietType => false;

    public bool Matches(string value, ExtendedBanContext context)
    {
        // Matches if user is NOT using TLS
        return !context.IsSecure;
    }

    public string GetDescription(string value) => "Users without TLS/SSL";
}

/// <summary>Oper extended ban (~o:).</summary>
internal sealed class OperBanType : IExtendedBanType
{
    public string Name => "oper";
    public string Description => "Matches non-operators";
    public bool RequiresValue => false;
    public bool IsQuietType => false;

    public bool Matches(string value, ExtendedBanContext context)
    {
        // Matches if user is NOT an oper
        return !context.IsOper;
    }

    public string GetDescription(string value) => "Non-operators";
}

/// <summary>Quiet/mute extended ban (~q:mask).</summary>
internal sealed class QuietBanType : IExtendedBanType
{
    public string Name => "quiet";
    public string Description => "Mutes matching users instead of banning";
    public bool RequiresValue => true;
    public bool IsQuietType => true;

    public bool Matches(string value, ExtendedBanContext context)
    {
        var hostmask = $"{context.Nickname}!{context.Username}@{context.Hostname}";
        return WildcardHelper.WildcardMatch(hostmask, value);
    }

    public string GetDescription(string value) => $"Mutes users matching '{value}'";
}

/// <summary>Certificate fingerprint extended ban (~f:fingerprint).</summary>
internal sealed class CertFingerprintBanType : IExtendedBanType
{
    public string Name => "certfp";
    public string Description => "Matches users by TLS certificate fingerprint";
    public bool RequiresValue => true;
    public bool IsQuietType => false;

    public bool Matches(string value, ExtendedBanContext context)
    {
        if (string.IsNullOrEmpty(context.CertFingerprint))
        {
            return false;
        }

        return context.CertFingerprint.Equals(value, StringComparison.OrdinalIgnoreCase);
    }

    public string GetDescription(string value) => $"Users with certificate fingerprint '{value}'";
}

/// <summary>Text pattern extended ban (~T:pattern).</summary>
internal sealed class TextPatternBanType : IExtendedBanType
{
    public string Name => "text";
    public string Description => "Blocks messages matching a pattern";
    public bool RequiresValue => true;
    public bool IsQuietType => true;

    public bool Matches(string value, ExtendedBanContext context)
    {
        if (string.IsNullOrEmpty(context.Message))
        {
            return false;
        }

        return WildcardHelper.WildcardMatch(context.Message, value);
    }

    public string GetDescription(string value) => $"Messages matching '{value}'";
}

#endregion

/// <summary>
/// Helper for wildcard matching.
/// </summary>
internal static class WildcardHelper
{
    /// <summary>
    /// Performs IRC-style wildcard matching.
    /// </summary>
    /// <param name="input">The input string to match.</param>
    /// <param name="pattern">The pattern with * and ? wildcards.</param>
    /// <returns>True if the input matches the pattern.</returns>
    public static bool WildcardMatch(string input, string pattern)
    {
        // Convert IRC-style wildcards to regex
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        try
        {
            return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        }
        catch
        {
            return false;
        }
    }
}
