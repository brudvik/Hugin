using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Hugin.Security;

/// <summary>
/// Filters messages based on configurable rules.
/// Supports regex patterns, keywords, and various matching modes.
/// </summary>
public sealed class SpamFilter : ISpamFilter
{
    private readonly ILogger<SpamFilter> _logger;
    private readonly ConcurrentDictionary<string, SpamFilterRule> _rules = new();
    private readonly object _configLock = new();
    private int _nextRuleId;

    /// <summary>
    /// Creates a new spam filter.
    /// </summary>
    /// <param name="logger">Logger for filter operations.</param>
    public SpamFilter(ILogger<SpamFilter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Adds a filter rule.
    /// </summary>
    /// <param name="pattern">The pattern to match.</param>
    /// <param name="target">What to match against (message, nick, channel, etc.).</param>
    /// <param name="action">Action to take on match.</param>
    /// <param name="reason">Reason to log/show.</param>
    /// <param name="isRegex">Whether pattern is a regex.</param>
    /// <param name="duration">Duration for temporary actions (e.g., bans).</param>
    /// <returns>The rule ID.</returns>
    public string AddRule(
        string pattern,
        SpamFilterTarget target,
        SpamFilterAction action,
        string reason,
        bool isRegex = true,
        TimeSpan? duration = null)
    {
        var ruleId = $"rule_{Interlocked.Increment(ref _nextRuleId)}";

        Regex? regex = null;
        if (isRegex)
        {
            try
            {
                regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
            }
            catch (RegexParseException ex)
            {
                _logger.LogError(ex, "Invalid regex pattern for rule: {Pattern}", pattern);
                throw new ArgumentException($"Invalid regex pattern: {ex.Message}", nameof(pattern), ex);
            }
        }

        var rule = new SpamFilterRule
        {
            Id = ruleId,
            Pattern = pattern,
            CompiledPattern = regex,
            IsRegex = isRegex,
            Target = target,
            Action = action,
            Reason = reason,
            Duration = duration,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _rules[ruleId] = rule;

        _logger.LogInformation("Added spam filter rule {Id}: {Pattern} -> {Action}",
            ruleId, pattern, action);

        return ruleId;
    }

    /// <summary>
    /// Removes a filter rule.
    /// </summary>
    /// <param name="ruleId">The rule ID to remove.</param>
    /// <returns>True if removed.</returns>
    public bool RemoveRule(string ruleId)
    {
        var removed = _rules.TryRemove(ruleId, out _);
        if (removed)
        {
            _logger.LogInformation("Removed spam filter rule {Id}", ruleId);
        }
        return removed;
    }

    /// <summary>
    /// Enables or disables a rule.
    /// </summary>
    /// <param name="ruleId">The rule ID.</param>
    /// <param name="enabled">Whether to enable the rule.</param>
    /// <returns>True if rule exists.</returns>
    public bool SetRuleEnabled(string ruleId, bool enabled)
    {
        if (_rules.TryGetValue(ruleId, out var rule))
        {
            rule.IsEnabled = enabled;
            _logger.LogInformation("Spam filter rule {Id} {State}",
                ruleId, enabled ? "enabled" : "disabled");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gets all filter rules.
    /// </summary>
    public IReadOnlyList<SpamFilterRule> GetRules()
    {
        return _rules.Values.ToList();
    }

    /// <inheritdoc />
    public SpamFilterResult CheckMessage(SpamFilterContext context)
    {
        foreach (var rule in _rules.Values)
        {
            if (!rule.IsEnabled)
            {
                continue;
            }

            // Check if rule applies to this target
            if (!TargetMatches(rule.Target, context))
            {
                continue;
            }

            // Get the text to check based on target
            var textToCheck = GetTextForTarget(rule.Target, context);
            if (string.IsNullOrEmpty(textToCheck))
            {
                continue;
            }

            // Check for match
            bool matches;
            if (rule.IsRegex && rule.CompiledPattern is not null)
            {
                try
                {
                    matches = rule.CompiledPattern.IsMatch(textToCheck);
                }
                catch (RegexMatchTimeoutException)
                {
                    _logger.LogWarning("Regex timeout for rule {Id}", rule.Id);
                    continue;
                }
            }
            else
            {
                // Simple contains check for non-regex
                matches = textToCheck.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase);
            }

            if (matches)
            {
                rule.MatchCount++;
                rule.LastMatch = DateTimeOffset.UtcNow;

                _logger.LogWarning("Spam filter match: rule {Id} matched {Target} from {Nick}",
                    rule.Id, rule.Target, context.Nickname);

                return new SpamFilterResult
                {
                    IsMatch = true,
                    MatchedRule = rule,
                    Action = rule.Action,
                    Reason = rule.Reason,
                    Duration = rule.Duration
                };
            }
        }

        return SpamFilterResult.NoMatch;
    }

    /// <summary>
    /// Checks if a rule target matches the context.
    /// </summary>
    private static bool TargetMatches(SpamFilterTarget target, SpamFilterContext context)
    {
        return target switch
        {
            SpamFilterTarget.Message => !string.IsNullOrEmpty(context.Message),
            SpamFilterTarget.PrivateMessage => !string.IsNullOrEmpty(context.Message) && context.IsPrivate,
            SpamFilterTarget.ChannelMessage => !string.IsNullOrEmpty(context.Message) && !context.IsPrivate,
            SpamFilterTarget.Notice => !string.IsNullOrEmpty(context.Message) && context.IsNotice,
            SpamFilterTarget.Part => !string.IsNullOrEmpty(context.PartQuitMessage),
            SpamFilterTarget.Quit => !string.IsNullOrEmpty(context.PartQuitMessage),
            SpamFilterTarget.Nick => !string.IsNullOrEmpty(context.Nickname),
            SpamFilterTarget.Topic => !string.IsNullOrEmpty(context.Topic),
            SpamFilterTarget.Away => !string.IsNullOrEmpty(context.AwayMessage),
            SpamFilterTarget.Realname => !string.IsNullOrEmpty(context.Realname),
            SpamFilterTarget.All => true,
            _ => false
        };
    }

    /// <summary>
    /// Gets the text to check for a given target.
    /// </summary>
    private static string? GetTextForTarget(SpamFilterTarget target, SpamFilterContext context)
    {
        return target switch
        {
            SpamFilterTarget.Message or
            SpamFilterTarget.PrivateMessage or
            SpamFilterTarget.ChannelMessage or
            SpamFilterTarget.Notice => context.Message,
            SpamFilterTarget.Part or SpamFilterTarget.Quit => context.PartQuitMessage,
            SpamFilterTarget.Nick => context.Nickname,
            SpamFilterTarget.Topic => context.Topic,
            SpamFilterTarget.Away => context.AwayMessage,
            SpamFilterTarget.Realname => context.Realname,
            SpamFilterTarget.All => $"{context.Nickname} {context.Message} {context.PartQuitMessage} {context.Topic} {context.AwayMessage} {context.Realname}",
            _ => null
        };
    }

    /// <summary>
    /// Gets filter statistics.
    /// </summary>
    public SpamFilterStats GetStats()
    {
        var rules = _rules.Values.ToList();
        return new SpamFilterStats
        {
            TotalRules = rules.Count,
            EnabledRules = rules.Count(r => r.IsEnabled),
            TotalMatches = rules.Sum(r => r.MatchCount),
            TopMatchingRules = rules
                .Where(r => r.MatchCount > 0)
                .OrderByDescending(r => r.MatchCount)
                .Take(10)
                .ToList()
        };
    }
}

/// <summary>
/// Interface for spam filtering.
/// </summary>
public interface ISpamFilter
{
    /// <summary>
    /// Checks a message against filter rules.
    /// </summary>
    /// <param name="context">The filter context.</param>
    /// <returns>The filter result.</returns>
    SpamFilterResult CheckMessage(SpamFilterContext context);
}

/// <summary>
/// A spam filter rule.
/// </summary>
public sealed class SpamFilterRule
{
    /// <summary>The rule ID.</summary>
    public required string Id { get; init; }

    /// <summary>The pattern to match.</summary>
    public required string Pattern { get; init; }

    /// <summary>Compiled regex (if applicable).</summary>
    public Regex? CompiledPattern { get; init; }

    /// <summary>Whether pattern is a regex.</summary>
    public bool IsRegex { get; init; }

    /// <summary>What to match against.</summary>
    public SpamFilterTarget Target { get; init; }

    /// <summary>Action to take on match.</summary>
    public SpamFilterAction Action { get; init; }

    /// <summary>Reason for the rule.</summary>
    public required string Reason { get; init; }

    /// <summary>Duration for temporary actions.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Whether the rule is enabled.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>When the rule was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Number of matches.</summary>
    public long MatchCount { get; set; }

    /// <summary>Last match time.</summary>
    public DateTimeOffset? LastMatch { get; set; }
}

/// <summary>
/// What a spam filter rule matches against.
/// </summary>
public enum SpamFilterTarget
{
    /// <summary>Any message (PRIVMSG).</summary>
    Message,

    /// <summary>Private messages only.</summary>
    PrivateMessage,

    /// <summary>Channel messages only.</summary>
    ChannelMessage,

    /// <summary>NOTICE messages.</summary>
    Notice,

    /// <summary>PART messages.</summary>
    Part,

    /// <summary>QUIT messages.</summary>
    Quit,

    /// <summary>Nickname changes.</summary>
    Nick,

    /// <summary>Channel topics.</summary>
    Topic,

    /// <summary>Away messages.</summary>
    Away,

    /// <summary>User realnames.</summary>
    Realname,

    /// <summary>All of the above.</summary>
    All
}

/// <summary>
/// Action to take when a filter matches.
/// </summary>
public enum SpamFilterAction
{
    /// <summary>Block the message.</summary>
    Block,

    /// <summary>Kill the user.</summary>
    Kill,

    /// <summary>G-line/K-line the user.</summary>
    Gline,

    /// <summary>Temporarily ban the user.</summary>
    TempBan,

    /// <summary>Warn the user.</summary>
    Warn,

    /// <summary>Log only.</summary>
    Log,

    /// <summary>Report to operators.</summary>
    Report
}

/// <summary>
/// Context for spam filter checking.
/// </summary>
public sealed class SpamFilterContext
{
    /// <summary>The user's nickname.</summary>
    public string? Nickname { get; init; }

    /// <summary>The user's username.</summary>
    public string? Username { get; init; }

    /// <summary>The user's hostname.</summary>
    public string? Hostname { get; init; }

    /// <summary>The message content.</summary>
    public string? Message { get; init; }

    /// <summary>Whether this is a private message.</summary>
    public bool IsPrivate { get; init; }

    /// <summary>Whether this is a NOTICE.</summary>
    public bool IsNotice { get; init; }

    /// <summary>PART/QUIT message.</summary>
    public string? PartQuitMessage { get; init; }

    /// <summary>Channel topic.</summary>
    public string? Topic { get; init; }

    /// <summary>Away message.</summary>
    public string? AwayMessage { get; init; }

    /// <summary>User's realname.</summary>
    public string? Realname { get; init; }

    /// <summary>Target channel or user.</summary>
    public string? Target { get; init; }

    /// <summary>User's account name (if registered).</summary>
    public string? Account { get; init; }
}

/// <summary>
/// Result of spam filter check.
/// </summary>
public readonly struct SpamFilterResult
{
    /// <summary>No match result.</summary>
    public static readonly SpamFilterResult NoMatch = new() { IsMatch = false };

    /// <summary>Whether a rule matched.</summary>
    public bool IsMatch { get; init; }

    /// <summary>The rule that matched.</summary>
    public SpamFilterRule? MatchedRule { get; init; }

    /// <summary>Action to take.</summary>
    public SpamFilterAction Action { get; init; }

    /// <summary>Reason for match.</summary>
    public string? Reason { get; init; }

    /// <summary>Duration for temporary actions.</summary>
    public TimeSpan? Duration { get; init; }
}

/// <summary>
/// Spam filter statistics.
/// </summary>
public sealed class SpamFilterStats
{
    /// <summary>Total number of rules.</summary>
    public int TotalRules { get; init; }

    /// <summary>Number of enabled rules.</summary>
    public int EnabledRules { get; init; }

    /// <summary>Total matches across all rules.</summary>
    public long TotalMatches { get; init; }

    /// <summary>Top matching rules.</summary>
    public required IReadOnlyList<SpamFilterRule> TopMatchingRules { get; init; }
}
