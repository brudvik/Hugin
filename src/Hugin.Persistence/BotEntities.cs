using Hugin.Core.Entities;

namespace Hugin.Persistence;

/// <summary>
/// Entity Framework entity for Bot.
/// </summary>
public sealed class BotEntity
{
    public Guid Id { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string Ident { get; set; } = string.Empty;
    public string Realname { get; set; } = string.Empty;
    public string Host { get; set; } = "services.bot";
    public DateTimeOffset CreatedAt { get; set; }
    public string Uid { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Converts the entity to a domain model.
    /// </summary>
    public Bot ToDomain()
    {
        return new Bot(Id, Nickname, Ident, Realname, Uid)
        {
            Host = Host,
            CreatedAt = CreatedAt,
            IsActive = IsActive
        };
    }
}

/// <summary>
/// Entity Framework entity for ChannelBot.
/// </summary>
public sealed class ChannelBotEntity
{
    public Guid Id { get; set; }
    public Guid BotId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public Guid AssignedBy { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public string? GreetMessage { get; set; }
    public bool AutoGreet { get; set; }

    /// <summary>
    /// Converts the entity to a domain model.
    /// </summary>
    public ChannelBot ToDomain()
    {
        return new ChannelBot(Id, BotId, ChannelName, AssignedBy)
        {
            AssignedAt = AssignedAt,
            GreetMessage = GreetMessage,
            AutoGreet = AutoGreet
        };
    }
}
