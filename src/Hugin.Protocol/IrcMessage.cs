using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Hugin.Protocol;

/// <summary>
/// Represents a parsed IRC message following the IRCv3 message format.
/// Format: [@tags] [:source] command [params] [:trailing]
/// </summary>
public sealed class IrcMessage
{
    /// <summary>
    /// Maximum message length (512 bytes for traditional IRC, 4096 with message-tags).
    /// </summary>
    public const int MaxLengthWithTags = 4096;
    public const int MaxLengthWithoutTags = 512;
    public const int MaxTagsLength = 4096;
    public const int MaxParams = 15;

    /// <summary>
    /// Gets the message tags (IRCv3).
    /// </summary>
    public IReadOnlyDictionary<string, string?> Tags { get; }

    /// <summary>
    /// Gets the message source (prefix).
    /// </summary>
    public string? Source { get; }

    /// <summary>
    /// Gets the command (e.g., PRIVMSG, JOIN, 001).
    /// </summary>
    public string Command { get; }

    /// <summary>
    /// Gets the command parameters.
    /// </summary>
    public IReadOnlyList<string> Parameters { get; }

    /// <summary>
    /// Gets whether this message has tags.
    /// </summary>
    public bool HasTags => Tags.Count > 0;

    /// <summary>
    /// Gets the trailing parameter (last parameter, usually the message content).
    /// </summary>
    public string? Trailing => Parameters.Count > 0 ? Parameters[^1] : null;

    private IrcMessage(
        IReadOnlyDictionary<string, string?> tags,
        string? source,
        string command,
        IReadOnlyList<string> parameters)
    {
        Tags = tags;
        Source = source;
        Command = command;
        Parameters = parameters;
    }

    /// <summary>
    /// Creates a new IRC message without source.
    /// </summary>
    public static IrcMessage Create(string command, params string[] parameters)
    {
        return new IrcMessage(
            new Dictionary<string, string?>(),
            null,
            command.ToUpperInvariant(),
            parameters);
    }

    /// <summary>
    /// Creates a new IRC message with a source.
    /// Use CreateWithSource for explicit source, or Create for messages without source.
    /// </summary>
    public static IrcMessage CreateWithSource(
        string source,
        string command,
        params string[] parameters)
    {
        return new IrcMessage(
            new Dictionary<string, string?>(),
            source,
            command.ToUpperInvariant(),
            parameters);
    }

    /// <summary>
    /// Creates a new IRC message with tags and source.
    /// </summary>
    public static IrcMessage CreateFull(
        IReadOnlyDictionary<string, string?> tags,
        string? source,
        string command,
        params string[] parameters)
    {
        return new IrcMessage(
            tags,
            source,
            command.ToUpperInvariant(),
            parameters);
    }

    /// <summary>
    /// Parses an IRC message from a string.
    /// </summary>
    public static bool TryParse(string line, [NotNullWhen(true)] out IrcMessage? message)
    {
        return TryParse(line.AsSpan(), out message);
    }

    /// <summary>
    /// Parses an IRC message from a span.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> line, [NotNullWhen(true)] out IrcMessage? message)
    {
        message = null;

        if (line.IsEmpty)
        {
            return false;
        }

        // Remove trailing CRLF if present
        if (line.EndsWith("\r\n".AsSpan()))
        {
            line = line[..^2];
        }
        else if (line.EndsWith("\n".AsSpan()) || line.EndsWith("\r".AsSpan()))
        {
            line = line[..^1];
        }

        if (line.IsEmpty)
        {
            return false;
        }

        var tags = new Dictionary<string, string?>();
        string? source = null;
        string command;
        var parameters = new List<string>();

        int position = 0;

        // Parse tags (if present)
        if (line[0] == '@')
        {
            int tagEnd = line.IndexOf(' ');
            if (tagEnd == -1)
            {
                return false;
            }

            var tagSpan = line[1..tagEnd];
            ParseTags(tagSpan, tags);
            position = tagEnd + 1;

            // Skip any additional spaces
            while (position < line.Length && line[position] == ' ')
            {
                position++;
            }
        }

        if (position >= line.Length)
        {
            return false;
        }

        line = line[position..];
        position = 0;

        // Parse source/prefix (if present)
        if (line[0] == ':')
        {
            int sourceEnd = line.IndexOf(' ');
            if (sourceEnd == -1)
            {
                return false;
            }

            source = line[1..sourceEnd].ToString();
            position = sourceEnd + 1;

            // Skip any additional spaces
            while (position < line.Length && line[position] == ' ')
            {
                position++;
            }
        }

        if (position >= line.Length)
        {
            return false;
        }

        line = line[position..];

        // Parse command
        int commandEnd = line.IndexOf(' ');
        if (commandEnd == -1)
        {
            command = line.ToString().ToUpperInvariant();
        }
        else
        {
            command = line[..commandEnd].ToString().ToUpperInvariant();
            line = line[(commandEnd + 1)..];

            // Parse parameters
            while (!line.IsEmpty && parameters.Count < MaxParams)
            {
                // Skip leading spaces
                while (!line.IsEmpty && line[0] == ' ')
                {
                    line = line[1..];
                }

                if (line.IsEmpty)
                {
                    break;
                }

                // Trailing parameter
                if (line[0] == ':')
                {
                    parameters.Add(line[1..].ToString());
                    break;
                }

                // Regular parameter
                int paramEnd = line.IndexOf(' ');
                if (paramEnd == -1)
                {
                    parameters.Add(line.ToString());
                    break;
                }
                else
                {
                    parameters.Add(line[..paramEnd].ToString());
                    line = line[(paramEnd + 1)..];
                }
            }
        }

        if (string.IsNullOrEmpty(command))
        {
            return false;
        }

        message = new IrcMessage(tags, source, command, parameters);
        return true;
    }

