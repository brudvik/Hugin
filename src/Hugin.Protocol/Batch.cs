using System.Text;

namespace Hugin.Protocol;

/// <summary>
/// Handles IRCv3 BATCH command for grouping related messages.
/// </summary>
public sealed class Batch
{
    /// <summary>
    /// Gets the batch reference tag.
    /// </summary>
    public string Reference { get; }

    /// <summary>
    /// Gets the batch type.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Gets the batch parameters.
    /// </summary>
    public IReadOnlyList<string> Parameters { get; }

    private readonly List<IrcMessage> _messages = new();

    /// <summary>
    /// Gets the messages in this batch.
    /// </summary>
    public IReadOnlyList<IrcMessage> Messages => _messages;

    private static int _counter;

    /// <summary>
    /// Creates a new message batch.
    /// </summary>
    /// <param name="type">The batch type (e.g., "chathistory", "netjoin").</param>
    /// <param name="parameters">Additional batch parameters.</param>
    public Batch(string type, params string[] parameters)
    {
        Reference = GenerateReference();
        Type = type;
        Parameters = parameters;
    }

    private static string GenerateReference()
    {
        var id = Interlocked.Increment(ref _counter);
        return Convert.ToBase64String(BitConverter.GetBytes(id))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Adds a message to the batch.
    /// </summary>
    public void AddMessage(IrcMessage message)
    {
        // Add batch tag to the message
        var tags = new Dictionary<string, string?>(message.Tags)
        {
            ["batch"] = Reference
        };
        _messages.Add(IrcMessage.CreateFull(tags, message.Source, message.Command, message.Parameters.ToArray()));
    }

    /// <summary>
    /// Creates the BATCH start message.
    /// </summary>
    public IrcMessage CreateStartMessage(string serverName)
    {
        var args = new List<string> { "+" + Reference, Type };
        args.AddRange(Parameters);
        return IrcMessage.CreateWithSource(serverName, "BATCH", args.ToArray());
    }

    /// <summary>
    /// Creates the BATCH end message.
    /// </summary>
    public IrcMessage CreateEndMessage(string serverName)
    {
        return IrcMessage.CreateWithSource(serverName, "BATCH", "-" + Reference);
    }

    /// <summary>
    /// Gets all messages including start and end.
    /// </summary>
    public IEnumerable<IrcMessage> GetAllMessages(string serverName)
    {
        yield return CreateStartMessage(serverName);
        foreach (var msg in _messages)
        {
            yield return msg;
        }
        yield return CreateEndMessage(serverName);
    }
}

/// <summary>
/// Standard batch types.
/// </summary>
public static class BatchTypes
{
    /// <summary>
    /// Network connection batch (NETSPLIT/NETJOIN).
    /// </summary>
    public const string Netjoin = "netjoin";
    public const string Netsplit = "netsplit";

    /// <summary>
    /// Chat history playback.
    /// </summary>
    public const string Chathistory = "chathistory";

    /// <summary>
    /// Labeled response.
    /// </summary>
    public const string LabeledResponse = "labeled-response";

    /// <summary>
    /// Draft multiline messages.
    /// </summary>
    public const string DraftMultiline = "draft/multiline";
}
