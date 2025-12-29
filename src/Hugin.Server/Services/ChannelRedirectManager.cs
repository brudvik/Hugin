using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Hugin.Server.Services;

/// <summary>
/// Manages channel redirect functionality (+L and +F modes).
/// +L: Redirect when channel is full (limit reached)
/// +F: Redirect when banned or invite-only denied
/// </summary>
public sealed class ChannelRedirectManager
{
    private readonly ILogger<ChannelRedirectManager> _logger;
    private readonly ConcurrentDictionary<string, ChannelRedirectSettings> _channelSettings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new channel redirect manager.
    /// </summary>
    /// <param name="logger">Logger for redirect events.</param>
    public ChannelRedirectManager(ILogger<ChannelRedirectManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sets the redirect channel when full (+L mode).
    /// </summary>
    /// <param name="channelName">The source channel name.</param>
    /// <param name="targetChannel">The target channel to redirect to.</param>
    /// <returns>True if successfully set.</returns>
    public bool SetRedirectOnFull(string channelName, string targetChannel)
    {
        if (!IsValidChannelName(targetChannel))
        {
            return false;
        }

        var settings = _channelSettings.GetOrAdd(channelName, _ => new ChannelRedirectSettings());
        settings.RedirectOnFull = targetChannel;

        _logger.LogInformation("Set redirect on full for {Channel} -> {Target}",
            channelName, targetChannel);

        return true;
    }

    /// <summary>
    /// Sets the forward channel on ban/invite-only (+F mode).
    /// </summary>
    /// <param name="channelName">The source channel name.</param>
    /// <param name="targetChannel">The target channel to forward to.</param>
    /// <returns>True if successfully set.</returns>
    public bool SetForwardOnRestriction(string channelName, string targetChannel)
    {
        if (!IsValidChannelName(targetChannel))
        {
            return false;
        }

        var settings = _channelSettings.GetOrAdd(channelName, _ => new ChannelRedirectSettings());
        settings.ForwardOnRestriction = targetChannel;

        _logger.LogInformation("Set forward on restriction for {Channel} -> {Target}",
            channelName, targetChannel);

        return true;
    }

    /// <summary>
    /// Removes the redirect on full setting.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    public void RemoveRedirectOnFull(string channelName)
    {
        if (_channelSettings.TryGetValue(channelName, out var settings))
        {
            settings.RedirectOnFull = null;
            CleanupIfEmpty(channelName, settings);
        }
    }

    /// <summary>
    /// Removes the forward on restriction setting.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    public void RemoveForwardOnRestriction(string channelName)
    {
        if (_channelSettings.TryGetValue(channelName, out var settings))
        {
            settings.ForwardOnRestriction = null;
            CleanupIfEmpty(channelName, settings);
        }
    }

    /// <summary>
    /// Gets the redirect target when channel is full.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <returns>The redirect target, or null if not set.</returns>
    public string? GetRedirectOnFull(string channelName)
    {
        return _channelSettings.TryGetValue(channelName, out var settings)
            ? settings.RedirectOnFull
            : null;
    }

    /// <summary>
    /// Gets the forward target on restriction.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <returns>The forward target, or null if not set.</returns>
    public string? GetForwardOnRestriction(string channelName)
    {
        return _channelSettings.TryGetValue(channelName, out var settings)
            ? settings.ForwardOnRestriction
            : null;
    }

    /// <summary>
    /// Determines if a user should be redirected due to channel being full.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <param name="currentUserCount">Current number of users in channel.</param>
    /// <param name="userLimit">The channel's user limit.</param>
    /// <returns>The redirect target channel, or null if no redirect.</returns>
    public string? ShouldRedirectOnFull(string channelName, int currentUserCount, int userLimit)
    {
        if (userLimit <= 0 || currentUserCount < userLimit)
        {
            return null;
        }

        var target = GetRedirectOnFull(channelName);
        if (target is not null)
        {
            _logger.LogDebug("Redirecting from full channel {Channel} to {Target}",
                channelName, target);
        }

        return target;
    }

    /// <summary>
    /// Determines if a user should be forwarded due to a restriction.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <param name="reason">The reason for the restriction (ban, invite-only, etc.).</param>
    /// <returns>The forward target channel, or null if no forward.</returns>
    public string? ShouldForwardOnRestriction(string channelName, RedirectReason reason)
    {
        var target = GetForwardOnRestriction(channelName);
        if (target is not null)
        {
            _logger.LogDebug("Forwarding from restricted channel {Channel} to {Target} (reason: {Reason})",
                channelName, target, reason);
        }

        return target;
    }

    /// <summary>
    /// Validates that a target channel name is valid.
    /// </summary>
    private static bool IsValidChannelName(string channelName)
    {
        if (string.IsNullOrEmpty(channelName))
        {
            return false;
        }

        // Must start with # or &
        return channelName[0] is '#' or '&' && channelName.Length >= 2;
    }

    /// <summary>
    /// Removes settings entry if both redirects are null.
    /// </summary>
    private void CleanupIfEmpty(string channelName, ChannelRedirectSettings settings)
    {
        if (settings.RedirectOnFull is null && settings.ForwardOnRestriction is null)
        {
            _channelSettings.TryRemove(channelName, out _);
        }
    }
}

/// <summary>
/// Redirect settings for a channel.
/// </summary>
public sealed class ChannelRedirectSettings
{
    /// <summary>
    /// Channel to redirect to when this channel is full (+L).
    /// </summary>
    public string? RedirectOnFull { get; set; }

    /// <summary>
    /// Channel to forward to on ban or invite-only (+F).
    /// </summary>
    public string? ForwardOnRestriction { get; set; }
}

/// <summary>
/// Reason for redirect/forward.
/// </summary>
public enum RedirectReason
{
    /// <summary>Channel is full.</summary>
    Full,

    /// <summary>User is banned.</summary>
    Banned,

    /// <summary>Channel is invite-only.</summary>
    InviteOnly,

    /// <summary>Channel requires registration.</summary>
    RegisteredOnly,

    /// <summary>Wrong channel key.</summary>
    BadKey
}
