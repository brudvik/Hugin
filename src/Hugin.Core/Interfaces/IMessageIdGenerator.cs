namespace Hugin.Core.Interfaces;

/// <summary>
/// Service for generating unique message IDs.
/// </summary>
public interface IMessageIdGenerator
{
    /// <summary>
    /// Generates a unique message ID.
    /// The format follows IRCv3 msgid requirements.
    /// </summary>
    string Generate();
}
