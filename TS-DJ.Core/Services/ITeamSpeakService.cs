using TS_DJ.Core.Models;

namespace TS_DJ.Core.Services;

public interface ITeamSpeakService
{
    ConnectionState State { get; }

    event EventHandler<ConnectionState>? StateChanged;
    event EventHandler<string>? StatusMessage;

    Task ConnectAsync(ConnectionSettings settings, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task SetNicknameAsync(string nickname, CancellationToken cancellationToken = default);
}
