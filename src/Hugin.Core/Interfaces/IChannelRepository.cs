using Hugin.Core.Entities;
using Hugin.Core.ValueObjects;

namespace Hugin.Core.Interfaces;

/// <summary>
/// Repository for channel operations.
/// </summary>
public interface IChannelRepository
{
    /// <summary>
    /// Gets a channel by name.
    /// </summary>
    Channel? GetByName(ChannelName name);

    /// <summary>
    /// Gets all channels.
    /// </summary>
    IEnumerable<Channel> GetAll();

    /// <summary>
    /// Gets channels matching a search pattern.
    /// </summary>
    IEnumerable<Channel> Search(string pattern);

    /// <summary>
    /// Checks if a channel exists.
    /// </summary>
    bool Exists(ChannelName name);

    /// <summary>
    /// Creates a new channel.
    /// </summary>
    Channel Create(ChannelName name);

    /// <summary>
    /// Adds an existing channel.
    /// </summary>
    void Add(Channel channel);

    /// <summary>
    /// Removes a channel.
    /// </summary>
    void Remove(ChannelName name);

    /// <summary>
    /// Gets channels a user is in.
    /// </summary>
    IEnumerable<Channel> GetChannelsForUser(Guid connectionId);

    /// <summary>
    /// Gets the total channel count.
    /// </summary>
    int GetCount();

    /// <summary>
    /// Gets channels visible to a user (based on secret mode).
    /// </summary>
    IEnumerable<Channel> GetVisibleChannels(User? user);
}