    private static void ParseTags(ReadOnlySpan<char> tagSpan, Dictionary<string, string?> tags)
    {
        while (!tagSpan.IsEmpty)
        {
            int nextTag = tagSpan.IndexOf(';');
            ReadOnlySpan<char> tag;

            if (nextTag == -1)
            {
                tag = tagSpan;
                tagSpan = ReadOnlySpan<char>.Empty;
            }
            else
            {
                tag = tagSpan[..nextTag];
                tagSpan = tagSpan[(nextTag + 1)..];
            }

            if (tag.IsEmpty)
            {
                continue;
            }

            int equals = tag.IndexOf('=');
            if (equals == -1)
            {
                tags[tag.ToString()] = null;
            }
            else
            {
                string key = tag[..equals].ToString();
                string value = UnescapeTagValue(tag[(equals + 1)..]);
                tags[key] = value;
            }
        }
    }

    private static string UnescapeTagValue(ReadOnlySpan<char> value)
    {
        var sb = new StringBuilder(value.Length);
        bool escape = false;

        foreach (char c in value)
        {
            if (escape)
            {
                sb.Append(c switch
                {
                    ':' => ';',
                    's' => ' ',
                    '\\' => '\\',
                    'r' => '\r',
                    'n' => '\n',
                    _ => c
                });
                escape = false;
            }
            else if (c == '\\')
            {
                escape = true;
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static string EscapeTagValue(string value)
    {
        var sb = new StringBuilder(value.Length);

        foreach (char c in value)
        {
            sb.Append(c switch
            {
                ';' => "\\:",
                ' ' => "\\s",
                '\\' => "\\\\",
                '\r' => "\\r",
                '\n' => "\\n",
                _ => c.ToString()
            });
        }

        return sb.ToString();
    }

    /// <summary>
    /// Serializes the message to a string.
    /// </summary>
    public override string ToString()
    {
        var sb = new StringBuilder();

        // Tags
        if (Tags.Count > 0)
        {
            sb.Append('@');
            bool first = true;
            foreach (var (key, value) in Tags)
            {
                if (!first)
                {
                    sb.Append(';');
                }
                first = false;

                sb.Append(key);
                if (value is not null)
                {
                    sb.Append('=');
                    sb.Append(EscapeTagValue(value));
                }
            }
            sb.Append(' ');
        }

        // Source
        if (Source is not null)
        {
            sb.Append(':');
            sb.Append(Source);
            sb.Append(' ');
        }

        // Command
        sb.Append(Command);

        // Parameters
        for (int i = 0; i < Parameters.Count; i++)
        {
            sb.Append(' ');

            // Last parameter always gets colon prefix for consistency and safety
            // This is the common practice in IRC implementations
            if (i == Parameters.Count - 1)
            {
                sb.Append(':');
            }

            sb.Append(Parameters[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets the raw bytes for sending over the wire.
    /// </summary>
    public byte[] ToBytes()
    {
        return Encoding.UTF8.GetBytes(ToString() + "\r\n");
    }

    /// <summary>
    /// Creates a copy with additional/modified tags.
    /// </summary>
    public IrcMessage WithTags(IReadOnlyDictionary<string, string?> additionalTags)
    {
        var newTags = new Dictionary<string, string?>(Tags);
        foreach (var (key, value) in additionalTags)
        {
            newTags[key] = value;
        }
        return new IrcMessage(newTags, Source, Command, Parameters);
    }

    /// <summary>
    /// Creates a copy with a specific source.
    /// </summary>
    public IrcMessage WithSource(string source)
    {
        return new IrcMessage(Tags, source, Command, Parameters);
    }
}
