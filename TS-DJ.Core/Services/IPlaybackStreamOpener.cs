using TS_DJ.Core.Models;

namespace TS_DJ.Core.Services;

/// <summary>
/// Opens buffered playback streams for sources that cannot expose a direct HTTP URL (e.g. YouTube via yt-dlp).
/// </summary>
public interface IPlaybackStreamOpener
{
    bool CanOpen(PlaybackQueueItem item);

    Task<IPlaybackStreamHandle?> OpenPlaybackStreamAsync(
        PlaybackQueueItem item,
        CancellationToken cancellationToken = default);
}
