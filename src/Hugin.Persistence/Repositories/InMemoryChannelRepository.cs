using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using System.Collections.Concurrent;

namespace Hugin.Persistence.Repositories;

/// <summary>
/// In-memory implementation of channel repository.
/// </summary>
public sealed class InMemoryChannelRepository : IChannelRepository
{
    private readonly ConcurrentDictionary<string, Channel> _channels = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Channel? GetByName(ChannelName name)
    {
        return _channels.GetValueOrDefault(name.Value);
    }

    /// <inheritdoc />
    public IEnumerable<Channel> GetAll()
    {
        return _channels.Values;
    }

    /// <inheritdoc />
    public IEnumerable<Channel> Search(string pattern)
    {
        // Simple wildcard matching
        if (string.IsNullOrEmpty(pattern) || pattern == "*")
        {
            return _channels.Values;
        }

        if (pattern.Contains('*'))
        {
            var regex = new System.Text.RegularExpressions.Regex(
                "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return _channels.Values.Where(c => regex.IsMatch(c.Name.Value));
        }

        var channel = _channels.GetValueOrDefault(pattern);
        return channel is not null ? new[] { channel } : Enumerable.Empty<Channel>();
    }

    public bool Exists(ChannelName name)
    {
        return _channels.ContainsKey(name.Value);
    }

    public Channel Create(ChannelName name)
    {
        var channel = new Channel(name);
        if (!_channels.TryAdd(name.Value, channel))
        {
            throw new InvalidOperationException($"Channel {name.Value} already exists");
        }
        return channel;
    }

    public void Add(Channel channel)
    {
        if (!_channels.TryAdd(channel.Name.Value, channel))
        {
            throw new InvalidOperationException($"Channel {channel.Name.Value} already exists");
        }
    }

    public void Remove(ChannelName name)
    {
        _channels.TryRemove(name.Value, out _);
    }

    public IEnumerable<Channel> GetChannelsForUser(Guid connectionId)
    {
        return _channels.Values.Where(c => c.HasMember(connectionId));
    }

    public int GetCount()
    {
        return _channels.Count;
    }

    public IEnumerable<Channel> GetVisibleChannels(User? user)
    {
        if (user is null)
        {
            // Only return non-secret channels
            return _channels.Values.Where(c => !c.Modes.HasFlag(Core.Enums.ChannelMode.Secret));
        }

        // Return all channels the user is on, plus non-secret channels
        return _channels.Values.Where(c =>
            c.HasMember(user.ConnectionId) ||
            !c.Modes.HasFlag(Core.Enums.ChannelMode.Secret));
    }
}
