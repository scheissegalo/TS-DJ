using TS_DJ.Core.Models;

namespace TS_DJ.Core.Services;

public interface IAudioPlaybackService
{
    PlaybackState State { get; }
    string? CurrentFilePath { get; }
    float Volume { get; set; }

    event EventHandler<PlaybackState>? StateChanged;

    Task LoadAsync(string filePath, CancellationToken cancellationToken = default);
    Task PlayAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
