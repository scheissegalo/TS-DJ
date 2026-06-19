using TS_DJ.Core.Models;

namespace TS_DJ.Core.Services;

/// <summary>
/// Resolves a queue item into decoder-ready media at play time.
/// </summary>
public interface IMediaPlaybackLoader
{
    Task<MediaLoadResult> LoadAsync(
        PlaybackQueueItem item,
        IPlaybackStreamHandle? prefetchedStream = null,
        CancellationToken cancellationToken = default);
}
