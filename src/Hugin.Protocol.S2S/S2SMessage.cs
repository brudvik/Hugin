namespace Hugin.Protocol.S2S;

/// <summary>
/// Represents a server-to-server IRC message.
/// S2S messages use UID/SID prefixes instead of nick!user@host.
/// </summary>
/// <remarks>
/// S2S protocol differences from client protocol:
/// - Sources are SID (3 chars) for servers or UID (9 chars) for users
/// - Commands may have additional server-specific parameters
/// - Certain commands like SJOIN, UID, SID are S2S only
/// </remarks>
public sealed class S2SMessage
{
    /// <summary>
    /// Gets the source identifier (SID or UID).
    /// </summary>
    public string? Source { get; }

    /// <summary>
    /// Gets the command name.
    /// </summary>
    public string Command { get; }

    /// <summary>
    /// Gets the command parameters.
    /// </summary>
    public IReadOnlyList<string> Parameters { get; }

    /// <summary>
    /// Gets the message tags (IRCv3).
    /// </summary>
    public IReadOnlyDictionary<string, string?> Tags { get; }

    /// <summary>
    /// Creates a new S2S message.
    /// </summary>
    private S2SMessage(
        string? source,
        string command,
        IReadOnlyList<string> parameters,
        IReadOnlyDictionary<string, string?>? tags = null)
    {
        Source = source;
        Command = command;
        Parameters = parameters;
        Tags = tags ?? new Dictionary<string, string?>();
    }

    /// <summary>
    /// Creates a message without a source.
    /// </summary>
    public static S2SMessage Create(string command, params string[] parameters) =>
        new(null, command, parameters);

    /// <summary>
    /// Creates a message with a source.
    /// </summary>
    public static S2SMessage CreateWithSource(string source, string command, params string[] parameters) =>
        new(source, command, parameters);

    /// <summary>
    /// Creates a message with tags and source.
    /// </summary>
    public static S2SMessage CreateFull(
        IReadOnlyDictionary<string, string?> tags,
        string source,
        string command,
        params string[] parameters) =>
        new(source, command, parameters, tags);

    /// <summary>
    /// Tries to parse an S2S message from a raw line.
    /// </summary>
    public static bool TryParse(string line, out S2SMessage? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var span = line.AsSpan();
        Dictionary<string, string?>? tags = null;
        string? source = null;

        // Parse tags
        if (span.Length > 0 && span[0] == '@')
        {
            var tagEnd = span.IndexOf(' ');
            if (tagEnd == -1)
            {
                return false;
            }

            tags = ParseTags(span[1..tagEnd]);
            span = span[(tagEnd + 1)..].TrimStart();
        }

        // Parse source
        if (span.Length > 0 && span[0] == ':')
        {
            var sourceEnd = span.IndexOf(' ');
            if (sourceEnd == -1)
            {
                return false;
            }

            source = span[1..sourceEnd].ToString();
            span = span[(sourceEnd + 1)..].TrimStart();
        }

        // Parse command
        var commandEnd = span.IndexOf(' ');
        string command;
        if (commandEnd == -1)
        {
            command = span.ToString();
            message = new S2SMessage(source, command, Array.Empty<string>(), tags);
            return true;
        }

        command = span[..commandEnd].ToString();
        span = span[(commandEnd + 1)..];

        // Parse parameters
        var parameters = new List<string>();
        while (span.Length > 0)
        {
            if (span[0] == ':')
            {
                // Trailing parameter
                parameters.Add(span[1..].ToString());
                break;
            }

            var nextSpace = span.IndexOf(' ');
            if (nextSpace == -1)
            {
                parameters.Add(span.ToString());
                break;
            }

            parameters.Add(span[..nextSpace].ToString());
            span = span[(nextSpace + 1)..];
        }

        message = new S2SMessage(source, command, parameters, tags);
        return true;
    }

    private static Dictionary<string, string?> ParseTags(ReadOnlySpan<char> tagSpan)
    {
        var tags = new Dictionary<string, string?>();
        
        while (tagSpan.Length > 0)
        {
            var semicolon = tagSpan.IndexOf(';');
            ReadOnlySpan<char> tagPair;
            
            if (semicolon == -1)
            {
                tagPair = tagSpan;
                tagSpan = ReadOnlySpan<char>.Empty;
            }
            else
            {
                tagPair = tagSpan[..semicolon];
                tagSpan = tagSpan[(semicolon + 1)..];
            }

            var equals = tagPair.IndexOf('=');
            if (equals == -1)
            {
                tags[tagPair.ToString()] = null;
            }
            else
            {
                var key = tagPair[..equals].ToString();
                var value = UnescapeTagValue(tagPair[(equals + 1)..]);
                tags[key] = value;
            }
        }

        return tags;
    }

    private static string UnescapeTagValue(ReadOnlySpan<char> value)
    {
        var result = new char[value.Length];
        var resultIndex = 0;
        var i = 0;

        while (i < value.Length)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                result[resultIndex++] = value[i + 1] switch
                {
                    ':' => ';',
                    's' => ' ',
                    'r' => '\r',
                    'n' => '\n',
                    '\\' => '\\',
                    _ => value[i + 1]
                };
                i += 2;
            }
            else
            {
                result[resultIndex++] = value[i];
                i++;
            }
        }

        return new string(result, 0, resultIndex);
    }

    /// <summary>
    /// Serializes the message to wire format.
    /// </summary>
    public override string ToString()
    {
        var builder = new System.Text.StringBuilder();

        // Tags
        if (Tags.Count > 0)
        {
            builder.Append('@');
            var first = true;
            foreach (var (key, value) in Tags)
            {
                if (!first)
                {
                    builder.Append(';');
                }
                first = false;
                builder.Append(key);
                if (value != null)
                {
                    builder.Append('=');
                    builder.Append(EscapeTagValue(value));
                }
            }
            builder.Append(' ');
        }

        // Source
        if (Source != null)
        {
            builder.Append(':');
            builder.Append(Source);
            builder.Append(' ');
        }

        // Command
        builder.Append(Command);

        // Parameters
        for (var i = 0; i < Parameters.Count; i++)
        {
            builder.Append(' ');
            if (i == Parameters.Count - 1 && 
                (Parameters[i].Contains(' ') || Parameters[i].StartsWith(':')))
            {
                builder.Append(':');
            }
            builder.Append(Parameters[i]);
        }

        return builder.ToString();
    }

    private static string EscapeTagValue(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace(";", "\\:")
            .Replace(" ", "\\s")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }
}
