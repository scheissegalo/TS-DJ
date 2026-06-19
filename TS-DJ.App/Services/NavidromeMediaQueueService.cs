using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.App.Services;

/// <summary>
/// Enqueues Navidrome tracks through the playback target (queue or deck).
/// </summary>
public sealed class NavidromeMediaQueueService : INavidromeMediaQueueService
{
    private readonly INavidromeService _navidromeService;
    private readonly IPlaybackTargetService _playbackTarget;

    public NavidromeMediaQueueService(
        INavidromeService navidromeService,
        IPlaybackTargetService playbackTarget)
    {
        _navidromeService = navidromeService;
        _playbackTarget = playbackTarget;
    }

    public Task<int> EnqueueTracksAsync(IReadOnlyList<NavidromeTrack> tracks, bool playImmediately)
    {
        if (tracks.Count == 0)
            return Task.FromResult(-1);

        var items = tracks.Select(_navidromeService.CreateQueueItem).ToList();
        return _playbackTarget.LoadItemsAsync(items, playImmediately, replaceQueue: false);
    }
}
