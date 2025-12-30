namespace Hugin.Core.Entities;

/// <summary>
/// Represents a bot that can be assigned to channels.
/// </summary>
public sealed class Bot
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the bot's nickname.
    /// </summary>
    public string Nickname { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the bot's ident/username.
    /// </summary>
    public string Ident { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the bot's realname/gecos.
    /// </summary>
    public string Realname { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the bot's hostmask.
    /// </summary>
    public string Host { get; set; } = "services.bot";

    /// <summary>
    /// Gets or sets when the bot was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the bot's unique server ID (UID).
    /// </summary>
    public string Uid { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the bot is active/enabled.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Creates a new bot instance.
    /// </summary>
    public Bot()
    {
    }

    /// <summary>
    /// Creates a new bot with the specified values.
    /// </summary>
    /// <param name="id">Bot identifier.</param>
    /// <param name="nickname">Bot nickname.</param>
    /// <param name="ident">Bot ident.</param>
    /// <param name="realname">Bot realname.</param>
    /// <param name="uid">Bot UID.</param>
    public Bot(Guid id, string nickname, string ident, string realname, string uid)
    {
        Id = id;
        Nickname = nickname;
        Ident = ident;
        Realname = realname;
        Uid = uid;
        Host = "services.bot";
        CreatedAt = DateTimeOffset.UtcNow;
        IsActive = true;
    }
}
