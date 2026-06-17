using TS_DJ.Core.Models;

namespace TS_DJ.Core.Services;

/// <summary>
/// Pluggable media origin (local files, Navidrome, YouTube, etc.).
/// </summary>
public interface IMediaSource
{
    PlaybackSourceKind SourceKind { get; }

    bool CanHandleInput(string input);

    bool CanHandleItem(PlaybackQueueItem item);

    void ValidateForEnqueue(PlaybackQueueItem item);

    Task<PlaybackQueueItem?> TryCreateFromInputAsync(string input, CancellationToken cancellationToken = default);

    Task<string?> ResolveStreamUrlAsync(PlaybackQueueItem item, CancellationToken cancellationToken = default);
}
