using TS_DJ.Core.Models;

namespace TS_DJ.Core.Services;

public interface ITeamSpeakService
{
    ConnectionState State { get; }

    event EventHandler<ConnectionState>? StateChanged;
    event EventHandler<string>? StatusMessage;
    event EventHandler<TeamSpeakChannelInfo>? CurrentChannelChanged;
    event EventHandler<IReadOnlyList<TeamSpeakChannelInfo>>? ChannelsUpdated;

    Task ConnectAsync(ConnectionSettings settings, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task SetNicknameAsync(string nickname, CancellationToken cancellationToken = default);
    Task SetDescriptionAsync(string description, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeamSpeakChannelInfo>> GetChannelsAsync(
        bool includeEmpty,
        CancellationToken cancellationToken = default);
    Task MoveToChannelAsync(string channelId, CancellationToken cancellationToken = default);
    TeamSpeakChannelInfo? GetCurrentChannel();
}
