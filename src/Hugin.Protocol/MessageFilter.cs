using System.Text;
using System.Text.RegularExpressions;

namespace Hugin.Protocol;

/// <summary>
/// Provides utilities for processing IRC message content.
/// </summary>
public static partial class MessageFilter
{
    /// <summary>
    /// The ASCII code for CTCP delimiter (0x01).
    /// </summary>
    public const char CtcpDelimiter = '\x01';

    /// <summary>
    /// Checks if a message is a CTCP message.
    /// </summary>
    /// <param name="message">The message text to check.</param>
    /// <returns>True if the message is a CTCP; otherwise false.</returns>
    public static bool IsCtcp(string message)
    {
        return message.Length >= 2 && 
               message[0] == CtcpDelimiter && 
               message[^1] == CtcpDelimiter;
    }

    /// <summary>
    /// Checks if a message is a CTCP ACTION (/me).
    /// </summary>
    /// <param name="message">The message text to check.</param>
    /// <returns>True if the message is a CTCP ACTION; otherwise false.</returns>
    public static bool IsCtcpAction(string message)
    {
        return IsCtcp(message) && 
               message.Length >= 9 && 
               message.AsSpan(1, 6).Equals("ACTION", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a message contains mIRC color codes.
    /// Color codes: 0x03 (color), 0x02 (bold), 0x1D (italic), 0x1F (underline), 0x16 (reverse), 0x0F (reset)
    /// </summary>
    /// <param name="message">The message text to check.</param>
    /// <returns>True if the message contains color/formatting codes; otherwise false.</returns>
    public static bool ContainsColorCodes(string message)
    {
        return ColorCodePattern().IsMatch(message);
    }

    /// <summary>
    /// Strips all mIRC color and formatting codes from a message.
    /// </summary>
    /// <param name="message">The message to strip.</param>
    /// <returns>The message with all color/formatting codes removed.</returns>
    public static string StripColorCodes(string message)
    {
        // Remove color codes with optional foreground/background: \x03[0-9]{1,2}(,[0-9]{1,2})?
        var result = ColorCodeWithDigitsPattern().Replace(message, string.Empty);
        
        // Remove remaining formatting codes
        return FormattingCodesPattern().Replace(result, string.Empty);
    }

    /// <summary>
    /// Pattern to detect any color/formatting codes.
    /// </summary>
    [GeneratedRegex(@"[\x02\x03\x0F\x16\x1D\x1F]")]
    private static partial Regex ColorCodePattern();

    /// <summary>
    /// Pattern to match color codes with optional digit parameters.
    /// </summary>
    [GeneratedRegex(@"\x03(\d{1,2}(,\d{1,2})?)?")]
    private static partial Regex ColorCodeWithDigitsPattern();

    /// <summary>
    /// Pattern to match remaining formatting codes.
    /// </summary>
    [GeneratedRegex(@"[\x02\x0F\x16\x1D\x1F]")]
    private static partial Regex FormattingCodesPattern();
}
