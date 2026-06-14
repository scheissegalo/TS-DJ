using TSLib;
using TSLib.Full.Book;
using TS_DJ.Core.Models;

namespace TS_DJ.TeamSpeak;

internal static class TeamSpeakChannelMapper
{
    public static TeamSpeakChannelInfo ToInfo(
        ChannelId channelId,
        string name,
        IReadOnlyDictionary<ChannelId, Channel> bookChannels,
        int totalClients)
    {
        var id = channelId.ToPath();
        var displayPath = bookChannels.Count > 0
            ? BuildDisplayPath(channelId, bookChannels)
            : name;

        return new TeamSpeakChannelInfo(id, name, displayPath, totalClients);
    }

    public static TeamSpeakChannelInfo? FromCurrentChannel(
        Channel? channel,
        IReadOnlyDictionary<ChannelId, Channel> bookChannels)
    {
        if (channel is null)
            return null;

        return ToInfo(channel.Id, channel.Name, bookChannels, totalClients: 0);
    }

    public static string BuildDisplayPath(ChannelId id, IReadOnlyDictionary<ChannelId, Channel> channels)
    {
        var parts = new List<string>();
        var current = id;
        var visited = new HashSet<ChannelId>();

        while (current != ChannelId.Null
               && channels.TryGetValue(current, out var channel)
               && visited.Add(current))
        {
            parts.Add(channel.Name);
            current = channel.Parent;
        }

        parts.Reverse();
        return parts.Count > 0 ? string.Join('/', parts) : id.ToPath();
    }

    public static ChannelId ParseChannelId(string channelId)
    {
        channelId = channelId.Trim();
        if (channelId.StartsWith('/'))
            channelId = channelId[1..];

        if (!ulong.TryParse(channelId, out var value))
            throw new ArgumentException($"Invalid channel id: {channelId}", nameof(channelId));

        return new ChannelId(value);
    }
}
