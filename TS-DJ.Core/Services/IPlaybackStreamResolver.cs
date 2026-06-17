using TS_DJ.Core.Models;

namespace TS_DJ.Core.Services;

/// <summary>
/// Resolves remote playback URLs at play time (e.g. fresh Subsonic auth tokens).
/// </summary>
public interface IPlaybackStreamResolver
{
    Task<string?> ResolveStreamUrlAsync(PlaybackQueueItem item, CancellationToken cancellationToken = default);
}
