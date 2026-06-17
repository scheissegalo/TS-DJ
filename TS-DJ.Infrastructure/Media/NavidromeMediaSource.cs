using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.Navidrome;

namespace TS_DJ.Infrastructure.Media;

public sealed class NavidromeMediaSource : IMediaSource
{
    private readonly NavidromeService _navidromeService;

    public NavidromeMediaSource(NavidromeService navidromeService)
    {
        _navidromeService = navidromeService;
    }

    public PlaybackSourceKind SourceKind => PlaybackSourceKind.RemoteStream;

    public bool CanHandleInput(string input) => false;

    public bool CanHandleItem(PlaybackQueueItem item) =>
        item.SourceKind == PlaybackSourceKind.RemoteStream;

    public void ValidateForEnqueue(PlaybackQueueItem item)
    {
        if (item.SourceKind != PlaybackSourceKind.RemoteStream)
            throw new ArgumentException("Not a Navidrome queue item.", nameof(item));

        if (string.IsNullOrWhiteSpace(item.RemoteTrackId))
            throw new ArgumentException("Remote queue items require a track id.", nameof(item));

        if (!_navidromeService.IsAuthenticated)
            throw new InvalidOperationException("Navidrome is not authenticated.");
    }

    public Task<PlaybackQueueItem?> TryCreateFromInputAsync(string input, CancellationToken cancellationToken = default) =>
        Task.FromResult<PlaybackQueueItem?>(null);

    public Task<string?> ResolveStreamUrlAsync(PlaybackQueueItem item, CancellationToken cancellationToken = default)
    {
        if (!CanHandleItem(item))
            return Task.FromResult<string?>(null);

        if (string.IsNullOrWhiteSpace(item.RemoteTrackId) || !_navidromeService.IsAuthenticated)
            return Task.FromResult<string?>(null);

        return Task.FromResult<string?>(_navidromeService.BuildStreamUrl(item.RemoteTrackId));
    }
}
